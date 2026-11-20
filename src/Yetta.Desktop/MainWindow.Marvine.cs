using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.ComponentModel;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Yetta.Core;

namespace Yetta.Desktop;

public partial class MainWindow
{
    private sealed class ConsoleEntry(string command, string message, ScriptOutput? script = null)
    {
        internal string Command { get; } = command;
        internal string Message { get; set; } = message;
        internal ScriptOutput? Script { get; } = script;
        internal SubsyFrame? Frame { get; set; }
        internal PromptState? Prompt { get; init; }
        internal Func<UIElement>? Render { get; init; }
        internal bool AwaitsReply => Prompt is { Result: null };
    }

    private sealed record SubsyView(TextBlock Name, TextBlock Value, ProgressBar Bar,
        System.Windows.Shapes.Path Ring, RotateTransform Rotation);

    private bool Dracula => _settings.MarvineColorset == "dracula";
    private Brush ConsoleSurface => Dracula ? Hex("#282A36") : Surface;
    private Brush ConsoleInk => Dracula ? Brushes.White : Ink;
    private Brush ConsolePanel => Dracula ? Hex("#44475A") : Resource("HoverBackground");
    private Brush ConsoleMuted => Dracula ? Hex("#CCD1E5") : Muted;
    private Brush ConsoleAccent => Dracula ? Hex("#BD93F9") : Accent;
    private static Brush Hex(string value) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    private string _marvineDraft = "";
    private ConsoleFollowController? _consoleFollow;
    private static readonly FontFamily ConsoleFont = new("Cascadia Mono, Consolas");
    private readonly List<ConsoleEntry> _history = [];
    private readonly Dictionary<Guid, ConsoleEntry> _activeSubsy = [];
    private readonly Dictionary<ConsoleEntry, SubsyView> _subsyViews = [];
    private readonly ConcurrentQueue<ScriptOutput> _scriptOutput = new();
    private int _pendingScriptOutput, _droppedScriptOutput, _scriptDrainScheduled;

