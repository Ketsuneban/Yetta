using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    }

    private sealed record SubsyView(TextBlock Name, TextBlock Value, ProgressBar Bar);

    private static readonly Brush ConsoleSurface = new SolidColorBrush(Color.FromRgb(13, 19, 27));
    private static readonly Brush ConsoleMuted = new SolidColorBrush(Color.FromRgb(170, 189, 207));
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
        foreach (var entry in _history.TakeLast(400))
            display.Children.Add(RenderConsoleEntry(entry));
        _consoleOutput = display;
        var scroll = new ScrollViewer
        {
            Focusable = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = display
        };
        _consoleScroll = scroll;
        UpdateConsoleHeight();
        body.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = ConsoleSurface, Child = scroll });
        scroll.Loaded += (_, _) => scroll.ScrollToEnd();

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
                Text = "›", FontSize = 22, Foreground = new SolidColorBrush(Color.FromRgb(120, 222, 213)),
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
        row.Children.Add(input);
        body.Children.Add(row);

        var shortcuts = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        foreach (string command in new[] { "/help", "/dax status", "/subsy demo", "clear" })
        {
            var chip = ActionButton(command, () =>
            {
                if (_marvineCommandRunning) return;
                input.Text = command;
                RunMarvine();
                input.Focus();
            });
            chip.Background = new SolidColorBrush(Color.FromRgb(42, 53, 66));
            chip.Foreground = Brushes.White;
            chip.MinWidth = 0;
            chip.MinHeight = 32;
            chip.Padding = new Thickness(12, 5, 12, 5);
            chip.Margin = new Thickness(0, 0, 6, 4);
            shortcuts.Children.Add(chip);
        }
        body.Children.Add(shortcuts);
        var hint = new TextBlock
        {
            Text = "↑ ↓  История     Tab  Дополнение     Enter  Выполнить",
            Foreground = ConsoleMuted, FontSize = 11, Margin = new Thickness(0, 4, 0, 0)
        };
        input.TextChanged += (_, _) =>
        {
            var suggestions = ConsoleEngine.Suggestions(input.Text);
            hint.Text = input.Text.Length > 0 && suggestions.Count > 0
                ? "Tab  " + string.Join("    ", suggestions.Take(3))
                : "↑ ↓  История     Tab  Дополнение     Enter  Выполнить";
        };
        body.Children.Add(hint);
    }

    private static TextBlock LiteralConsoleLine(string value, Brush? foreground = null) => new()
    {
        Text = value, Foreground = foreground ?? ConsoleMuted, FontFamily = ConsoleFont,
        FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 7)
    };

    private UIElement RenderConsoleEntry(ConsoleEntry entry)
    {
        if (entry.Command.Length > 0)
            return LiteralConsoleLine("› " + entry.Command, Brushes.White);
        if (entry.Script is not { } script)
            return ConsoleLine(entry.Message, false);
        if (entry.Frame is null || !_settings.MarvineSubsyEnabled)
            return LiteralConsoleLine(script.Id + "  ·  " + entry.Message,
                script.IsError ? Brushes.LightSalmon : ConsoleMuted);

        var name = new TextBlock { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
        var value = LiteralConsoleLine("", ConsoleMuted);
        var bar = new ProgressBar
        {
            Height = 5, Foreground = new SolidColorBrush(Color.FromRgb(120, 222, 213)),
            Background = new SolidColorBrush(Color.FromRgb(59, 72, 87)), BorderThickness = new Thickness(0)
        };
        var view = new SubsyView(name, value, bar);
        _subsyViews[entry] = view;
        RefreshSubsyView(view, script.Id, entry.Frame);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 42, 55)),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 10),
            Margin = new Thickness(0, 1, 0, 9),
            Child = new StackPanel { Children = { name, value, bar } }
        };
    }

    private static void RefreshSubsyView(SubsyView view, string id, SubsyFrame frame)
    {
        view.Name.Text = id + (frame.Label.Length == 0 ? "" : "  ·  " + frame.Label);
        view.Value.Text = frame.Finished ? "Завершено" : frame.IsStep
            ? "Шаг " + frame.Current + " из " + frame.Total : frame.Current + "%";
        view.Bar.Maximum = frame.Total;
        view.Bar.Value = frame.Current;
    }

    private void AddConsoleEntry(ConsoleEntry entry)
    {
        _history.Add(entry);
        while (_history.Count > _settings.MarvineHistoryLimit)
        {
            var oldest = _history[0];
            _subsyViews.Remove(oldest);
            if (oldest.Script is { } script &&
                _activeSubsy.TryGetValue(script.Run, out var active) && ReferenceEquals(active, oldest))
                _activeSubsy.Remove(script.Run);
            _history.RemoveAt(0);
        }

        if (_page != "console" || _consoleMode != "Marvine" || _consoleOutput is not { } display) return;
        bool follow = _consoleScroll is { } scroll && scroll.ScrollableHeight - scroll.VerticalOffset <= 24;
        display.Children.Add(RenderConsoleEntry(entry));
        int limit = Math.Min(400, _settings.MarvineHistoryLimit);
        while (display.Children.Count > limit) display.Children.RemoveAt(0);
        if (follow || entry.Command.Length > 0) _consoleScroll?.ScrollToEnd();
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
        if (output.Exited) _activeSubsy.Remove(output.Run);
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

    private async Task<string> PreviewSubsyAsync()
    {
        if (!_settings.MarvineSubsyEnabled) return "[ℹ] Включи Subsy командой /marvine subsy enable.";
        var run = Guid.NewGuid();
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
