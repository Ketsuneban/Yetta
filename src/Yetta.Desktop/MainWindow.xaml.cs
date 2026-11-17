using System.Net.Http;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Microsoft.Toolkit.Uwp.Notifications;
using Yetta.Core;

namespace Yetta.Desktop;

public partial class MainWindow : Window
{
    private static readonly Version CurrentVersion = typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 3, 26);
    private static readonly string ReleaseVersion = CurrentVersion.ToString(3) + "-preview";
    private readonly CancellationTokenSource _updateStop = new();
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private CancellationTokenSource? _downloadCancel;
    private Version? _dismissedAutoVersion;
    private Version? _downloadingVersion;
    private DateTimeOffset _nextAutoUpdateErrorNotice;
    private readonly SettingsStore _store = new();
    private readonly TallowNotificationStore _activityStore = new();
    private readonly AppSettings _settings;
    private readonly Dictionary<string, Button> _navigation;
    private readonly List<(string Command, string Reply)> _history = [];
    private readonly List<string> _enteredCommands = [];
    private readonly MarvineScriptManager _scripts = new();
    private int _commandHistoryIndex;
    private readonly List<(DateTime Time, string Source, string Message)> _notifications = [];
    private string _page = "overview";
    private string _consoleMode = "Marvine";
    private StackPanel? _consoleOutput;
    private TextBox? _consoleInput;
    private ScrollViewer? _consoleScroll;
    private EmbeddedShellSession? _embeddedShell;
    private DaxProcessMonitor? _processMonitor;
    private readonly DaxAgentGate _daxGate;

    private Brush Ink => Resource("TextPrimary");
    private Brush Muted => Resource("TextSecondary");
    private Brush Accent => Resource("AccentBrush");
    private Brush Surface => Resource("CardBackground");

    public MainWindow()
    {
        InitializeComponent();
        SidebarVersion.Text = "YETTA " + ReleaseVersion + " · WINDOWS 11";
        FooterVersion.Text = ReleaseVersion;
        _settings = _store.Load();
        LoadMarvineCommandHistory();
        try
        {
            foreach (var record in _activityStore.Load(_settings.NotificationRetentionHours))
                _notifications.Add((record.Time.LocalDateTime, record.Source, record.Message));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DaxAudit.Write("activity", "load_failed", ex.GetType().Name);
        }
        _navigation = new()
        {
            ["overview"] = NavOverview,
            ["info"] = NavInfo,
            ["console"] = NavConsole,
            ["guard"] = NavGuard,
            ["logs"] = NavLogs,
            ["activity"] = NavActivity,
            ["recovery"] = NavRecovery,
            ["settings"] = NavSettings,
            ["sources"] = NavSources
        };
        ApplyTheme();
        InitializeIconsAndCursors();
        foreach (var navButton in _navigation.Values)
            UiMotion.AttachButtonFeedback(navButton);
        UiMotion.AttachButtonFeedback(CaptionMinimize);
        UiMotion.AttachButtonFeedback(CaptionMaximize);
        UiMotion.AttachButtonFeedback(CaptionClose);
        SourceInitialized += (_, _) => ApplyBackdrop();
        UpdateCaptionState();
        StateChanged += (_, _) => UpdateCaptionState();
        MainScroll.SizeChanged += (_, _) => UpdateConsoleHeight();
        Navigate("overview");
        _daxGate = new DaxAgentGate(this);
        SystemEvents.UserPreferenceChanged += OnWindowsPreferenceChanged;
        ToastNotificationManagerCompat.OnActivated += args =>
        {
            if (args.Argument.Contains("cancel-update", StringComparison.Ordinal))
                Dispatcher.BeginInvoke(new Action(() => { _dismissedAutoVersion = _downloadingVersion; _downloadCancel?.Cancel(); }));
        };
        Loaded += (_, _) => _ = WatchUpdatesAsync();
        Closed += (_, _) => { SystemEvents.UserPreferenceChanged -= OnWindowsPreferenceChanged; _updateStop.Cancel(); _downloadCancel?.Cancel(); _daxGate.Dispose(); _processMonitor?.Dispose(); _processMonitor = null; _embeddedShell?.Dispose(); _embeddedShell = null; _scripts.Dispose(); };
    }

    private void InitializeIconsAndCursors()
    {
        IconAssets.SetNav(NavOverview, 7, TallowText.Get("ui.main.0001"));
        IconAssets.SetNav(NavInfo, 2, TallowText.Get("ui.main.0002"));
        IconAssets.SetNav(NavConsole, 1, TallowText.Get("ui.main.0003"));
        IconAssets.SetNavLogo(NavGuard, "dax-logo.png", TallowText.Get("ui.main.0004"));
        IconAssets.SetNav(NavLogs, 8, TallowText.Get("ui.main.0005"));
        IconAssets.SetNav(NavActivity, 31, TallowText.Get("ui.main.0006"));
        IconAssets.SetNav(NavRecovery, 9, TallowText.Get("ui.main.0007"));
        IconAssets.SetNav(NavSettings, 3, TallowText.Get("ui.main.0008"));
        IconAssets.SetNavLogo(NavSources, "external-link.png", TallowText.Get("ui.main.0009"));
        Cursor = Cursors.Arrow;
    }

    private void ShowYettaNotice(string title, string message, WarningKind kind = WarningKind.Dax) =>
        YettaWarningWindow.Show(this,
            new WarningRequest(kind, title, message, Subject: "Yetta"),
            _settings.ActiveProfile, nonModal: true);

    private bool ConfirmYettaAction(string title, string message) =>
        YettaWarningWindow.Show(this,
            new WarningRequest(WarningKind.Standard, title, message, Subject: "Yetta"),
            _settings.ActiveProfile);

    private void OnWindowMinimize(object sender, RoutedEventArgs e) => WindowState = System.Windows.WindowState.Minimized;

    private void OnWindowMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == System.Windows.WindowState.Maximized ? System.Windows.WindowState.Normal : System.Windows.WindowState.Maximized;

    private void OnWindowClose(object sender, RoutedEventArgs e) => Close();

    private void UpdateCaptionState()
    {
        if (CaptionMaximize is null) return;
        bool maximized = WindowState == System.Windows.WindowState.Maximized;
        CaptionMaximize.Content = maximized ? "\uE923" : "\uE922";
        CaptionMaximize.ToolTip = maximized ? TallowText.Get("ui.main.0010") : TallowText.Get("ui.main.0011");
    }

    private static Brush Resource(string key) => (Brush)Application.Current.Resources[key];
    private void OnWindowsPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
        Dispatcher.BeginInvoke(new Action(ApplyTheme));
    private static void SetColor(string key, Color color) =>
        Application.Current.Resources[key] = new SolidColorBrush(color);
    private static void SetBrush(string key, Brush brush) =>
        Application.Current.Resources[key] = brush;

    private void ApplyTheme()
    {
        const bool glass = false;
        var ui = new Windows.UI.ViewManagement.UISettings();
        static Color Native(Windows.UI.Color c) => Color.FromArgb(c.A, c.R, c.G, c.B);
        static Color Mix(Color a, Color b, double amount) => Color.FromRgb(
            (byte)(a.R * (1 - amount) + b.R * amount),
            (byte)(a.G * (1 - amount) + b.G * amount),
            (byte)(a.B * (1 - amount) + b.B * amount));
        Color background = SystemParameters.HighContrast ? SystemColors.WindowColor
            : Native(ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background));
        Color foreground = SystemParameters.HighContrast ? SystemColors.WindowTextColor
            : Native(ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground));
        Color accent = SystemParameters.HighContrast ? SystemColors.HighlightColor
            : Native(ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.Accent));
        bool dark = background.R + background.G + background.B < 384;
        Color surface = Mix(background, foreground, dark ? 0.14 : 0.035);
        SetColor("WindowBackground", Mix(background, foreground, dark ? 0.055 : 0.012));
        SetColor("SidebarBackground", Mix(background, foreground, dark ? 0.09 : 0.025));
        SetColor("CardBackground", surface);
        SetColor("HoverBackground", Mix(background, foreground, dark ? 0.18 : 0.10));
        SetColor("AccentBrush", accent);
        SetColor("AccentForeground", accent.R * 0.299 + accent.G * 0.587 + accent.B * 0.114 > 145
            ? Colors.Black : Colors.White);
        SetColor("TextPrimary", foreground);
        SetColor("TextSecondary", Mix(foreground, background, 0.35));
        SetColor("OutlineBrush", Mix(background, foreground, 0.20));
        SetColor("SelectedBrush", Mix(background, accent, dark ? 0.27 : 0.15));
        GlassDialog.IsGlass = glass;
        ApplyBackdrop();
        ThemeIndicator.Text = "Windows 11";
    }

    private void ApplyBackdrop() => WindowsBackdrop.Configure(this, false);

    private void SavePreferences()
    {
        try { _store.Save(_settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowYettaNotice(TallowText.Get("ui.main.0014"), TallowText.Get("ui.main.0015") + ex.Message, WarningKind.Caution);
        }
    }

    private static string MarvineHistoryFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Yetta", "marvine-history.txt");

    private void LoadMarvineCommandHistory()
    {
        try
        {
            if (File.Exists(MarvineHistoryFile))
                _enteredCommands.AddRange(File.ReadLines(MarvineHistoryFile)
                    .Where(line => line.Length is > 0 and <= 4096)
                    .TakeLast(_settings.MarvineHistoryLimit));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DaxAudit.Write("marvine", "history_load_failed", ex.GetType().Name);
        }
        _commandHistoryIndex = _enteredCommands.Count;
    }

    private void RememberMarvineCommand(string command)
    {
        if (command.Length > 4096) return;
        _enteredCommands.Add(command);
        if (_enteredCommands.Count > _settings.MarvineHistoryLimit)
            _enteredCommands.RemoveRange(0, _enteredCommands.Count - _settings.MarvineHistoryLimit);
        _commandHistoryIndex = _enteredCommands.Count;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MarvineHistoryFile)!);
            File.WriteAllLines(MarvineHistoryFile, _enteredCommands);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DaxAudit.Write("marvine", "history_save_failed", ex.GetType().Name);
        }
    }

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page }) Navigate(page);
    }

    private void Navigate(string page)
    {
        if (!_navigation.ContainsKey(page)) return;
        _processMonitor?.Dispose();
        _processMonitor = null;
        _embeddedShell?.Dispose();
        _embeddedShell = null;
        _page = page;
        _consoleOutput = null;
        _consoleInput = null;
        _consoleScroll = null;

        foreach (var pair in _navigation)
        {
            pair.Value.Background = pair.Key == page ? Resource("SelectedBrush") : Brushes.Transparent;
            pair.Value.Foreground = pair.Key == page ? Accent : Muted;
            pair.Value.FontWeight = pair.Key == page ? FontWeights.SemiBold : FontWeights.Normal;
            pair.Value.MinHeight = 45;
        }

        var headings = new Dictionary<string, (string Name, string Hint)>
        {
            ["overview"] = (TallowText.Get("ui.main.0001"), TallowText.Get("ui.main.0016")),
            ["info"] = (TallowText.Get("ui.main.0002"), TallowText.Get("ui.main.0017")),
            ["console"] = (TallowText.Get("ui.main.0003"), TallowText.Get("ui.main.0018")),
            ["guard"] = ("DAX Guard", TallowText.Get("ui.main.0019")),
            ["logs"] = ("DAX Logs", TallowText.Get("ui.main.0020")),
            ["activity"] = (TallowText.Get("ui.main.0006"), TallowText.Get("ui.main.0021")),
            ["recovery"] = ("DAX Recovery", TallowText.Get("ui.main.0022")),
            ["settings"] = (TallowText.Get("ui.main.0008"), TallowText.Get("ui.main.0023")),
            ["sources"] = (TallowText.Get("ui.main.0009"), TallowText.Get("ui.main.0024"))
        };
        PageTitle.Text = headings[page].Name;
        PageSubtitle.Text = headings[page].Hint;
        ContentPanel.Children.Clear();
        switch (page)
        {
            case "overview": BuildOverview(); break;
            case "info": BuildInfo(); break;
            case "console": BuildConsole(); break;
            case "guard": BuildGuard(); break;
            case "logs": BuildLogs(); break;
            case "activity": BuildActivity(); break;
            case "recovery": BuildRecovery(); break;
            case "settings": BuildSettings(); break;
            case "sources": BuildSources(); break;
        }
        MainScroll.ScrollToTop();
        UiMotion.AnimatePage(ContentPanel);
    }

    private void UpdateConsoleHeight()
    {
        if (_consoleScroll is not null)
            _consoleScroll.Height = Math.Clamp(MainScroll.ActualHeight - 250, 270, 800);
    }

    private TextBlock Text(string value, double size = 13, bool heading = false, bool muted = false)
    {
        return new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            FontSize = size,
            FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = muted ? Muted : Ink,
            LineHeight = size * 1.55
        };
    }

    private Border Card(UIElement child)
    {
        return new Border
        {
            Background = Surface,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 14),
            Child = child
        };
    }

    private Grid SettingsToggleRow(string title, string description, bool initial, Action<bool> changed)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 20) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        copy.Children.Add(Text(title, 14, heading: true));
        var detail = Text(description, 12, muted: true);
        detail.Margin = new Thickness(0, 3, 0, 0);
        copy.Children.Add(detail);
        row.Children.Add(copy);
        var toggle = new ToggleButton
        {
            Style = (Style)FindResource("SettingToggle"),
            IsChecked = initial,
            VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Automation.AutomationProperties.SetName(toggle, title);
        toggle.Checked += (_, _) => changed(true);
        toggle.Unchecked += (_, _) => changed(false);
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);
        return row;
    }

    private Grid SettingsChoiceRow(string title, string description, ComboBox choice)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 20) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        copy.Children.Add(Text(title, 14, heading: true));
        var detail = Text(description, 12, muted: true);
        detail.Margin = new Thickness(0, 3, 0, 0);
        copy.Children.Add(detail);
        row.Children.Add(copy);
        choice.Width = 220;
        choice.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(choice, title);
        Grid.SetColumn(choice, 1);
        row.Children.Add(choice);
        return row;
    }

    private Button ActionButton(string label, Action callback, bool primary = false, bool enabled = true)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = label, TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center, MaxWidth = 280
            },
            Style = (Style)FindResource(primary ? "PrimaryButton" : "SecondaryButton"),
            Margin = new Thickness(0, 0, 10, 10),
            MinHeight = 44,
            MinWidth = 142,
            Padding = new Thickness(18, 11, 18, 11),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            FocusVisualStyle = null,
            Cursor = Cursors.Hand,
            IsEnabled = enabled
        };
        UiMotion.AttachButtonFeedback(button);
        button.Click += (_, _) => callback();
        return button;
    }

    private void AddIntro(string eyebrow, string heading, string description)
    {
        var eyebrowLabel = Text(eyebrow.ToUpperInvariant(), 10, heading: true);
        eyebrowLabel.Foreground = Accent;
        eyebrowLabel.Margin = new Thickness(0, 0, 0, 12);
        ContentPanel.Children.Add(eyebrowLabel);
        var title = Text(heading, 31, heading: true);
        title.Margin = new Thickness(0, 0, 0, 10);
        ContentPanel.Children.Add(title);
        var body = Text(description, 13, muted: true);
        body.Margin = new Thickness(0, 0, 0, 22);
        ContentPanel.Children.Add(body);
    }

    private void AddSection(string title, string? description = null)
    {
        var h = Text(title, 20, heading: true);
        h.Margin = new Thickness(0, 16, 0, description is null ? 15 : 3);
        ContentPanel.Children.Add(h);
        if (description is not null)
        {
            var sub = Text(description, 12, muted: true);
            sub.Margin = new Thickness(0, 0, 0, 14);
            ContentPanel.Children.Add(sub);
        }
    }

    private void AddNotice(string title, string description)
    {
        var content = new StackPanel();
        var heading = Text("ⓘ  " + title, 14, heading: true);
        heading.Foreground = Accent;
        content.Children.Add(heading);
        var body = Text(description, 12, muted: true);
        body.Margin = new Thickness(0, 8, 0, 0);
        content.Children.Add(body);
        var note = Card(content);
        note.Background = Resource("SelectedBrush");
        ContentPanel.Children.Add(note);
    }

    private Border SummaryTile(string title, string value, string? description)
    {
        var stack = new StackPanel();
        stack.Children.Add(Text(title.ToUpperInvariant(), 10, muted: true));
        var main = Text(value, 19, heading: true);
        main.Foreground = Accent;
        main.Margin = new Thickness(0, 8, 0, description is null ? 0 : 7);
        stack.Children.Add(main);
        if (description is not null) stack.Children.Add(Text(description, 11, muted: true));
        var tile = Card(stack);
        tile.Width = 214;
        tile.Margin = new Thickness(0, 0, 12, 12);
        return tile;
    }

    private void BuildOverview()
    {
        AddIntro(TallowText.Get("ui.main.0025"), TallowText.Get("ui.main.0026"),
            TallowText.Get("ui.main.0027"));
        var tiles = new WrapPanel();
        tiles.Children.Add(SummaryTile(TallowText.Get("ui.main.0028"), ReleaseVersion, null));
        tiles.Children.Add(SummaryTile(TallowText.Get("ui.main.0029"),
            AccessProfiles.DisplayName(_settings.ActiveProfile),
            AccessProfiles.Description(_settings.ActiveProfile)));
        var daxTile = SummaryTile(TallowText.Get("ui.main.0030"), TallowText.Get("ui.main.0031"),
            TallowText.Get("ui.main.0032"));
        daxTile.Cursor = Cursors.Hand;
        daxTile.MouseLeftButtonUp += (_, _) => ShowDaxInfo();
        tiles.Children.Add(daxTile);
        ContentPanel.Children.Add(tiles);
        AddNotice(TallowText.Get("ui.main.0033"), TallowText.Get("ui.main.0034"));
    }

    private void ShowDaxInfo()
    {
        _ = YettaWarningWindow.Show(this,
            new WarningRequest(WarningKind.Dax, TallowText.Get("ui.main.0035"),
                "DAX · Defense & Access eXecutive\n\n" +
                TallowText.Get("ui.main.0036") + "\n\n" +
                TallowText.Get("ui.main.0037") +
                TallowText.Get("ui.main.0038") + "\n\n" +
                TallowText.Get("ui.main.0039") + "\n\n" +
                TallowText.Get("ui.main.0040") +
                TallowText.Get("ui.main.0041"),
                Subject: "DAX", Publisher: TallowText.Get("ui.main.0042")),
            _settings.ActiveProfile, nonModal: true);
    }

    private static bool IsProcessAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception) { return false; }
    }

    private void BuildInfo()
    {
        AddIntro(TallowText.Get("ui.main.0043"), TallowText.Get("ui.main.0044"),
            TallowText.Get("ui.main.0045"));

        var status = new StackPanel();
        status.Children.Add(Text(TallowText.Get("ui.main.0046") + ReleaseVersion, 21, heading: true));
        status.Children.Add(Text(TallowText.Get("ui.main.0047") + AccessProfiles.DisplayName(_settings.ActiveProfile), 14));
        status.Children.Add(Text(AccessProfiles.Description(_settings.ActiveProfile), 12, muted: true));
        status.Children.Add(Text(TallowText.Get("ui.main.0048") +
            string.Join(", ", _settings.InstalledProfiles.Select(AccessProfiles.DisplayName)), 12, muted: true));
        ContentPanel.Children.Add(Card(status));

        AddSection(TallowText.Get("ui.main.0049"));
        var capabilities = new StackPanel();
        capabilities.Children.Add(Text(IsProcessAdministrator()
            ? TallowText.Get("ui.main.0050")
            : TallowText.Get("ui.main.0051"), 13, heading: true));
        capabilities.Children.Add(Text(
            TallowText.Get("ui.main.0052"), 12, muted: true));
        capabilities.Children.Add(Text(
            TallowText.Get("ui.main.0053"), 12, muted: true));
        ContentPanel.Children.Add(Card(capabilities));

        AddSection(TallowText.Get("ui.main.0054"));
        var specs = new (string Label, string Value)[]
        {
            (TallowText.Get("ui.main.0055"), SystemSnapshot.Os),
            (TallowText.Get("ui.main.0056"), SystemSnapshot.Architecture),
            (".NET", SystemSnapshot.Runtime),
            (TallowText.Get("ui.main.0057"), SystemSnapshot.LogicalProcessors.ToString()),
            (TallowText.Get("ui.main.0058"), SystemSnapshot.User),
            (TallowText.Get("ui.main.0059"), SystemSnapshot.Machine),
            (TallowText.Get("ui.main.0060"), SystemSnapshot.Uptime.ToString(@"d\.hh\:mm\:ss")),
            (TallowText.Get("ui.main.0061"), "Windows 11"),
            (TallowText.Get("ui.main.0062"), _store.FilePath)
        };
        var deviceCard = new StackPanel();
        foreach (var (label, value) in specs)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            row.Children.Add(Text(label, 10, heading: true, muted: true));
            row.Children.Add(Text(value, 13));
            deviceCard.Children.Add(row);
        }
        deviceCard.Children.Add(Text(TallowText.Get("ui.main.0063") +
            (Path.GetDirectoryName(_store.FilePath) ?? TallowText.Get("ui.main.0064")), 12, muted: true));
        deviceCard.Children.Add(ActionButton("Скопировать сводку", () =>
        {
            try
            {
                Clipboard.SetText("Yetta " + ReleaseVersion + Environment.NewLine +
                    "Профиль: " + AccessProfiles.DisplayName(_settings.ActiveProfile) + Environment.NewLine +
                    string.Join(Environment.NewLine, specs.Select(x => x.Label + ": " + x.Value)));
                Notify("Информация", "Сводка скопирована в буфер обмена.");
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                ShowYettaNotice("Буфер обмена", "Не удалось скопировать сводку.", WarningKind.Caution);
            }
        }));
        ContentPanel.Children.Add(Card(deviceCard));
        AddNotice(TallowText.Get("ui.main.0065"), TallowText.Get("ui.main.0066"));
    }

    private void BuildGuard()
    {
        AddIntro(TallowText.Get("ui.main.0067"), TallowText.Get("ui.main.0068"),
            TallowText.Get("ui.main.0069"));

        var current = new StackPanel();
        current.Children.Add(Text(TallowText.Get("ui.main.0070"), 10, heading: true, muted: true));
        var active = Text(AccessProfiles.DisplayName(_settings.ActiveProfile), 25, heading: true);
        active.Foreground = Accent;
        active.Margin = new Thickness(0, 8, 0, 8);
        current.Children.Add(active);
        current.Children.Add(Text(AccessProfiles.Description(_settings.ActiveProfile), 12, muted: true));

        var choices = new ComboBox
        {
            Style = (Style)FindResource("AccessCombo"),
            Margin = new Thickness(0, 16, 0, 0),
            MaxWidth = 350,
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 280,
            Padding = new Thickness(10, 7, 10, 7)
        };
        foreach (string profile in AccessProfiles.All.Where(p => _settings.InstalledProfiles.Contains(p)))
        {
            var item = new ComboBoxItem
            {
                Tag = profile,
                Content = (profile == _settings.ActiveProfile ? "✓  " : "    ") + AccessProfiles.DisplayName(profile),
                Foreground = Ink,
                Background = Brushes.Transparent
            };
            choices.Items.Add(item);
            if (profile == _settings.ActiveProfile) choices.SelectedItem = item;
        }
        choices.SelectionChanged += (_, _) =>
        {
            if (choices.SelectedItem is not ComboBoxItem { Tag: string profile } ||
                profile == _settings.ActiveProfile) return;
            if (profile == "Administrator" && !IsProcessAdministrator())
            {
                ShowYettaNotice("Недостаточно прав Windows",
                    "Перезапусти Yetta от имени администратора, чтобы выбрать этот профиль.", WarningKind.Caution);
                Navigate("guard");
                return;
            }
            if (AccessProfiles.Apply(_settings, profile))
            {
                SavePreferences();
                Navigate("guard");
            }
        };
        current.Children.Add(choices);
        ContentPanel.Children.Add(Card(current));

        AddSection(TallowText.Get("ui.main.0071"), TallowText.Get("ui.main.0072"));
        foreach (string profile in AccessProfiles.All)
        {
            bool installed = _settings.InstalledProfiles.Contains(profile);
            bool applied = profile == _settings.ActiveProfile;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var description = new StackPanel();
            string code = profile switch
            {
                "Guest" => "GUEST",
                "User" => "USER", "Administrator" => "ADMIN",
                _ => "SYS"
            };
            var headingLine = new StackPanel { Orientation = Orientation.Horizontal };
            headingLine.Children.Add(Text(code + "   ", 12, heading: true, muted: true));
            headingLine.Children.Add(Text(AccessProfiles.DisplayName(profile), 15, heading: true));
            description.Children.Add(headingLine);
            description.Children.Add(Text(AccessProfiles.Description(profile), 12, muted: true));
            Grid.SetColumn(description, 0);
            row.Children.Add(description);

            var button = ActionButton(
                applied ? TallowText.Get("ui.main.0073") : installed ? TallowText.Get("ui.main.0074") : TallowText.Get("ui.main.0075"),
                () => InstallOrApplyProfile(profile), applied == false && installed, enabled: !applied);
            button.VerticalAlignment = VerticalAlignment.Center;
            button.Margin = new Thickness(13, 0, 0, 0);
            Grid.SetColumn(button, 1);
            row.Children.Add(button);
            ContentPanel.Children.Add(Card(row));
        }
        AddSection(TallowText.Get("ui.main.0076"), TallowText.Get("ui.main.0077"));
        var ttyPanel = new StackPanel();
        ttyPanel.Children.Add(Text("•••• •••• •••• •••• •••• •••• •••• ••••", 17, heading: true));
        ttyPanel.Children.Add(Text(
            TallowText.Get("ui.main.0078") +
            TallowText.Get("ui.main.0079") +
            TallowText.Get("ui.main.0080"),
            12, muted: true));
        var ttyActions = new WrapPanel { Margin = new Thickness(0, 15, 0, 0) };
        ttyActions.Children.Add(ActionButton(TallowText.Get("ui.main.0081"), () =>
        {
            if (!ConfirmYettaAction(TallowText.Get("ui.main.0082"),
                TallowText.Get("ui.main.0083") +
                TallowText.Get("ui.main.0084"))) return;
            try
            {
                Clipboard.SetText(TtyKeyStore.Current());
                Notify(TallowText.Get("ui.main.0004"), TallowText.Get("ui.main.0085"));
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or
                System.Runtime.InteropServices.ExternalException or IOException or InvalidDataException)
            {
                ShowYettaNotice(TallowText.Get("ui.main.0086"), ex.Message, WarningKind.Caution);
            }
        }));
        ttyActions.Children.Add(ActionButton(TallowText.Get("ui.main.0087"), () =>
        {
            if (!ConfirmYettaAction(TallowText.Get("ui.main.0088"),
                TallowText.Get("ui.main.0089")))
                return;
            try
            {
                _ = TtyKeyStore.Rotate();
                Notify(TallowText.Get("ui.main.0004"), TallowText.Get("ui.main.0090"));
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
            {
                ShowYettaNotice(TallowText.Get("ui.main.0086"), ex.Message, WarningKind.Caution);
            }
        }, true));
        ttyPanel.Children.Add(ttyActions);
        ContentPanel.Children.Add(Card(ttyPanel));
        AddNotice(TallowText.Get("ui.main.0091"),
            TallowText.Get("ui.main.0092"));
    }

    private void InstallOrApplyProfile(string profile)
    {
        if (profile == "Administrator" && !IsProcessAdministrator())
        {
            ShowYettaNotice("Недостаточно прав Windows",
                "Для профиля администратора запусти Yetta от имени администратора.", WarningKind.Caution);
            return;
        }
        if (profile != "Administrator" && !AccessProfiles.CanInstallLogicalProfile(profile))
        {
            ShowYettaNotice(TallowText.Get("ui.main.0093"),
                TallowText.Get("ui.main.0094") + AccessProfiles.DisplayName(profile) +
                TallowText.Get("ui.main.0095"),
                WarningKind.Dax);
            return;
        }
        if (_settings.InstalledProfiles.Contains(profile))
            AccessProfiles.Apply(_settings, profile);
        else if (profile == "Administrator")
            AccessProfiles.InstallAdministrator(_settings, verifiedWindowsAdministrator: true);
        else
            AccessProfiles.Install(_settings, profile);
        SavePreferences();
        Notify(TallowText.Get("ui.main.0004"), TallowText.Get("ui.main.0096") + AccessProfiles.DisplayName(profile) + TallowText.Get("ui.main.0097"));
        Navigate("guard");
    }


    private void OnFooterLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        e.Handled = true;
        if (e.Uri.Scheme == Uri.UriSchemeHttps && e.Uri.Host == "icons8.ru")
            OpenExternal(e.Uri.AbsoluteUri, "Icons8");
    }

    private void Notify(string source, string message)
    {
        _notifications.RemoveAll(x => x.Time < DateTime.Now.AddHours(-_settings.NotificationRetentionHours));
        _notifications.Add((DateTime.Now, source, message));
        if (_notifications.Count > 1000) _notifications.RemoveAt(0);
        try { _activityStore.Add(source, message, _settings.NotificationRetentionHours); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DaxAudit.Write("activity", "persist_failed", ex.GetType().Name);
        }

        var toast = new Border
        {
            Width = 314,
            Background = Surface,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(13),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 8),
            Opacity = 0
        };
        var body = new StackPanel();
        var header = new DockPanel();
        var close = new Button
        {
            Content = "×", Width = 30, Height = 26,
            Background = Brushes.Transparent, Foreground = Muted,
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand
        };
        close.Click += (_, _) => ToastHost.Children.Remove(toast);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var icon = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/31.png")),
            Width = 25, Height = 25, Margin = new Thickness(0, 0, 10, 0)
        };
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon);
        header.Children.Add(Text(source, 13, heading: true));
        body.Children.Add(header);
        var details = Text(message, 12, muted: true);
        details.Margin = new Thickness(0, 8, 0, 4);
        body.Children.Add(details);
        body.Children.Add(ActionButton(TallowText.Get("ui.main.0098"), () =>
        {
            ToastHost.Children.Remove(toast);
            Navigate("activity");
        }));
        toast.Child = body;
        if (ToastHost.Children.Count >= 5) ToastHost.Children.RemoveAt(0);
        ToastHost.Children.Add(toast);
        toast.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(
            0, 1, TimeSpan.FromMilliseconds(220)));
        if (_settings.NotificationSoundsEnabled)
        {
            try { System.Media.SystemSounds.Asterisk.Play(); }
            catch (InvalidOperationException) { }
        }
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
        timer.Tick += (_, _) => { timer.Stop(); ToastHost.Children.Remove(toast); };
        timer.Start();
    }

    private void BuildActivity()
    {
        AddIntro(TallowText.Get("ui.main.0099"), TallowText.Get("ui.main.0006"),
            TallowText.Get("ui.main.0100"));
        if (_notifications.Count == 0)
        {
            AddNotice(TallowText.Get("ui.main.0101"), TallowText.Get("ui.main.0102"));
            return;
        }
        foreach (var group in _notifications.GroupBy(note => note.Source).OrderBy(group => group.Key))
        {
            AddSection(group.Key + " · " + group.Count() + TallowText.Get("ui.main.0103"));
            foreach (var note in group.Reverse())
            {
                var panel = new StackPanel();
                panel.Children.Add(Text(note.Time.ToString(TallowText.Get("ui.main.0104")), 12, heading: true));
                panel.Children.Add(Text(note.Message, 12, muted: true));
                ContentPanel.Children.Add(Card(panel));
            }
        }
    }

    private void BuildLogs()
    {
        AddIntro(TallowText.Get("ui.main.0105"), TallowText.Get("ui.main.0106"),
            TallowText.Get("ui.main.0107"));
        ContentPanel.Children.Add(Card(Text(TallowText.Get("ui.main.0108") + DaxAudit.FilePath, 11, muted: true)));
        if (!File.Exists(DaxAudit.FilePath))
        {
            AddNotice(TallowText.Get("ui.main.0109"), TallowText.Get("ui.main.0110"));
            return;
        }
        try
        {
            var tail = File.ReadLines(DaxAudit.FilePath).TakeLast(150).Reverse().ToList();
            foreach (string record in tail)
                ContentPanel.Children.Add(Card(Text(record, 11)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddNotice(TallowText.Get("ui.main.0111"), ex.Message);
        }
    }

    private void BuildRecovery()
    {
        AddIntro(TallowText.Get("ui.main.0112"), TallowText.Get("ui.main.0113"),
            TallowText.Get("ui.main.0114"));
        AddNotice(TallowText.Get("ui.main.0115"),
            TallowText.Get("ui.main.0116"));
    }

    private void BuildConsole()
    {
        // Windows Terminal-inspired shell: only Marvine and one PowerShell
        // engine are offered. "Windows Terminal" is the UI, not a second
        // misleading pseudo-terminal engine.
        if (_consoleMode == "Windows Terminal")
            _consoleMode = "Windows PowerShell";

        var shell = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(43, 46, 51)),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(0),
            ClipToBounds = true,
            MinHeight = 430,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var shellLayout = new DockPanel { LastChildFill = true };
        var tabBar = new DockPanel
        {
            LastChildFill = false,
            Background = new SolidColorBrush(Color.FromRgb(54, 58, 64)),
            Height = 44
        };
        DockPanel.SetDock(tabBar, Dock.Top);
        shellLayout.Children.Add(tabBar);

        // The tab row is one functional console, with distinct profiles for
        // Yetta commands and the genuine unelevated Windows PowerShell session.
        var tabButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        DockPanel.SetDock(tabButtons, Dock.Left);
        tabBar.Children.Add(tabButtons);

        Button TabButton(string mode, string symbol)
        {
            bool selected = _consoleMode == mode;
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new TextBlock
            {
                Text = symbol, FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 11, 0)
            });
            label.Children.Add(new TextBlock
            {
                Text = mode, FontSize = 12,
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            });
            var tab = new Button
            {
                Content = label,
                Padding = new Thickness(16, 0, 16, 0),
                MinWidth = mode == "Marvine" ? 126 : 186,
                Height = 41,
                Foreground = Brushes.White,
                Background = selected
                    ? new SolidColorBrush(Color.FromRgb(43, 46, 51))
                    : new SolidColorBrush(Color.FromRgb(54, 58, 64)),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(1, 3, 0, 0),
                Cursor = Cursors.Hand,
                FocusVisualStyle = null
            };
            tab.Click += (_, _) =>
            {
                if (_consoleMode == mode) return;
                _consoleMode = mode;
                Navigate("console");
            };
            return tab;
        }

        tabButtons.Children.Add(TabButton("Marvine", "⌘"));
        tabButtons.Children.Add(TabButton("Windows PowerShell", "❯"));

        // "+" opens a fresh PowerShell session. The current PowerShell
        // session is disposed by Navigate; this is not multiple ConPTY sessions.
        var addTab = new Button
        {
            Content = "+", FontSize = 21,
            Width = 43, Height = 39,
            Foreground = Brushes.White, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ToolTip = TallowText.Get("ui.main.0117"),
            FocusVisualStyle = null
        };
        addTab.Click += (_, _) =>
        {
            _consoleMode = "Windows PowerShell";
            Navigate("console");
        };
        tabButtons.Children.Add(addTab);

        var more = new Button
        {
            Content = "⌄", Width = 34, Height = 39,
            FontSize = 17,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            ToolTip = TallowText.Get("ui.main.0118"),
            FocusVisualStyle = null
        };
        var profiles = new ContextMenu();
        foreach (var profile in new[] { "Marvine", "Windows PowerShell" })
        {
            string chosen = profile;
            var item = new MenuItem { Header = profile, IsChecked = _consoleMode == profile };
            item.Click += (_, _) =>
            {
                if (chosen == _consoleMode) return;
                _consoleMode = chosen;
                Navigate("console");
            };
            profiles.Items.Add(item);
        }
        more.Click += (_, _) =>
        {
            profiles.PlacementTarget = more;
            profiles.IsOpen = true;
        };
        tabButtons.Children.Add(more);

        // The black content plane has no separator strokes, cards or focus
        // rectangles. Full TTY support would require a separate ConPTY host.
        var workspace = new Grid
        {
            Background = new SolidColorBrush(Color.FromRgb(43, 46, 51)),
            Margin = new Thickness(0)
        };
        var body = new StackPanel { Margin = new Thickness(12, 11, 12, 13) };
        workspace.Children.Add(body);
        shellLayout.Children.Add(workspace);
        shell.Child = shellLayout;
        ContentPanel.Children.Add(shell);

        if (_consoleMode == "Marvine")
        {
            var display = new StackPanel();
            if (_history.Count == 0)
            {
                display.Children.Add(ConsoleLine(TallowText.Get("ui.main.0119") + ReleaseVersion, true));
                display.Children.Add(ConsoleLine(TallowText.Get("ui.main.0120"), false));
            }
            else
            {
                foreach (var (cmd, response) in _history)
                {
                    if (cmd.Length > 0) display.Children.Add(ConsoleLine("> " + cmd, true));
                    display.Children.Add(cmd.Length == 0
                        ? ConsoleOutputElement(response) : ConsoleLine(response, false));
                }
            }
            _consoleOutput = display;
            _consoleScroll = new ScrollViewer
            {
                Focusable = false, FocusVisualStyle = null,
                BorderThickness = new Thickness(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = display
            };
            UpdateConsoleHeight();
            body.Children.Add(_consoleScroll);
            _consoleInput = BuildTerminalInput(TallowText.Get("ui.main.0121"), RunMarvine);
            body.Children.Add(_consoleInput);
            var suggestions = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(185, 195, 201)),
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 11,
                Margin = new Thickness(4, 7, 0, 6),
                Text = "Подсказка: /help · ↑↓ история · Tab дополнить"
            };
            _consoleInput.TextChanged += (_, _) =>
            {
                var matches = ConsoleEngine.Suggestions(_consoleInput.Text);
                suggestions.Text = matches.Count == 0
                    ? "Нет подсказок · /help — список команд"
                    : string.Join("    ", matches.Take(3));
            };
            body.Children.Add(suggestions);
            body.Children.Add(ActionButton(TallowText.Get("ui.main.0122"), RunMarvine, true));
        }
        else if (!AccessProfiles.CanLaunchUnprivilegedTerminal(_settings))
        {
            var note = Text(
                TallowText.Get("ui.main.0123") +
                TallowText.Get("ui.main.0124"), 13);
            note.Foreground = Brushes.White;
            body.Children.Add(note);
            body.Children.Add(ActionButton(TallowText.Get("ui.main.0125"), () => Navigate(TallowText.Get("ui.main.0126"))));
        }
        else if (IsProcessAdministrator())
        {
            var note = Text(
                TallowText.Get("ui.main.0127") +
                TallowText.Get("ui.main.0128"), 13);
            note.Foreground = Brushes.White;
            body.Children.Add(note);
        }
        else if (!_settings.EmbeddedShellConsentGranted)
        {
            var note = Text(
                TallowText.Get("ui.main.0129") +
                TallowText.Get("ui.main.0130"), 13);
            note.Foreground = Brushes.White;
            note.Margin = new Thickness(0, 0, 0, 15);
            body.Children.Add(note);
            body.Children.Add(ActionButton(TallowText.Get("ui.main.0131"), () =>
            {
                if (ConsentDialog.Confirm(this,
                    TallowText.Get("ui.main.0132"),
                    TallowText.Get("ui.main.0133") +
                    TallowText.Get("ui.main.0134") +
                    TallowText.Get("ui.main.0135") +
                    TallowText.Get("ui.main.0136"),
                    YettaWarningLevel.Consent))
                {
                    _settings.EmbeddedShellConsentGranted = true;
                    SavePreferences();
                    Navigate("console");
                }
            }, true));
        }
        else
        {
            var description = Text(
                TallowText.Get("ui.main.0137"), 11);
            description.Foreground = Brushes.LightGray;
            description.Margin = new Thickness(0, 0, 0, 10);
            body.Children.Add(description);

            var display = new StackPanel();
            _consoleOutput = display;
            var scrolling = new ScrollViewer
            {
                Focusable = false, FocusVisualStyle = null,
                BorderThickness = new Thickness(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = display
            };
            _consoleScroll = scrolling;
            UpdateConsoleHeight();
            body.Children.Add(scrolling);
            display.Children.Add(ConsoleLine(TallowText.Get("ui.main.0138"), true));
            _consoleInput = BuildTerminalInput(TallowText.Get("ui.main.0139"), RunPowerShell);
            body.Children.Add(_consoleInput);
            var controls = new WrapPanel();
            controls.Children.Add(ActionButton(TallowText.Get("ui.main.0122"), RunPowerShell, true));
            controls.Children.Add(ActionButton(TallowText.Get("ui.main.0140"), () =>
            {
                _processMonitor?.Dispose();
                _processMonitor = null;
                _embeddedShell?.Dispose();
                _embeddedShell = null;
                display.Children.Add(ConsoleLine(TallowText.Get("ui.main.0141"), false));
            }));
            body.Children.Add(controls);
            try
            {
                _embeddedShell = EmbeddedShellSession.Start(line =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_consoleOutput == display)
                        {
                            display.Children.Add(ConsoleOutputElement(line));
                            if (display.Children.Count > 500) display.Children.RemoveAt(1);
                            scrolling.ScrollToEnd();
                        }
                    }));
                });
                _processMonitor = new DaxProcessMonitor(_embeddedShell.ProcessId,
                    message => Notify(TallowText.Get("ui.main.0142"), message));
            }
            catch (Exception ex) when (ex is InvalidOperationException or
                System.ComponentModel.Win32Exception or IOException)
            {
                display.Children.Add(ConsoleLine(TallowText.Get("ui.main.0143") + ex.Message, false));
            }
        }

        // No extra border around the workspace, unlike the former card UI.
        AddNotice(TallowText.Get("ui.main.0144"),
            TallowText.Get("ui.main.0145") +
            TallowText.Get("ui.main.0146") +
            TallowText.Get("ui.main.0147") +
            TallowText.Get("ui.main.0148"));
        _consoleInput?.Focus();
    }

    private TextBox BuildTerminalInput(string hint, Action execute)
    {
        var field = new TextBox
        {
            Background = new SolidColorBrush(Color.FromRgb(58, 62, 68)),
            Foreground = Brushes.White,
            FocusVisualStyle = null,
            BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 14, Padding = new Thickness(10, 10, 10, 10),
            CaretBrush = Brushes.White, ToolTip = hint
        };
        SpellCheck.SetIsEnabled(field, false);
        field.KeyDown += (_, e) =>
        {
            if (_consoleMode == "Marvine" && (e.Key is Key.Up or Key.Down) && _enteredCommands.Count > 0)
            {
                _commandHistoryIndex = Math.Clamp(_commandHistoryIndex +
                    (e.Key == Key.Up ? -1 : 1), 0, _enteredCommands.Count);
                field.Text = _commandHistoryIndex == _enteredCommands.Count
                    ? "" : _enteredCommands[_commandHistoryIndex];
                field.CaretIndex = field.Text.Length;
                e.Handled = true;
            }
            else if (_consoleMode == "Marvine" && e.Key == Key.Tab)
            {
                string? suggestion = ConsoleEngine.Suggestions(field.Text).FirstOrDefault();
                if (suggestion is not null)
                {
                    field.Text = suggestion;
                    field.CaretIndex = field.Text.Length;
                }
                e.Handled = true;
            }
            if (e.Key == Key.Enter)
            {
                execute();
                e.Handled = true;
            }
        };
        return field;
    }

    private async void RunPowerShell()
    {
        if (_consoleInput is null || _embeddedShell is null) return;
        if (IsProcessAdministrator() || !AccessProfiles.CanLaunchUnprivilegedTerminal(_settings)) return;
        var command = _consoleInput.Text;
        if (string.IsNullOrWhiteSpace(command)) return;
        WarningKind? risk = DaxCommandGuard.Classify(command);
        if (risk is WarningKind level)
        {
            bool allowed = YettaWarningWindow.Show(this,
                new WarningRequest(level,
                    TallowText.Get("ui.main.0149"),
                    TallowText.Get("ui.main.0150") +
                    TallowText.Get("ui.main.0151") +
                    TallowText.Get("ui.main.0152") + command,
                    Subject: TallowText.Get("ui.main.0153"),
                    RequireAcknowledgement: true,
                    RequireTty: level == WarningKind.Critical,
                    PotentialDamage: true),
                _settings.ActiveProfile);
            DaxAudit.Write("powershell_preflight", allowed ? "allowed" : "denied",
                "Command pattern category: " + level + ". Command content intentionally omitted.");
            if (!allowed) return;
        }
        _consoleInput.Clear();
        _consoleOutput?.Children.Add(ConsoleLine("> " + command, true));
        DaxAudit.Write("powershell", "submitted", "Command sent through user-level shell. Text omitted.");
        try { await _embeddedShell.SendAsync(command); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or
            ObjectDisposedException)
        {
            _consoleOutput?.Children.Add(ConsoleLine(TallowText.Get("ui.main.0154") + ex.Message, false));
        }
    }

    private TextBlock ConsoleLine(string message, bool prompt)
    {
        var line = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 13,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 9)
        };
        string[] parts = message.Split('\n');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) line.Inlines.Add(new LineBreak());
            string part = parts[i].TrimEnd('\r');
            Brush color = Brushes.White;
            if (!prompt && _settings.MarvineColorset == "EGA")
            {
                color = part switch
                {
                    var text when text.StartsWith("[✔]", StringComparison.Ordinal) || text.StartsWith("[Зелёный]", StringComparison.Ordinal)
                        => new SolidColorBrush(Color.FromRgb(90, 224, 132)),
                    var text when text.StartsWith("[✖]", StringComparison.Ordinal) || text.StartsWith("[Красный]", StringComparison.Ordinal)
                        => new SolidColorBrush(Color.FromRgb(255, 103, 103)),
                    var text when text.StartsWith("[⚠]", StringComparison.Ordinal) || text.StartsWith("[Жёлтый]", StringComparison.Ordinal)
                        => new SolidColorBrush(Color.FromRgb(255, 215, 105)),
                    var text when text.StartsWith("[Серый]", StringComparison.Ordinal)
                        => new SolidColorBrush(Color.FromRgb(168, 178, 188)),
                    var text when text.StartsWith("[☄]", StringComparison.Ordinal) || text.StartsWith("[⌘]", StringComparison.Ordinal)
                        => new SolidColorBrush(Color.FromRgb(109, 193, 255)),
                    _ => new SolidColorBrush(Color.FromRgb(230, 234, 238))
                };
            }
            if (part.Contains("DAX TTY:", StringComparison.Ordinal))
                line.Inlines.Add(new Run(part) { Foreground = color });
            else
                AddFormattedConsoleText(line, part, color);
        }
        return line;
    }

    private void AddFormattedConsoleText(TextBlock line, string value, Brush color)
    {
        var markup = Regex.Matches(value,
            @"\[[^\]]+\]\(https://[^\s)]+\)|https://[^\s]+|\*\*[^*]+\*\*|~~[^~]+~~|__[^_]+__|\*[^*]+\*",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        int cursor = 0;
        foreach (Match item in markup)
        {
            if (item.Index > cursor)
                line.Inlines.Add(new Run(value[cursor..item.Index]) { Foreground = color });
            string token = item.Value;
            if (token.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                (token.StartsWith("[", StringComparison.Ordinal) && token.Contains("](https://", StringComparison.Ordinal)))
            {
                string url = token.StartsWith("[", StringComparison.Ordinal)
                    ? token[(token.IndexOf('(') + 1)..^1] : token;
                string label = token.StartsWith("[", StringComparison.Ordinal)
                    ? token[1..token.IndexOf(']')] : token;
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                {
                    var link = new Hyperlink(new Run(label)) { NavigateUri = uri, Foreground = Accent };
                    link.RequestNavigate += (_, e) => { e.Handled = true; OpenExternal(e.Uri.AbsoluteUri, e.Uri.Host); };
                    line.Inlines.Add(link);
                }
                else line.Inlines.Add(new Run(token) { Foreground = color });
            }
            else
            {
                string text = token.StartsWith('*') && !token.StartsWith("**", StringComparison.Ordinal)
                    ? token[1..^1] : token[2..^2];
                var run = new Run(text) { Foreground = color };
                if (token.StartsWith("**", StringComparison.Ordinal)) run.FontWeight = FontWeights.Bold;
                else if (token.StartsWith("~~", StringComparison.Ordinal)) run.TextDecorations = TextDecorations.Strikethrough;
                else if (token.StartsWith("__", StringComparison.Ordinal)) run.TextDecorations = TextDecorations.Underline;
                else run.FontStyle = FontStyles.Italic;
                line.Inlines.Add(run);
            }
            cursor = item.Index + item.Length;
        }
        if (cursor < value.Length) line.Inlines.Add(new Run(value[cursor..]) { Foreground = color });
    }

    private UIElement ConsoleOutputElement(string message)
    {
        if (!_settings.MarvineSubsyEnabled) return ConsoleLine(message, false);
        var progress = Regex.Match(message, @"\[[#=█]+[-.\s_]*\]\s*(?<value>\d{1,3})%",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (progress.Success && int.TryParse(progress.Groups["value"].Value, out int percent))
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(ConsoleLine("[♨] " + message, false));
            panel.Children.Add(new ProgressBar
            {
                Minimum = 0, Maximum = 100, Value = Math.Clamp(percent, 0, 100),
                Height = 6, Foreground = Accent,
                Background = new SolidColorBrush(Color.FromRgb(79, 84, 90))
            });
            return panel;
        }
        var steps = Regex.Match(message, @"^\s*(?<step>\d+)\s*/\s*(?<total>\d+)\s*[:—-]?\s*(?<label>.*)$",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (steps.Success && int.TryParse(steps.Groups["step"].Value, out int step) &&
            int.TryParse(steps.Groups["total"].Value, out int total) && total > 0)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(ConsoleLine("[♨] Шаг " + step + " из " + total + " · " + steps.Groups["label"].Value, false));
            panel.Children.Add(new ProgressBar
            {
                Minimum = 0, Maximum = total, Value = Math.Clamp(step, 0, total),
                Height = 6, Foreground = Accent,
                Background = new SolidColorBrush(Color.FromRgb(79, 84, 90))
            });
            return panel;
        }
        if (message.Trim() is "|" or "/" or "-" or "\\")
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(ConsoleLine("[♨] Выполняется…", false));
            panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 6, Foreground = Accent });
            return panel;
        }
        if (_consoleMode == "Windows PowerShell" && Regex.IsMatch(message,
            @"(?:\[|\()\s*y\s*/\s*n(?:\s*/\s*a)?(?:\s*/\s*h)?\s*(?:\]|\))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(ConsoleLine(message, false));
            var actions = new WrapPanel();
            async void Choose(string answer)
            {
                actions.IsEnabled = false;
                try
                {
                    if (_embeddedShell is not null) await _embeddedShell.SendAsync(answer);
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException)
                {
                    _consoleOutput?.Children.Add(ConsoleLine("[✖] Marvine Subsy: " + ex.Message, false));
                }
            }
            actions.Children.Add(ActionButton("Да", () => Choose("y"), true));
            actions.Children.Add(ActionButton("Нет", () => Choose("n")));
            if (Regex.IsMatch(message, @"\ba\s*/\s*h\b", RegexOptions.IgnoreCase))
            {
                actions.Children.Add(ActionButton("Да для всех", () => Choose("a")));
                actions.Children.Add(ActionButton("Справка", () => Choose("h")));
            }
            panel.Children.Add(actions);
            return panel;
        }
        return ConsoleLine(message, false);
    }

    private async void RunMarvine()
    {
        if (_consoleInput is null) return;
        string command = _consoleInput.Text.Trim();
        if (command.Length == 0) return;
        _consoleInput.Clear();
        RememberMarvineCommand(command);
        var response = ConsoleEngine.Execute(command,
            new ConsoleContext(_settings.Theme, _settings.ActiveProfile, ReleaseVersion,
                _settings.MarvineSubsyEnabled, _settings.MarvineColorset));
        if (response.ClearOutput)
        {
            _history.Clear();
            _consoleOutput?.Children.Clear();
            return;
        }
        _consoleOutput?.Children.Add(ConsoleLine("> " + command, true));
        string message = response.Message;
        if (response.Request is not null)
        {
            try { message = await HandleMarvineRequestAsync(response.Request, response.Argument); }
            catch (Exception ex) when (ex is HttpRequestException or IOException or
                InvalidDataException or UnauthorizedAccessException or
                System.ComponentModel.Win32Exception or InvalidOperationException or
                ArgumentException or TimeoutException or OperationCanceledException or
                System.Text.Json.JsonException or System.Xml.XmlException)
            {
                message = "[✖] Marvine: " + FriendlyMarvineError(ex);
            }
        }
        _history.Add((command, message));
        if (_history.Count > _settings.MarvineHistoryLimit) _history.RemoveAt(0);
        if (_page == "console" && _consoleMode == "Marvine")
            _consoleOutput?.Children.Add(ConsoleLine(message, false));
        if (response.NavigateTo is string page) Navigate(page);
    }

    private static string FriendlyMarvineError(Exception ex) => ex switch
    {
        OperationCanceledException => "действие отменено или истекло время ожидания.",
        TimeoutException => "DAX не ответил вовремя. Проверь, запущен ли DAX.",
        UnauthorizedAccessException => "нет доступа к файлу или папке Windows.",
        FileNotFoundException missing => missing.Message,
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests }
            => "GitHub временно ограничил запросы. Повтори проверку позже.",
        HttpRequestException => "не удалось связаться с GitHub. Проверь подключение к сети.",
        InvalidDataException => "полученные данные или контрольная сумма повреждены.",
        System.Text.Json.JsonException => "GitHub вернул данные в неожиданном формате.",
        System.Xml.XmlException => "лента релизов GitHub повреждена или недоступна.",
        System.ComponentModel.Win32Exception => "Windows не удалось запустить нужную программу.",
        ArgumentException => "проверь аргументы команды и путь к файлу.",
        IOException => "ошибка чтения или записи файла.",
        _ => ex.Message
    };

    private async Task<string> HandleMarvineRequestAsync(string request, string? argument)
    {
        switch (request)
        {
            case "VERSION":
            {
                var releases = await YettaUpdater.ListReleasesAsync(_updateStop.Token);
                return "[⌘] Yetta: установлена " + ReleaseVersion +
                    (releases.Count == 0 ? "\n[⚠] GitHub: подходящие релизы не найдены."
                    : "\n[ℹ] Доступные релизы:\n" +
                      string.Join("\n", releases.Select(item => item.Tag + " · ID " +
                          (item.Id == 0 ? item.Tag : item.Id.ToString()))));
            }
            case "UPDATE":
                await CheckAndInstallUpdateAsync(true);
                return "[ℹ] Yetta: проверка обновления завершена.";
            case "DOWNLOAD":
            {
                UpdateRelease? release;
                if (long.TryParse(argument, out long id) && id > 0)
                    release = await YettaUpdater.FindReleaseByIdAsync(id, _updateStop.Token);
                else
                    release = YettaUpdater.FindReleaseByTag(argument);
                if (release is null && string.IsNullOrWhiteSpace(argument))
                    return "[⚠] Marvine: укажи ID или тег из команды /yetta version.";
                if (release is null) return "[✖] Marvine: релиз с таким ID не найден среди доступных установщиков.";
                if (!ConfirmYettaAction("Установить Yetta " + release.Tag,
                    "Будет загружен и запущен установщик релиза " + release.Tag + "."))
                    return "[ℹ] Marvine: установка отменена.";
                if (!await _updateGate.WaitAsync(0)) return "[♨] Marvine: другое обновление уже выполняется.";
                try { await InstallReleaseAsync(release); }
                finally { _updateGate.Release(); }
                return "[♨] Marvine: установка запущена.";
            }
            case "TTY_INFO":
                return ConfirmYettaAction("Показать ключ TTY", "Ключ появится в истории текущей консоли. Продолжить?")
                    ? "[☄] DAX TTY: " + TtyKeyStore.Current()
                    : "[ℹ] Marvine: показ ключа отменён.";
            case "TTY_UPDATE":
                return ConfirmYettaAction("Обновить ключ TTY", "Старый ключ перестанет работать. Продолжить?")
                    ? "[✔] DAX TTY: ключ обновлён. Новый ключ: " + TtyKeyStore.Rotate()
                    : "[ℹ] Marvine: обновление ключа отменено.";
            case "SUBSY":
                _settings.MarvineSubsyEnabled = argument == "enable";
                SavePreferences();
                return "[✔] Marvine Subsy: " + (_settings.MarvineSubsyEnabled ? "включена." : "выключена.");
            case "COLORSET":
                _settings.MarvineColorset = argument == "EGA" ? "EGA" : "No Color";
                SavePreferences();
                return "[✔] Marvine: палитра " + _settings.MarvineColorset + ".";
            case "SCRIPT_LIST":
            {
                var scripts = _scripts.List();
                return scripts.Count == 0 ? "[ℹ] Marvine: загруженных Python-скриптов нет."
                    : "[⌘] Marvine: Python-скрипты\n" + string.Join("\n",
                        scripts.Select(item => item.Id + " · " + item.Path +
                            " · " + (item.Running ? "запущен" : "завершён")));
            }
            case "SCRIPT_RUN":
            {
                if (_settings.ActiveProfile != "Administrator" || !IsProcessAdministrator())
                    return "[✖] Marvine: нужен профиль администратора и запущенная от администратора Yetta.";
                if (await DaxAgentGate.RequestAsync("AUTHORIZE_SCRIPT", _updateStop.Token) != "AUTHORIZED")
                    return "[✖] DAX [A]: запуск скрипта отклонён.";
                string? id = PromptScriptId();
                if (id is null) return "[ℹ] Marvine: запуск отменён.";
                string? interpreter = ResolvePythonExecutable();
                if (interpreter is null) return "[ℹ] Marvine: выбор Python отменён.";
                if (!ConfirmYettaAction("Запустить Python-скрипт",
                    "Скрипт получит права текущего процесса Windows. Файл: " + argument))
                    return "[ℹ] Marvine: запуск отменён.";
                _scripts.Start(id, argument ?? "", interpreter, line =>
                {
                    if (Dispatcher.HasShutdownStarted) return;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _history.Add(("", line));
                        if (_history.Count > _settings.MarvineHistoryLimit) _history.RemoveAt(0);
                        if (_page == "console" && _consoleMode == "Marvine")
                            _consoleOutput?.Children.Add(ConsoleOutputElement(line));
                    }));
                });
                return "[✔] Marvine: скрипт " + id + " запущен.";
            }
            case "SCRIPT_STOP":
                return _settings.ActiveProfile == "Guest"
                    ? "[✖] Marvine: для остановки скрипта нужен профиль пользователя."
                    : _scripts.Stop(argument ?? "")
                        ? "[✔] Marvine: скрипт остановлен."
                        : "[ℹ] Marvine: запущенный скрипт с таким ID или путём не найден.";
            default:
                return "[⚠] Marvine: неизвестное действие.";
        }
    }

    private string? PromptScriptId()
    {
        var dialog = new Window
        {
            Owner = this,
            Title = "ID Python-скрипта · Marvine",
            Width = 440, Height = 225,
            MinWidth = 380, MinHeight = 210,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Surface,
            Foreground = Ink,
            FontFamily = SystemFonts.MessageFontFamily,
            ShowInTaskbar = false
        };
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(Text("Присвой скрипту ID", 17, heading: true));
        var hint = Text("От 1 до 16 латинских букв или цифр, без пробелов.", 12, muted: true);
        hint.Margin = new Thickness(0, 5, 0, 12);
        content.Children.Add(hint);
        var input = new TextBox { MaxLength = 16, MinHeight = 36, Padding = new Thickness(8),
            Background = Resource("WindowBackground"), Foreground = Ink };
        content.Children.Add(input);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Отменить", MinWidth = 100, MinHeight = 34, IsCancel = true,
            Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var ok = new Button { Content = "Продолжить", MinWidth = 110, MinHeight = 34, IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (Regex.IsMatch(input.Text, @"\A[A-Za-z0-9]{1,16}\z", RegexOptions.CultureInvariant))
                dialog.DialogResult = true;
            else
                hint.Text = "ID должен содержать только латинские буквы и цифры (1–16).";
        };
        row.Children.Add(cancel);
        row.Children.Add(ok);
        content.Children.Add(row);
        dialog.Content = content;
        dialog.Loaded += (_, _) => input.Focus();
        return dialog.ShowDialog() == true ? input.Text : null;
    }

    private string? ResolvePythonExecutable()
    {
        if (_settings.PythonExecutablePath is string configured && File.Exists(configured))
            return configured;
        string launcher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "py.exe");
        if (File.Exists(launcher)) return launcher;
        var picker = new OpenFileDialog
        {
            Title = "Выбери установленный Python 3",
            Filter = "Python (py.exe;python.exe)|py.exe;python.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true) return null;
        _settings.PythonExecutablePath = picker.FileName;
        SavePreferences();
        return picker.FileName;
    }

    private void BuildSettings()
    {
        AddIntro(TallowText.Get("ui.main.0155"), TallowText.Get("ui.main.0156"),
            "Настройки Yetta и консоли Marvine.");

        AddSection("Поведение приложения", "Связанные параметры собраны в одной панели.");
        var switches = new StackPanel();
        switches.Children.Add(SettingsToggleRow(
            "Автоматические обновления",
            "Загружать и устанавливать новую версию Yetta после её выхода.",
            _settings.AutoUpdatesEnabled, enabled =>
            {
                _settings.AutoUpdatesEnabled = enabled;
                SavePreferences();
                if (enabled) _ = CheckAndInstallUpdateAsync(false);
            }));
        switches.Children.Add(SettingsToggleRow(
            "Звуки уведомлений",
            "Проигрывать системные сигналы для событий Центра активности.",
            _settings.NotificationSoundsEnabled, enabled =>
            {
                _settings.NotificationSoundsEnabled = enabled;
                SavePreferences();
            }));
        switches.Children.Add(SettingsToggleRow(
            "Подтверждение внешних ссылок",
            "Спрашивать перед открытием сайтов из Yetta.",
            _settings.ConfirmExternalLinks, enabled =>
            {
                _settings.ConfirmExternalLinks = enabled;
                SavePreferences();
            }));
        switches.Children.Add(SettingsToggleRow(
            "Интерактивный вывод Marvine",
            "Показывать прогресс и шаги команд как элементы интерфейса (Subsy).",
            _settings.MarvineSubsyEnabled, enabled =>
            {
                _settings.MarvineSubsyEnabled = enabled;
                SavePreferences();
            }));
        ContentPanel.Children.Add(Card(switches));

        AddSection("Хранение и вывод");
        var choices = new StackPanel();
        var colorset = new ComboBox();
        colorset.Items.Add("EGA");
        colorset.Items.Add("No Color");
        colorset.SelectedItem = _settings.MarvineColorset;
        colorset.SelectionChanged += (_, _) =>
        {
            _settings.MarvineColorset = colorset.SelectedItem as string ?? "EGA";
            SavePreferences();
        };
        choices.Children.Add(SettingsChoiceRow("Палитра Marvine",
            "Цветовая схема текста в консоли.", colorset));

        var historySize = new ComboBox();
        foreach (int size in new[] { 100, 500, 1000, 5000, 10000 }) historySize.Items.Add(size);
        historySize.SelectedItem = _settings.MarvineHistoryLimit;
        historySize.SelectionChanged += (_, _) =>
        {
            if (historySize.SelectedItem is not int size) return;
            _settings.MarvineHistoryLimit = size;
            SavePreferences();
        };
        choices.Children.Add(SettingsChoiceRow("История команд",
            "Максимальное число сохранённых команд Marvine.", historySize));

        var retention = new ComboBox();
        foreach (int hours in new[] { 1, 6, 24, 72, 168, 720 })
        {
            var option = new ComboBoxItem
            {
                Style = (Style)FindResource("AccessComboItem"),
                Content = hours switch
                {
                    1 => TallowText.Get("ui.main.0163"),
                    6 => TallowText.Get("ui.main.0164"),
                    24 => TallowText.Get("ui.main.0165"),
                    72 => TallowText.Get("ui.main.0166"),
                    168 => TallowText.Get("ui.main.0167"),
                    _ => TallowText.Get("ui.main.0168")
                },
                Tag = hours
            };
            retention.Items.Add(option);
            if (hours == _settings.NotificationRetentionHours) retention.SelectedItem = option;
        }
        if (retention.SelectedItem is null) retention.SelectedIndex = 2;
        retention.SelectionChanged += (_, _) =>
        {
            if (retention.SelectedItem is not ComboBoxItem selected || selected.Tag is not int hours) return;
            _settings.NotificationRetentionHours = hours;
            _notifications.RemoveAll(x => x.Time < DateTime.Now.AddHours(-hours));
            SavePreferences();
            try { _ = _activityStore.Load(hours); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DaxAudit.Write("activity", "cleanup_failed", ex.GetType().Name);
            }
        };
        choices.Children.Add(SettingsChoiceRow("Хранение уведомлений",
            "Старые события удаляются по истечении срока.", retention));
        ContentPanel.Children.Add(Card(choices));

        AddSection("Обновления", "Ручная проверка доступна при любом положении переключателя.");
        var updates = new StackPanel();
        updates.Children.Add(Text("Установлена версия " + ReleaseVersion, 14, heading: true));
        var updateDetail = Text("Установщик загружается из GitHub Releases и проверяется по SHA-256 перед запуском.", 12, muted: true);
        updateDetail.Margin = new Thickness(0, 5, 0, 16);
        updates.Children.Add(updateDetail);
        updates.Children.Add(ActionButton("Проверить и обновить", async () =>
        {
            await CheckAndInstallUpdateAsync(true);
        }, true));
        ContentPanel.Children.Add(Card(updates));

        AddSection("Встроенная оболочка");
        var shell = new StackPanel();
        shell.Children.Add(Text(_settings.EmbeddedShellConsentGranted
            ? TallowText.Get("ui.main.0172") : TallowText.Get("ui.main.0173"), 12, muted: true));
        var revoke = ActionButton(TallowText.Get("ui.main.0174"), () =>
        {
            _settings.EmbeddedShellConsentGranted = false;
            _processMonitor?.Dispose();
            _processMonitor = null;
            _embeddedShell?.Dispose();
            _embeddedShell = null;
            SavePreferences();
            Navigate("settings");
        }, enabled: _settings.EmbeddedShellConsentGranted);
        revoke.Margin = new Thickness(0, 14, 0, 0);
        shell.Children.Add(revoke);
        ContentPanel.Children.Add(Card(shell));

        AddSection("Окна предупреждений", "Предпросмотр оформления без выполнения действия.");
        var demos = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
        foreach (WarningKind kind in Enum.GetValues<WarningKind>())
        {
            WarningKind choice = kind;
            demos.Children.Add(ActionButton("Показать · " + kind, () =>
            {
                _ = YettaWarningWindow.Show(this,
                    new WarningRequest(choice, TallowText.Get("ui.main.0185"),
                        "Так будет выглядеть предупреждение Yetta. Это только демонстрация.",
                        Subject: "Yetta · UI Test",
                        RequireAcknowledgement: choice is WarningKind.Caution or WarningKind.Dangerous,
                        PotentialDamage: choice is WarningKind.Dangerous or WarningKind.Critical,
                        RecoveryNavigation: choice == WarningKind.Fatal
                            ? () => Navigate("recovery") : null),
                    _settings.ActiveProfile, nonModal: true);
            }));
        }
        ContentPanel.Children.Add(demos);

        AddSection("Локальные данные");
        var local = new StackPanel();
        local.Children.Add(Text("Файл настроек: " + _store.FilePath, 12, muted: true));
        var history = Text("История Marvine хранится локально в пределах выбранного лимита.", 12, muted: true);
        history.Margin = new Thickness(0, 5, 0, 0);
        local.Children.Add(history);
        var dataFolder = ActionButton("Открыть папку данных", () =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe",
                    Path.GetDirectoryName(_store.FilePath)!) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                ShowYettaNotice("Локальные данные", "Не удалось открыть папку данных.", WarningKind.Caution);
            }
        });
        dataFolder.Margin = new Thickness(0, 14, 0, 0);
        local.Children.Add(dataFolder);
        ContentPanel.Children.Add(Card(local));
    }

    private async Task WatchUpdatesAsync()
    {
        while (!_updateStop.IsCancellationRequested)
        {
            if (_settings.AutoUpdatesEnabled)
                await CheckAndInstallUpdateAsync(false);
            try { await Task.Delay(TimeSpan.FromMinutes(5), _updateStop.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task CheckAndInstallUpdateAsync(bool manual)
    {
        if (!await _updateGate.WaitAsync(0)) return;
        try
        {
            var latest = await YettaUpdater.FindUpdateAsync(CurrentVersion, _updateStop.Token);
            if (latest is null)
            {
                if (manual) ShowYettaNotice(TallowText.Get("ui.main.0175"), TallowText.Get("ui.main.0196"));
                return;
            }
            if (!manual && latest.Version == _dismissedAutoVersion) return;
            if (!manual && !_settings.AutoUpdatesEnabled) return;
            if (manual && !ConsentDialog.Confirm(this,
                TallowText.Get("ui.main.0197") + latest.Version + "?",
                TallowText.Get("ui.main.0198") +
                TallowText.Get("ui.main.0199") +
                TallowText.Get("ui.main.0200") +
                TallowText.Get("ui.main.0201") +
                TallowText.Get("ui.main.0202"),
                YettaWarningLevel.Consent))
                return;

            await InstallReleaseAsync(latest, automatic: !manual);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or
            InvalidDataException or InvalidOperationException or
            System.ComponentModel.Win32Exception or TaskCanceledException or
            System.Text.Json.JsonException or System.Xml.XmlException)
        {
            if (ex is OperationCanceledException && !manual) return;
            string message = FriendlyMarvineError(ex);
            DaxAudit.Write("update", "check_failed", ex.GetType().Name);
            if (manual)
                ShowYettaNotice(TallowText.Get("ui.main.0203"), message, WarningKind.Caution);
            else if (DateTimeOffset.UtcNow >= _nextAutoUpdateErrorNotice)
            {
                _nextAutoUpdateErrorNotice = DateTimeOffset.UtcNow.AddHours(6);
                Notify("Обновления Yetta", message);
            }
        }
        finally { _downloadCancel = null; _downloadingVersion = null; _updateGate.Release(); }
    }

    private async Task InstallReleaseAsync(UpdateRelease latest, bool automatic = false)
    {
            var toast = new ToastContentBuilder()
                .AddText("Скачивание новой версии Yetta")
                .AddProgressBar(title: latest.Tag, value: 0, status: "Скачивание...")
                .AddButton(new ToastButton().SetContent("Отменить").AddArgument("action", "cancel-update"));
            try { toast.Show(notification => { notification.Tag = "yetta-update"; notification.Group = "updates"; }); }
            catch (Exception ex) { DaxAudit.Write("update", "toast_unavailable", ex.GetType().Name); }
            var progress = new Progress<double>(value =>
            {
                try
                {
                    var data = ToastContentBuilder.CreateProgressBarData(toast.Content, 0,
                        latest.Tag, value, null, "Скачивание...", (uint)(value * 1000 + 1));
                    ToastNotificationManagerCompat.CreateToastNotifier().Update(data, "yetta-update", "updates");
                }
                catch (Exception ex) { DaxAudit.Write("update", "toast_progress_unavailable", ex.GetType().Name); }
            });
            using var downloadCancel = CancellationTokenSource.CreateLinkedTokenSource(_updateStop.Token);
            _downloadCancel = downloadCancel;
            _downloadingVersion = latest.Version;
            var path = await YettaUpdater.DownloadInstallerAsync(latest, progress, downloadCancel.Token);
            _downloadCancel = null;
            _downloadingVersion = null;
            if (automatic && !_settings.AutoUpdatesEnabled) return;
            try
            {
                if (await DaxAgentGate.RequestAsync("STOP_FOR_UPDATE", _updateStop.Token) == "STOPPING")
                    await Task.Delay(800, _updateStop.Token);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
            {
                DaxAudit.Write("update", "dax_stop_unavailable", ex.GetType().Name);
            }
            var log = Path.Combine(Path.GetDirectoryName(path)!, "YettaSetup-" + latest.Version + ".log");
            var start = new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /LOG=\"" + log + "\""
            };
            using var installer = Process.Start(start) ?? throw new InvalidOperationException("Installer did not start.");
            Close();
    }

    private void BuildSources()
    {
        AddIntro(TallowText.Get("ui.main.0204"), TallowText.Get("ui.main.0205"),
            TallowText.Get("ui.main.0206"));
        AddSection(TallowText.Get("ui.main.0207"));
        AddSource(TallowText.Get("ui.main.0208"), "https://github.com/Ketsuneban/Yetta");
        AddSource(TallowText.Get("ui.main.0209"), "https://github.com/Ketsuneban/Yetta/releases");
        AddSource(TallowText.Get("ui.main.0210"), "https://yetta.pages.dev/");
        AddSection(TallowText.Get("ui.main.0211"));
        AddSource(TallowText.Get("ui.main.0212"), "https://icons8.ru/");
        ContentPanel.Children.Add(Card(Text(
            TallowText.Get("ui.main.0213") +
            TallowText.Get("ui.main.0214"), 12, muted: true)));
        AddSection(TallowText.Get("ui.main.0215"));
        AddSource(TallowText.Get("ui.main.0216"), TallowText.Get("ui.main.0217"));
        ContentPanel.Children.Add(Card(Text(
            TallowText.Get("ui.main.0218"), 12, muted: true)));
    }

    private void AddSource(string title, string address)
    {
        var panel = new StackPanel();
        panel.Children.Add(Text(title, 15, heading: true));
        panel.Children.Add(Text(address, 11, muted: true));
        panel.Children.Add(ActionButton(TallowText.Get("ui.main.0219"), () => OpenExternal(address, title)));
        ContentPanel.Children.Add(Card(panel));
    }

    private void OpenExternal(string target, string title)
    {
        // Only constant URLs and the system's real Windows Terminal are passed
        // by Yetta's own UI. No untrusted command arguments or shell strings.
        if (target == "wt.exe" && !AccessProfiles.CanLaunchUnprivilegedTerminal(_settings))
        {
            ShowYettaNotice(TallowText.Get("ui.main.0220"),
                TallowText.Get("ui.main.0221"));
            return;
        }
        if (_settings.ConfirmExternalLinks && !ConfirmYettaAction(
            TallowText.Get("ui.main.0222"), TallowText.Get("ui.main.0223") + title + TallowText.Get("ui.main.0224"))) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or
            InvalidOperationException or FileNotFoundException)
        {
            ShowYettaNotice(TallowText.Get("ui.main.0225"), TallowText.Get("ui.main.0226") + title + TallowText.Get("ui.main.0227") + ex.Message,
                WarningKind.Caution);
        }
    }
}