    private ConsoleTranscript AttachTranscript(DockPanel layout, int limit = 500)
    {
        var empty = WelcomeBanner();
        var display = new ConsoleTranscript(limit, empty) { Margin = new Thickness(5, 6, 8, 5) };
        var scroll = new EdgeScrollViewer
        {
            Content = display, Focusable = false, Background = ConsoleSurface, EdgePreviewHeight = 14,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        _consoleOutput = display;
        _consoleFollow = new ConsoleFollowController(scroll);
        var viewport = new Grid();
        viewport.Children.Add(scroll);
        var follow = _consoleFollow;
        var bottom = SubsyButton("К последней строке ↓", follow.Resume);
        bottom.Visibility = Visibility.Collapsed;
        bottom.HorizontalAlignment = HorizontalAlignment.Right;
        bottom.VerticalAlignment = VerticalAlignment.Bottom;
        bottom.Margin = new Thickness(0, 0, 18, 6);
        follow.FollowingChanged += following => bottom.Visibility = following ? Visibility.Collapsed : Visibility.Visible;
        viewport.Children.Add(bottom);
        layout.Children.Add(viewport);
        return display;
    }

    private DockPanel BuildCommandRow(TextBox input, string prefix, Action execute)
    {
        var row = new DockPanel();
        var send = SubsyButton("Выполнить", () => { input.Focus(); execute(); }, true);
        send.Background = ConsoleAccent;
        send.Foreground = Dracula ? Hex("#18222C") : Resource("AccentForeground");
        send.MinHeight = 40;
        send.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(send, Dock.Right);
        row.Children.Add(send);
        var marker = new TextBlock
        {
            Text = prefix, FontFamily = ConsoleFont, FontWeight = FontWeights.Bold, Foreground = ConsoleAccent,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0)
        };
        var field = new DockPanel();
        field.Children.Add(marker);
        var layers = new Grid();
        layers.Children.Add(input);
        var hint = new TextBlock
        {
            Text = "Введите команду", IsHitTestVisible = false, Foreground = ConsoleMuted,
            Margin = new Thickness(6, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        layers.Children.Add(hint);
        field.Children.Add(layers);
        var frame = new Border
        {
            Child = field, CornerRadius = new CornerRadius(7), Background = ConsolePanel, MinHeight = 40,
            BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent
        };
        row.Children.Add(frame);
        input.IsKeyboardFocusedChanged += (_, _) => frame.BorderBrush = input.IsKeyboardFocused ? ConsoleAccent : Brushes.Transparent;
        frame.MouseLeftButtonDown += (_, _) => input.Focus();
        void Refresh()
        {
            send.IsEnabled = !input.IsReadOnly && !string.IsNullOrWhiteSpace(input.Text);
            hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        }
        input.TextChanged += (_, _) => Refresh();
        var readOnly = DependencyPropertyDescriptor.FromProperty(TextBox.IsReadOnlyProperty, typeof(TextBox));
        EventHandler changed = (_, _) => Refresh();
        input.Loaded += (_, _) => { readOnly.AddValueChanged(input, changed); Refresh(); };
        input.Unloaded += (_, _) => readOnly.RemoveValueChanged(input, changed);
        Refresh();
        return row;
    }

    private void BuildMarvineWorkspace(DockPanel layout, DockPanel tools)
    {
        var footer = new StackPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);
        var input = BuildTerminalInput("Команда Marvine", RunMarvine);
        input.IsReadOnly = _marvineCommandRunning;
        input.Text = _marvineDraft;
        input.CaretIndex = input.Text.Length;
        _consoleInput = input;
        var history = SubsyButton("История", OpenMarvineHistory);
        history.ToolTip = "Найти введённую команду (Ctrl+R)";
        DockPanel.SetDock(history, Dock.Right);
        tools.Children.Add(history);
        footer.Children.Add(BuildCommandRow(input, "/", RunMarvine));
        var shortcuts = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        bool showAll = false;
        void RefreshSuggestions()
        {
            shortcuts.Children.Clear();
            var suggestions = CommandCompletion.Get(input.Text);
            foreach (var group in suggestions.GroupBy(s => s.Label.Split(':')[0]).Take(showAll ? int.MaxValue : 5))
            {
                var suggestion = group.First();
                if (suggestion.Label.EndsWith(":true") || suggestion.Label.EndsWith(":false"))
                {
                    var pair = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    var flag = LiteralConsoleLine(group.Key, ConsoleInk);
                    flag.VerticalAlignment = VerticalAlignment.Center;
                    flag.Margin = new Thickness(8, 0, 6, 0);
                    pair.Children.Add(flag);
                    foreach (var state in group)
                    {
                        string value = state.Label.Split(':')[1];
                        var choice = new RadioButton { Content = value, Style = (Style)FindResource("FlagChoice"),
                            Foreground = ConsoleInk, ToolTip = state.Label };
                        choice.Click += (_, _) =>
                        {
                            if (input.IsReadOnly) return;
                            input.Text = state.Command.TrimStart('/');
                            input.CaretIndex = input.Text.Length;
                            input.Focus();
                        };
                        pair.Children.Add(choice);
                    }
                    shortcuts.Children.Add(new Border { Background = ConsolePanel, CornerRadius = new CornerRadius(14),
                        Margin = new Thickness(0, 4, 6, 4), Padding = new Thickness(2), Child = pair });
                    continue;
                }
                var chip = SubsyButton(suggestion.Label, () =>
                {
                    if (_marvineCommandRunning) return;
                    input.Text = suggestion.Command.TrimStart('/');
                    input.CaretIndex = input.Text.Length;
                    input.Focus();
                });
                shortcuts.Children.Add(chip);
            }
            if (suggestions.Select(x => x.Label.Split(':')[0]).Distinct().Count() > 5)
                shortcuts.Children.Add(SubsyButton(showAll ? "Свернуть варианты" : "Все варианты (" + suggestions.Count + ")", () =>
                { showAll = !showAll; RefreshSuggestions(); }));
            var args = CommandTokens.Read(CommandAliases.Normalize(input.Text), out _);
            var words = CommandTokens.Read(input.Text, out bool closed);
            bool completed = closed && (input.Text.EndsWith(' ') || words.Count > 1 &&
                words[1].Value.Equals(args[1].Value, StringComparison.OrdinalIgnoreCase));
            if (completed && args.Count == 2 && args[0].Value == "/script" && args[1].Value is "run" or "stop")
                shortcuts.Children.Add(SubsyButton("Выбрать .py…", () =>
                {
                    if (input.IsReadOnly) return;
                    var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Python (*.py)|*.py", CheckFileExists = true };
                    if (picker.ShowDialog(this) == true)
                    {
                        input.Text = input.Text.TrimEnd() + " \"" + picker.FileName + "\"";
                        input.CaretIndex = input.Text.Length;
                    }
                    input.Focus();
                }));
            if (args.Count == 2 && args[0].Value == "/snob" &&
                args[1].Value.Equals("uninstall", StringComparison.OrdinalIgnoreCase))
                shortcuts.Children.Add(SubsyButton("Выбрать программу…", async () => await ShowUninstallCatalogAsync()));
            if (args.Count == 3 && args[0].Value == "/test" && args[1].Value == "warn" && !args[2].Value.Equals("Fatal", StringComparison.OrdinalIgnoreCase) && WarningTestCommand.Kinds.Contains(args[2].Value, StringComparer.OrdinalIgnoreCase))
            {
                shortcuts.Children.Add(SubsyButton("Выбрать файл…", () =>
                {
                    if (input.IsReadOnly) return;
                    var picker = new Microsoft.Win32.OpenFileDialog();
                    if (picker.ShowDialog(this) == true) { input.Text = input.Text.TrimEnd() + " \"" + picker.FileName + "\" "; input.CaretIndex = input.Text.Length; }
                }));
                shortcuts.Children.Add(SubsyButton("Выбрать папку…", () =>
                {
                    if (input.IsReadOnly) return;
                    var picker = new Microsoft.Win32.OpenFolderDialog();
                    if (picker.ShowDialog(this) == true) { input.Text = input.Text.TrimEnd() + " \"" + picker.FolderName + "\" "; input.CaretIndex = input.Text.Length; }
                }));
            }
        }
        input.TextChanged += (_, _) =>
        {
            if (input.Text.StartsWith('/'))
            {
                int caret = input.CaretIndex;
                input.Text = input.Text.TrimStart('/');
                input.CaretIndex = Math.Clamp(caret - 1, 0, input.Text.Length);
                return;
            }
            _marvineDraft = input.Text;
            showAll = false;
            RefreshSuggestions();
        };
        RefreshSuggestions();
        var options = new ScrollViewer
        {
            Content = shortcuts, MaxHeight = 118, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false
        };
        footer.Children.Add(options);
        var display = AttachTranscript(layout, Math.Min(400, _settings.MarvineHistoryLimit));
        var copy = SubsyButton("Копировать вывод", () =>
        {
            if (_history.Count == 0) return;
            try { Clipboard.SetText(string.Join(Environment.NewLine, _history.Select(entry =>
                entry.Command.Length > 0 ? "❭ " + entry.Command.TrimStart('/') : entry.Message))); }
            catch (System.Runtime.InteropServices.ExternalException)
            { AddConsoleEntry(new ConsoleEntry("", "[⚠] Буфер обмена занят. Повтори копирование.")); }
        });
        DockPanel.SetDock(copy, Dock.Right);
        tools.Children.Add(copy);
        var recent = _history.TakeLast(Math.Min(400, _settings.MarvineHistoryLimit)).ToHashSet();
        foreach (var entry in _history.Where(entry => entry.AwaitsReply || recent.Contains(entry)))
            AppendMarvineEntry(display, entry);
        input.ToolTip = "↑ и ↓ — история · Tab — дополнить · Ctrl+R — поиск по истории";
        input.PreviewKeyDown += (_, e) =>
        {
            if (input.IsReadOnly || Keyboard.Modifiers != ModifierKeys.None) return;
            bool up = e.Key == Key.Up || e.Key == Key.NumPad8 && !Keyboard.IsKeyToggled(Key.NumLock);
            bool down = e.Key == Key.Down || e.Key == Key.NumPad2 && !Keyboard.IsKeyToggled(Key.NumLock);
            if ((up || down) && _enteredCommands.Count > 0)
            {
                if (_commandHistoryIndex == _enteredCommands.Count) _commandDraft = input.Text;
                _commandHistoryIndex = Math.Clamp(_commandHistoryIndex + (up ? -1 : 1), 0, _enteredCommands.Count);
                input.Text = _commandHistoryIndex == _enteredCommands.Count ? _commandDraft : _enteredCommands[_commandHistoryIndex];
            }
            else if (e.Key == Key.Tab && input.CaretIndex == input.Text.Length && input.SelectionLength == 0 &&
                     input.Text.Length > 0 && CommandCompletion.Get(input.Text).FirstOrDefault() is { } completion)
                input.Text = completion.Command.TrimStart('/');
            else return;
            input.CaretIndex = input.Text.Length;
            e.Handled = true;
        };
        input.Focus();
    }

    private UIElement WelcomeBanner()
    {
        var layout = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Star) });

        var identity = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(4, 2, 12, 0) };
        var greeting = LiteralConsoleLine("Добро пожаловать");
        greeting.TextAlignment = TextAlignment.Center;
        greeting.Foreground = ConsoleInk;
        var mark = LiteralConsoleLine("   ╭────────╮\n  ╱  ◇    ◇  ╲\n  ╲__________╱\n    ╰╯╰╯╰╯", ConsoleAccent);
        mark.TextAlignment = TextAlignment.Center;
        var profile = LiteralConsoleLine(AccessProfiles.DisplayName(_settings.ActiveProfile) + " · " + _settings.MarvineColorset);
        profile.TextAlignment = TextAlignment.Center;
        identity.Children.Add(greeting);
        identity.Children.Add(mark);
        identity.Children.Add(profile);
        columns.Children.Add(identity);

        var tips = new StackPanel { Margin = new Thickness(14, 0, 4, 0) };
        tips.Children.Add(LiteralConsoleLine("Начало работы", ConsoleAccent));
        tips.Children.Add(LiteralConsoleLine("/help — справка · /script — Python", ConsoleInk));
        tips.Children.Add(LiteralConsoleLine("Последняя команда", ConsoleAccent));
        tips.Children.Add(LiteralConsoleLine(_enteredCommands.LastOrDefault() ?? "Команд пока нет"));
        var side = new Border { Child = tips, BorderBrush = ConsolePanel, BorderThickness = new Thickness(1, 0, 0, 0) };
        Grid.SetColumn(side, 1);
        columns.Children.Add(side);

        var outline = new Border { BorderBrush = ConsoleAccent, BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 10, 0, 0), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 15, 12, 8), Child = columns };
        layout.Children.Add(outline);
        var title = LiteralConsoleLine($"Marvine · Yetta {ReleaseVersion}", ConsoleAccent);
        title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(0);
        layout.Children.Add(new Border { Child = title, Background = ConsoleSurface,
            Margin = new Thickness(14, 0, 0, 0), Padding = new Thickness(5, 0, 5, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
        return layout;
    }

    private TextBlock LiteralConsoleLine(string value, Brush? foreground = null) => new()
    {
        Text = value, Foreground = foreground ?? ConsoleMuted, FontFamily = ConsoleFont,
        FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 7)
    };

    private UIElement RenderConsoleEntry(ConsoleEntry entry)
    {
        if (entry.Prompt is { } prompt)
            return RenderPrompt(prompt);
        if (entry.Render is { } render)
            return render();
        if (entry.Command.Length > 0)
            return LiteralConsoleLine("❭ " + entry.Command.TrimStart('/'), ConsoleAccent);
        if (entry.Script is not { } script)
            return ConsoleLine(entry.Message, false);
        if (entry.Frame is null || !_settings.MarvineSubsyEnabled)
            return LiteralConsoleLine(script.Id + "  ·  " + entry.Message,
                script.IsError ? (_settings.MarvineColorset == "dracula" ? Hex("#FF5555") : Brushes.LightSalmon) : ConsoleMuted);

        var name = new TextBlock { Foreground = ConsoleInk, TextWrapping = TextWrapping.Wrap };
        var value = LiteralConsoleLine("", ConsoleMuted);
        var bar = new ProgressBar
        {
            Height = 5, Foreground = new SolidColorBrush(Color.FromRgb(120, 222, 213)),
            Background = new SolidColorBrush(Color.FromRgb(59, 72, 87)), BorderThickness = new Thickness(0)
        };
        var rotation = new RotateTransform();
        var ring = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 14,7 A 7,7 0 1 1 7,0"),
            Width = 16, Height = 16, Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed,
            Stroke = bar.Foreground, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round, RenderTransformOrigin = new Point(.5, .5),
            RenderTransform = rotation, Margin = new Thickness(0, 0, 9, 6)
        };
        var details = new StackPanel { Orientation = Orientation.Horizontal, Children = { ring, value } };
        var view = new SubsyView(name, value, bar, ring, rotation);
        _subsyViews[entry] = view;
        RefreshSubsyView(view, script.Id, entry.Frame);
        return SubsyCard(new StackPanel { Children = { name, details, bar } });
    }

    private void AppendMarvineEntry(ConsoleTranscript display, ConsoleEntry entry) =>
        display.Append(RenderConsoleEntry(entry), entry.Prompt is null ? null : () => entry.AwaitsReply);

    private static void RefreshSubsyView(SubsyView view, string id, SubsyFrame frame)
    {
        view.Name.Text = id + (frame.Label.Length == 0 ? "" : "  ·  " + frame.Label);
        view.Value.Text = frame.Failed ? "Прервано" : frame.Finished ? "Завершено" : frame.IsBusy ? "Выполняется…" : frame.IsStep
            ? "Шаг " + frame.Current + " из " + frame.Total : frame.Current + "%";
        Visibility ringVisibility = frame.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        if (view.Ring.Visibility != ringVisibility)
        {
            view.Ring.Visibility = ringVisibility;
            view.Rotation.BeginAnimation(RotateTransform.AngleProperty, frame.IsBusy && UiMotion.AnimationsEnabled
                ? new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(800)) { RepeatBehavior = RepeatBehavior.Forever }
                : null);
        }
        view.Bar.Visibility = frame.IsBusy ? Visibility.Collapsed : Visibility.Visible;
        view.Bar.Maximum = frame.Total;
        view.Bar.Value = frame.Current;
    }

    private void AddConsoleEntry(ConsoleEntry entry)
    {
        _history.Add(entry);
        while (_history.Count > _settings.MarvineHistoryLimit)
        {
            int position = _history.FindIndex(item => !item.AwaitsReply);
            if (position < 0) break;
            var oldest = _history[position];
            _subsyViews.Remove(oldest);
            if (oldest.Script is { } script &&
                _activeSubsy.TryGetValue(script.Run, out var active) && ReferenceEquals(active, oldest))
                _activeSubsy.Remove(script.Run);
            _history.RemoveAt(position);
        }

        if (_page != "console" || _consoleOutput is not { } display) return;
        AppendMarvineEntry(display, entry);
        _consoleFollow?.ContentChanged();
    }

    private void EnqueueScriptOutput(ScriptOutput output)
    {
        if (Dispatcher.HasShutdownStarted) return;
        if (Interlocked.Increment(ref _pendingScriptOutput) <= 400 || output.Exited)
            _scriptOutput.Enqueue(output with { Text = output.Text.Length > 4096 ? output.Text[..4096] : output.Text });
        else
        {
            Interlocked.Decrement(ref _pendingScriptOutput);
            Interlocked.Increment(ref _droppedScriptOutput);
        }
        ScheduleScriptDrain();
    }

    private void ScheduleScriptDrain()
    {
        if (Interlocked.Exchange(ref _scriptDrainScheduled, 1) == 0)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(DrainScriptOutput));
    }

    private void DrainScriptOutput()
    {
        for (int i = 0; i < 60 && _scriptOutput.TryDequeue(out var output); i++)
        {
            Interlocked.Decrement(ref _pendingScriptOutput);
            ShowScriptOutput(output);
        }
        int skipped = Interlocked.Exchange(ref _droppedScriptOutput, 0);
        if (skipped > 0) AddConsoleEntry(new ConsoleEntry("", "[⚠] Пропущено строк быстрого вывода: " + skipped));
        Interlocked.Exchange(ref _scriptDrainScheduled, 0);
        if (!_scriptOutput.IsEmpty) ScheduleScriptDrain();
    }

    private void ShowScriptOutput(ScriptOutput output)
    {
        if (output.Exited)
        {
            FinishScriptPrompt(output.Run);
            EndSubsyActivity(output.Run, output.Id, output.Text, output.IsError);
        }
        if (_settings.MarvineSubsyEnabled && !output.IsError && !output.Exited && ShowScriptPrompt(output)) return;
        if (_settings.MarvineSubsyEnabled && !output.IsError && !output.Exited &&
            SubsyFrame.Parse(output.Text) is { } frame)
        {
            if (_activeSubsy.TryGetValue(output.Run, out var existing))
            {
                existing.Frame = frame;
                existing.Message = output.Text;
                if (_subsyViews.TryGetValue(existing, out var view)) RefreshSubsyView(view, output.Id, frame);
            }
            else
            {
                var entry = new ConsoleEntry("", output.Text, output) { Frame = frame };
                if (!frame.Finished) _activeSubsy[output.Run] = entry;
                AddConsoleEntry(entry);
            }
            if (frame.Finished) _activeSubsy.Remove(output.Run);
            return;
        }
        AddConsoleEntry(new ConsoleEntry("", output.Text, output));
    }

    private void EndSubsyActivity(Guid run, string id, string label, bool failed = false)
    {
        if (!_activeSubsy.Remove(run, out var active) || active.Frame is not { } previous) return;
        active.Frame = previous with { IsBusy = false, Finished = true, Failed = failed, Label = label };
        if (_subsyViews.TryGetValue(active, out var view)) RefreshSubsyView(view, id, active.Frame);
    }

    private async Task<string> PreviewSubsyAsync()
    {
        if (!_settings.MarvineSubsyEnabled) return "[ℹ] Включи Subsy командой /marvine subsy enable.";
        var run = Guid.NewGuid();
        ShowScriptOutput(new(run, "Демо Subsy", "@subsy busy Запуск"));
        await Task.Delay(300, _updateStop.Token);
        ShowScriptOutput(new(run, "Демо Subsy", "[####------] 40% Подготовка"));
        await Task.Delay(280, _updateStop.Token);
        for (int stage = 1; stage <= 3; stage++)
        {
            ShowScriptOutput(new(run, "Демо Subsy", "@subsy step " + stage + "/3 " +
                (stage == 1 ? "Подготовка" : stage == 2 ? "Обработка" : "Проверка результата")));
            await Task.Delay(280, _updateStop.Token);
        }
        ShowScriptOutput(new(run, "Демо Subsy", "@subsy done Все шаги пройдены"));
        return "";
    }
}
