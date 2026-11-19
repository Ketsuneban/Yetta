using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

    private Brush ConsoleSurface => _settings.MarvineColorset == "dracula" ? Hex("#282A36") : new SolidColorBrush(Color.FromRgb(23, 23, 23));
    private Brush ConsoleMuted => _settings.MarvineColorset == "dracula" ? Hex("#F8F8F2") : new SolidColorBrush(Color.FromRgb(170, 189, 207));
    private static Brush Hex(string value) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    private ConsoleFollowController? _consoleFollow;
    private static readonly FontFamily ConsoleFont = new("Cascadia Mono, Consolas");
    private readonly List<ConsoleEntry> _history = [];
    private readonly Dictionary<Guid, ConsoleEntry> _activeSubsy = [];
    private readonly Dictionary<ConsoleEntry, SubsyView> _subsyViews = [];
    private readonly ConcurrentQueue<ScriptOutput> _scriptOutput = new();
    private int _pendingScriptOutput, _droppedScriptOutput, _scriptDrainScheduled;

    private void BuildMarvineWorkspace(StackPanel body)
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var status = new TextBlock
        {
            Text = _settings.ActiveProfile.ToUpperInvariant() + "  ·  " + ReleaseVersion,
            Foreground = ConsoleMuted, FontFamily = ConsoleFont, FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(status, Dock.Right);
        header.Children.Add(status);
        header.Children.Add(new TextBlock
        {
            Text = "Рабочая область", Foreground = Brushes.White,
            FontSize = 18, FontWeight = FontWeights.SemiBold
        });
        body.Children.Add(header);

        var display = new StackPanel { Margin = new Thickness(14, 11, 12, 4) };
        if (_history.Count == 0)
            display.Children.Add(LiteralConsoleLine("Marvine готова. Введи /help или выбери команду ниже."));
        var pinned = _history.Where(entry => entry.AwaitsReply).ToHashSet();
        var last = _history.TakeLast(400).ToHashSet();
        foreach (var entry in _history.Where(entry => pinned.Contains(entry) || last.Contains(entry)))
            display.Children.Add(RenderTaggedEntry(entry));
        var menu = new ContextMenu();
        var copyAll = new MenuItem { Header = "Копировать весь вывод" };
        copyAll.Click += (_, _) =>
        {
            try { Clipboard.SetText(string.Join(Environment.NewLine, _history.Select(entry => entry.Command.Length > 0 ? "› " + entry.Command : entry.Message))); }
            catch (System.Runtime.InteropServices.ExternalException) { }
        };
        menu.Items.Add(copyAll);
        display.ContextMenu = menu;
        _consoleOutput = display;
        var scroll = new EdgeScrollViewer
        {
            Background = ConsoleSurface, Focusable = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = display
        };
        _consoleScroll = scroll;
        _consoleFollow?.Dispose();
        _consoleFollow = new ConsoleFollowController(scroll);
        UpdateConsoleHeight();
        body.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = ConsoleSurface, Child = scroll });
        scroll.Loaded += (_, _) => _consoleFollow?.ContentChanged();

        var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var send = ActionButton("Выполнить", RunMarvine, true);
        send.MinWidth = 108;
        send.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(send, Dock.Right);
        row.Children.Add(send);
        var prompt = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(31, 40, 52)), CornerRadius = new CornerRadius(6),
            Child = new TextBlock
            {
                Text = "/", FontSize = 22, Foreground = new SolidColorBrush(Color.FromRgb(120, 222, 213)),
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
            },
            Width = 26, Margin = new Thickness(0, 0, 3, 0)
        };
        DockPanel.SetDock(prompt, Dock.Left);
        row.Children.Add(prompt);
        var input = BuildTerminalInput("/help — команды Marvine", RunMarvine);
        input.IsReadOnly = _marvineCommandRunning;
        System.Windows.Automation.AutomationProperties.SetName(input, "Команда Marvine");
        _consoleInput = input;
        if (_settings.MarvineColorset == "dracula")
        {
            input.Background = Hex("#44475A"); input.Foreground = Hex("#F8F8F2");
            prompt.Background = Hex("#44475A");
            if (prompt.Child is TextBlock marker) marker.Foreground = Hex("#BD93F9");
        }
        row.Children.Add(input);
        body.Children.Add(row);

        var shortcuts = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        bool showAll = false;
        void RefreshSuggestions()
        {
            shortcuts.Children.Clear();
            var suggestions = CommandCompletion.Get(input.Text);
            foreach (var suggestion in suggestions.Take(showAll ? int.MaxValue : 5))
            {
                var chip = SubsyButton(suggestion.Label, () =>
                {
                    if (_marvineCommandRunning) return;
                    input.Text = suggestion.Command;
                    input.CaretIndex = input.Text.Length;
                    input.Focus();
                });
                shortcuts.Children.Add(chip);
            }
            if (suggestions.Count > 5)
                shortcuts.Children.Add(SubsyButton(showAll ? "Свернуть варианты" : "Все варианты (" + suggestions.Count + ")", () =>
                { showAll = !showAll; RefreshSuggestions(); }));
            var args = CommandTokens.Read(CommandAliases.Normalize(input.Text), out _);
            if (args.Count == 3 && args[0].Value == "/test" && args[1].Value == "warn" && WarningTestCommand.Kinds.Contains(args[2].Value, StringComparer.OrdinalIgnoreCase))
            {
                shortcuts.Children.Add(SubsyButton("Выбрать файл…", () =>
                {
                    var picker = new Microsoft.Win32.OpenFileDialog();
                    if (picker.ShowDialog(this) == true) { input.Text = input.Text.TrimEnd() + " \"" + picker.FileName + "\" "; input.CaretIndex = input.Text.Length; }
                }));
                shortcuts.Children.Add(SubsyButton("Выбрать папку…", () =>
                {
                    var picker = new Microsoft.Win32.OpenFolderDialog();
                    if (picker.ShowDialog(this) == true) { input.Text = input.Text.TrimEnd() + " \"" + picker.FolderName + "\" "; input.CaretIndex = input.Text.Length; }
                }));
            }
        }
        input.TextChanged += (_, _) =>
        {
            if (prompt.Child is TextBlock marker) marker.Text = input.Text.TrimStart().StartsWith('/') ? "" : "/";
            showAll = false; RefreshSuggestions();
        };
        RefreshSuggestions();
        body.Children.Add(shortcuts);
        body.Children.Add(new TextBlock
        {
            Text = "↑ ↓ / Num 8 2 — история    Tab — дополнить    Enter — выполнить    Выделение + Ctrl+C — копировать",
            Foreground = ConsoleMuted, FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap
        });
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
            return SelectableConsole.Wrap(LiteralConsoleLine("/ " + entry.Command.TrimStart('/'), _settings.MarvineColorset == "dracula" ? Hex("#8BE9FD") : Brushes.White));
        if (entry.Script is not { } script)
            return SelectableConsole.Wrap(ConsoleLine(entry.Message, false));
        if (entry.Frame is null || !_settings.MarvineSubsyEnabled)
            return SelectableConsole.Wrap(LiteralConsoleLine(script.Id + "  ·  " + entry.Message,
                script.IsError ? (_settings.MarvineColorset == "dracula" ? Hex("#FF5555") : Brushes.LightSalmon) : ConsoleMuted));

        var name = new TextBlock { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
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

    private UIElement RenderTaggedEntry(ConsoleEntry entry)
    {
        var element = RenderConsoleEntry(entry);
        if (element is FrameworkElement taggable) taggable.Tag = entry;
        return element;
    }

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

        if (_page != "console" || _consoleMode != "Marvine" || _consoleOutput is not { } display) return;
        display.Children.Add(RenderTaggedEntry(entry));
        int limit = Math.Min(400, _settings.MarvineHistoryLimit);
        while (display.Children.Count > limit)
        {
            var first = display.Children.OfType<FrameworkElement>().FirstOrDefault(item =>
                item.Tag is not ConsoleEntry { AwaitsReply: true } && !ReferenceEquals(item.Tag, entry));
            if (first is null) break;
            display.Children.Remove(first);
        }
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
