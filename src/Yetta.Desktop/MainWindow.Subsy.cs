using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Yetta.Core;

namespace Yetta.Desktop;

public partial class MainWindow
{
    private sealed class PromptState(SubsyPrompt prompt, Func<object?, Task> answer, string source)
    {
        internal SubsyPrompt Prompt { get; } = prompt;
        internal Func<object?, Task> Answer { get; } = answer;
        internal string Source { get; } = source;
        internal string? Result { get; set; }
        internal bool Sending { get; set; }
        internal event Action? Changed;
        internal void Refresh() => Changed?.Invoke();
        internal void Subscribe(FrameworkElement element, Action refresh)
        {
            element.Loaded += (_, _) => { Changed += refresh; refresh(); };
            element.Unloaded += (_, _) => Changed -= refresh;
        }
    }

    private readonly Dictionary<Guid, ConsoleEntry> _pendingPrompts = [];

    private Border SubsyCard(UIElement child) => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(35, 42, 48)),
        CornerRadius = new CornerRadius(7), Padding = new Thickness(14, 11, 14, 10),
        Margin = new Thickness(0, 1, 0, 9), Child = child
    };

    private Button SubsyButton(string label, Action click, bool primary = false)
    {
        var button = ActionButton(label, click, primary);
        if (!primary)
        {
            button.Background = new SolidColorBrush(Color.FromRgb(59, 67, 75));
            button.Foreground = Brushes.White;
        }
        button.MinWidth = 0;
        button.MinHeight = 34;
        button.Padding = new Thickness(12, 5, 12, 5);
        button.Margin = new Thickness(0, 4, 7, 4);
        return button;
    }

    private UIElement RenderPrompt(PromptState state)
    {
        var request = state.Prompt;
        var root = new StackPanel();
        root.Children.Add(LiteralConsoleLine("Subsy  ·  " + state.Source));
        var label = LiteralConsoleLine(request.Label, Brushes.White);
        label.FontSize = 15;
        root.Children.Add(label);
        var form = new StackPanel();
        root.Children.Add(form);
        var status = LiteralConsoleLine("");
        status.Visibility = Visibility.Collapsed;
        root.Children.Add(status);
        var buttons = new WrapPanel();
        var refresh = () =>
        {
            form.IsEnabled = !state.Sending;
            form.Visibility = state.Result is null ? Visibility.Visible : Visibility.Collapsed;
            if (state.Result is not null || state.Sending)
            {
                status.Text = state.Result ?? "Отправка…";
                status.Visibility = Visibility.Visible;
            }
            else if (status.Text == "Отправка…") status.Visibility = Visibility.Collapsed;
        };
        state.Subscribe(root, refresh);
        refresh();

        async void Submit(object? value)
        {
            if (state.Result is not null || state.Sending) return;
            state.Sending = true;
            state.Refresh();
            try
            {
                await state.Answer(value);
                state.Result ??= value is null ? "Отменено" : "Ответ принят";
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException)
            {
                // A failed write may have already reached the process. Never resend implicitly.
                state.Result ??= "Не удалось передать ответ";
            }
            finally
            {
                state.Sending = false;
                state.Refresh();
            }
        }

        void Feedback(string message)
        {
            status.Text = message;
            status.Visibility = Visibility.Visible;
        }

        void Add(string title, Action action, bool primary = false) =>
            buttons.Children.Add(SubsyButton(title, action, primary));

        TextBox Field(string name)
        {
            var field = new TextBox
            {
                MaxLength = 512, Padding = new Thickness(8), Margin = new Thickness(0, 2, 0, 6),
                Background = new SolidColorBrush(Color.FromRgb(53, 61, 69)), Foreground = Brushes.White,
                CaretBrush = Brushes.White, BorderThickness = new Thickness(0), MinHeight = 32
            };
            System.Windows.Automation.AutomationProperties.SetName(field, name);
            form.Children.Add(field);
            return field;
        }

        switch (request.Kind)
        {
            case SubsyPromptKind.Confirm:
            case SubsyPromptKind.Expandable:
                Add("Да", () => Submit("y"));
                Add("Нет", () => Submit("n"));
                if (request.Kind == SubsyPromptKind.Expandable)
                {
                    Add("Да для всех", () => Submit("a"));
                    Add("Справка", () => Feedback(request.Help ?? "«Да для всех» передаёт выбор для серии действий."));
                }
                break;

            case SubsyPromptKind.Number:
            case SubsyPromptKind.Text:
            {
                var input = Field(request.Label);
                void Accept()
                {
                    string value = input.Text.Trim();
                    if (value.Length == 0) { Feedback("Заполни поле."); return; }
                    if (request.Kind == SubsyPromptKind.Number)
                    {
                        if (value.Length > 24 || !value.All(char.IsAsciiDigit))
                        {
                            Feedback("Введи только цифры от 0 до 9.");
                            return;
                        }
                    }
                    if (state.Source == "Marvine" &&
                        (value.Length > 16 || !value.All(char.IsAsciiLetterOrDigit)))
                    {
                        Feedback("ID должен содержать от 1 до 16 латинских букв или цифр.");
                        return;
                    }
                    Submit(value);
                }
                input.KeyDown += (_, args) => { if (args.Key == Key.Enter) { args.Handled = true; Accept(); } };
                Add("Отправить", Accept, true);
                break;
            }

            case SubsyPromptKind.File:
            {
                var path = LiteralConsoleLine("Файл не выбран");
                string? selected = null;
                form.Children.Add(path);
                var send = SubsyButton("Отправить путь", () => { if (selected is not null) Submit(selected); }, true);
                send.IsEnabled = false;
                Add("Выбрать файл…", () =>
                {
                    var picker = new OpenFileDialog { Title = request.Label, CheckFileExists = true };
                    if (picker.ShowDialog(this) != true) return;
                    selected = picker.FileName;
                    path.Text = selected;
                    send.IsEnabled = true;
                });
                buttons.Children.Add(send);
                break;
            }

            case SubsyPromptKind.Password:
            {
                form.Children.Add(LiteralConsoleLine("Введённое значение получит указанный выше источник."));
                var secret = new PasswordBox
                {
                    MaxLength = 256, PasswordChar = '*', Padding = new Thickness(8),
                    Background = new SolidColorBrush(Color.FromRgb(53, 61, 69)),
                    Foreground = Brushes.White, BorderThickness = new Thickness(0), MinHeight = 32
                };
                System.Windows.Automation.AutomationProperties.SetName(secret, request.Label);
                form.Children.Add(secret);
                TextBox? revealed = null;
                if (request.Reveal)
                {
                    revealed = Field("Пароль");
                    revealed.MaxLength = 256;
                    revealed.Visibility = Visibility.Collapsed;
                    var show = new CheckBox { Content = "Показать пароль", Foreground = Brushes.White,
                        Margin = new Thickness(0, 5, 0, 4) };
                    show.Checked += (_, _) =>
                    {
                        revealed.Text = secret.Password;
                        secret.Clear();
                        secret.Visibility = Visibility.Collapsed;
                        revealed.Visibility = Visibility.Visible;
                    };
                    show.Unchecked += (_, _) =>
                    {
                        secret.Password = revealed.Text;
                        revealed.Clear();
                        revealed.Visibility = Visibility.Collapsed;
                        secret.Visibility = Visibility.Visible;
                    };
                    form.Children.Add(show);
                }
                Add("Отправить", () =>
                {
                    string value = revealed?.Visibility == Visibility.Visible ? revealed.Text : secret.Password;
                    if (value.Length == 0) { Feedback("Введи пароль."); return; }
                    secret.Clear();
                    revealed?.Clear();
                    Submit(value);
                }, true);
                root.Unloaded += (_, _) => { secret.Clear(); revealed?.Clear(); };
                break;
            }

            case SubsyPromptKind.Select:
            case SubsyPromptKind.Autocomplete:
            {
                TextBox? filter = request.Kind == SubsyPromptKind.Autocomplete ? Field("Фильтр вариантов") : null;
                var choices = new WrapPanel();
                form.Children.Add(choices);
                void Filter()
                {
                    string query = filter?.Text.Trim() ?? "";
                    choices.Children.Clear();
                    foreach (string option in request.Options.Where(v => v.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
                        choices.Children.Add(SubsyButton(option, () => Submit(option)));
                    if (choices.Children.Count == 0) choices.Children.Add(LiteralConsoleLine("Нет совпадений"));
                }
                if (filter is not null) filter.TextChanged += (_, _) => Filter();
                Filter();
                break;
            }

            case SubsyPromptKind.Multi:
            {
                var boxes = new List<CheckBox>();
                var count = LiteralConsoleLine("");
                form.Children.Add(count);
                void Count()
                {
                    int checkedCount = boxes.Count(box => box.IsChecked == true);
                    count.Text = $"Выбрано {checkedCount} из {request.Limit}";
                    foreach (var box in boxes) box.IsEnabled = box.IsChecked == true || checkedCount < request.Limit;
                }
                foreach (string option in request.Options)
                {
                    var box = new CheckBox { Content = option, Foreground = Brushes.White,
                        Margin = new Thickness(0, 0, 0, 6) };
                    box.Checked += (_, _) => Count();
                    box.Unchecked += (_, _) => Count();
                    boxes.Add(box);
                    form.Children.Add(box);
                }
                Count();
                Add("Подтвердить выбор", () => Submit(boxes.Where(box => box.IsChecked == true)
                    .Select(box => (string)box.Content).ToArray()), true);
                break;
            }

            case SubsyPromptKind.Pages:
                form.Children.Add(BuildPager(request.Options.Length,
                    index => LiteralConsoleLine(request.Options[index], Brushes.White),
                    index => Submit(index + 1)));
                break;
        }
        Add("Отмена", () => Submit(null));
        form.Children.Add(buttons);
        return SubsyCard(root);
    }

    private UIElement BuildPager(int total, Func<int, UIElement> content,
        Action<int>? select = null, int initial = 0)
    {
        int page = Math.Clamp(initial, 0, total - 1);
        var root = new StackPanel();
        var display = new ContentControl();
        root.Children.Add(display);
        var toolbar = new WrapPanel();
        root.Children.Add(toolbar);
        var indicator = LiteralConsoleLine("");
        indicator.VerticalAlignment = VerticalAlignment.Center;
        indicator.Margin = new Thickness(3, 0, 9, 0);
        var previous = SubsyButton("← Назад", () => { });
        var next = SubsyButton("Далее →", () => { });
        toolbar.Children.Add(previous);
        toolbar.Children.Add(indicator);
        toolbar.Children.Add(next);
        if (select is not null) toolbar.Children.Add(SubsyButton("Выбрать страницу", () => select(page), true));
        previous.Click += (_, _) => { if (page > 0) { page--; Refresh(); } };
        next.Click += (_, _) => { if (page + 1 < total) { page++; Refresh(); } };
        void Refresh()
        {
            display.Content = content(page);
            indicator.Text = $"{page + 1} / {total}";
            previous.IsEnabled = page > 0;
            next.IsEnabled = page + 1 < total;
        }
        Refresh();
        return root;
    }

    private bool ShowScriptPrompt(ScriptOutput output)
    {
        if (SubsyPrompt.Parse(output.Text) is not { } request) return false;
        if (_pendingPrompts.ContainsKey(output.Run))
        {
            AddConsoleEntry(new ConsoleEntry("", "[⚠] Subsy: скрипт уже ждёт ответа на другой запрос."));
            return true;
        }
        var state = new PromptState(request, async value =>
        {
            try { await _scripts.AnswerAsync(output.Run, request.Id, value, _updateStop.Token); }
            finally { _pendingPrompts.Remove(output.Run); }
        }, "Скрипт " + output.Id);
        var entry = new ConsoleEntry("", request.Label, output) { Prompt = state };
        _pendingPrompts[output.Run] = entry;
        AddConsoleEntry(entry);
        return true;
    }

    private void FinishScriptPrompt(Guid run)
    {
        if (!_pendingPrompts.Remove(run, out var entry) || entry.Prompt is not { } prompt) return;
        prompt.Result = "Скрипт завершился";
        prompt.Refresh();
    }

    private Task<string?> PromptInlineIdAsync()
    {
        if (!_settings.MarvineSubsyEnabled) return Task.FromResult(PromptScriptId());
        var request = new SubsyPrompt("script-id", SubsyPromptKind.Text, "ID запуска", [], 0, null, false);
        return PromptInlineAsync(request);
    }

    private async Task<bool> ConfirmInlineAsync(string title, string description)
    {
        if (!_settings.MarvineSubsyEnabled) return ConfirmYettaAction(title, description);
        var request = new SubsyPrompt("confirm", SubsyPromptKind.Confirm, title + ". " + description, [], 0, null, false);
        return await PromptInlineAsync(request) == "y";
    }

    private async Task<string?> PromptInlineAsync(SubsyPrompt request)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new PromptState(request, value =>
        {
            completion.TrySetResult(value as string);
            return Task.CompletedTask;
        }, "Marvine");
        AddConsoleEntry(new ConsoleEntry("", request.Label) { Prompt = state });
        _consoleFollow?.ContentChanged();
        try { return await completion.Task.WaitAsync(_updateStop.Token); }
        finally
        {
            if (state.Result is null)
            {
                state.Result = "Запрос закрыт";
                state.Refresh();
            }
        }
    }

    private async Task<string> PreviewSubsyGalleryAsync()
    {
        if (!_settings.MarvineSubsyEnabled) return "[ℹ] Включи Subsy командой /marvine subsy enable.";
        await PreviewSubsyAsync();
        var samples = new[]
        {
            new SubsyPrompt("confirm", SubsyPromptKind.Confirm, "Продолжить демонстрацию?", [], 0, null, false),
            new SubsyPrompt("text", SubsyPromptKind.Text, "Название заметки", [], 0, null, false),
            new SubsyPrompt("number", SubsyPromptKind.Number, "Число экземпляров", [], 0, null, false),
            new SubsyPrompt("file", SubsyPromptKind.File, "Выбор файла", [], 0, null, false),
            new SubsyPrompt("password", SubsyPromptKind.Password, "Введи вымышленный пароль", [], 0, null, true),
            new SubsyPrompt("select", SubsyPromptKind.Select, "Выбор одного варианта", ["Малый", "Средний", "Большой"], 0, null, false),
            new SubsyPrompt("multi", SubsyPromptKind.Multi, "Выбери до двух вариантов", ["Сводка", "Журнал", "Отчёт"], 2, null, false),
            new SubsyPrompt("expand", SubsyPromptKind.Expandable, "Применить ко всем?", [], 0, "В демо ни одна из кнопок не меняет файлы.", false),
            new SubsyPrompt("complete", SubsyPromptKind.Autocomplete, "Найди вариант по названию", ["Обновления", "Уведомления", "Оболочка", "Хранение"], 0, null, false),
            new SubsyPrompt("pages", SubsyPromptKind.Pages, "Страницы отчёта", ["Вводная", "Проверка", "Итоги"], 0, null, false)
        };
        var demo = new ConsoleEntry("", "Галерея Subsy")
        {
            Render = () => SubsyCard(BuildPager(samples.Length, page =>
                RenderPrompt(new PromptState(samples[page], _ => Task.CompletedTask, "Демо Subsy"))))
        };
        AddConsoleEntry(demo);
        return "";
    }

    private string PageMarvineResult(string title, string[] rows)
    {
        if (!_settings.MarvineSubsyEnabled || rows.Length == 0)
            return title + (rows.Length == 0 ? "" : "\n" + string.Join("\n", rows));
        const int pageSize = 8;
        AddConsoleEntry(new ConsoleEntry("", title)
        {
            Render = () => SubsyCard(new StackPanel
            {
                Children =
                {
                    LiteralConsoleLine(title, Brushes.White),
                    BuildPager((rows.Length + pageSize - 1) / pageSize, page => LiteralConsoleLine(
                        string.Join("\n", rows.Skip(page * pageSize).Take(pageSize)), Brushes.White))
                }
            })
        });
        return "";
    }

    private async Task<string> RunMarvineUpdateAsync()
    {
        if (!_settings.MarvineSubsyEnabled)
            return "[ℹ] Yetta: " + await CheckAndInstallUpdateAsync(true);
        var run = Guid.NewGuid();
        ShowScriptOutput(new(run, "Обновления", "@subsy busy Проверка релизов"));
        int shown = -1;
        try
        {
            string result = await CheckAndInstallUpdateAsync(true, value =>
            {
                int percent = (int)Math.Round(value * 100);
                if (percent <= shown) return;
                shown = percent;
                ShowScriptOutput(new(run, "Обновления", $"@subsy progress {percent} Загрузка установщика"));
            });
            EndSubsyActivity(run, "Обновления", result);
        }
        catch
        {
            EndSubsyActivity(run, "Обновления", "Проверка или загрузка прервана", failed: true);
            throw;
        }
        return "";
    }
}
