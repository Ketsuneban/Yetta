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

using Yetta.Core;

namespace Yetta.Desktop;

public partial class MainWindow : Window
{
    private static readonly Version CurrentVersion = typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 5, 1);
    private static readonly string ReleaseVersion = CurrentVersion.ToString(3);
    private readonly CancellationTokenSource _updateStop = new();
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private CancellationTokenSource? _downloadCancel;
    private Version? _dismissedAutoVersion;

    private DateTimeOffset _nextAutoUpdateErrorNotice;
    private readonly SettingsStore _store = new();
    private readonly TallowNotificationStore _activityStore = new();
    private readonly AppSettings _settings;
    private readonly Dictionary<string, Button> _navigation;
    private readonly List<string> _enteredCommands = [];
    private readonly MarvineScriptManager _scripts = new();
    private int _commandHistoryIndex;
    private string _commandDraft = "";
    private readonly List<(DateTime Time, string Source, string Message)> _notifications = [];
    private string _page = "overview";
    private ConsoleTranscript? _consoleOutput;
    private TextBox? _consoleInput;
    private readonly DaxAgentGate _daxGate;

    private Brush Ink => Resource("TextPrimary");
    private Brush Muted => Resource("TextSecondary");
    private Brush Accent => Resource("AccentBrush");
    private Brush Surface => Resource("CardBackground");

    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += OnMainShortcut;
        SidebarVersion.Text = "YETTA " + ReleaseVersion + " · WINDOWS 11";
        FooterVersion.Text = ReleaseVersion;
        _settings = _store.Load();
        AccessProfiles.SyncAdministrator(_settings, DaxAdministratorState.Unavailable);
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
            ["sources"] = NavSources,
            ["developers"] = NavDevelopers
        };
        ApplyTheme();
        InitializeIconsAndCursors();
        foreach (var navButton in _navigation.Values)
            UiMotion.AttachButtonFeedback(navButton);
        UiMotion.AttachButtonFeedback(CaptionMinimize, rounded: false);
        UiMotion.AttachButtonFeedback(CaptionMaximize, rounded: false);
        UiMotion.AttachButtonFeedback(CaptionClose, rounded: false);
        SourceInitialized += (_, _) => ApplyBackdrop();
        UpdateCaptionState();
        StateChanged += (_, _) => UpdateCaptionState();
        Navigate("overview");
        _daxGate = new DaxAgentGate(this, OnDaxStatusChanged);
        SystemEvents.UserPreferenceChanged += OnWindowsPreferenceChanged;
        Loaded += (_, _) => _ = WatchUpdatesAsync();
        Loaded += (_, _) => _ = WatchAdministratorAsync();
        Activated += (_, _) => _ = RefreshAdministratorAsync();
        Closed += (_, _) => { SystemEvents.UserPreferenceChanged -= OnWindowsPreferenceChanged; _updateStop.Cancel(); _downloadCancel?.Cancel(); _consoleFollow?.Dispose(); _daxGate?.Dispose(); _scripts?.Dispose(); };
    }

    private void ShowDaxNotice(string title, string message, WarningKind kind = WarningKind.Dax) =>
        YettaWarningWindow.Show(this, new WarningRequest(kind, title, message, Subject: "DAX", Publisher: "Tallow",
            CauseType: WarningCauseType.Dax, ReportOnly: true), _settings.ActiveProfile, nonModal: true);

    private bool ConfirmDaxAction(string title, string message) => YettaWarningWindow.Show(this,
        new WarningRequest(WarningKind.Agreement, title, message, Subject: "DAX", Publisher: "Tallow",
            CauseType: WarningCauseType.Dax), _settings.ActiveProfile);

    private void InitializeIconsAndCursors()
    {
        IconAssets.SetNavLogo(NavOverview, "Navigation/Home.png", TallowText.Get("ui.main.0001"));
        IconAssets.SetNavLogo(NavInfo, "Navigation/Info.png", TallowText.Get("ui.main.0002"));
        IconAssets.SetNavLogo(NavConsole, "Causes/12.png", TallowText.Get("ui.main.0003"));
        IconAssets.SetNavLogo(NavDevelopers, "Profiles/Developers.png", "Для разработчиков");
        IconAssets.SetNavLogo(NavGuard, "Navigation/Guard.png", TallowText.Get("ui.main.0004"));
        IconAssets.SetNavLogo(NavLogs, "Navigation/Logs.png", TallowText.Get("ui.main.0005"));
        _hasUnreadActivity = _notifications.Count > 0;
        UpdateActivityIcon();
        IconAssets.SetNavLogo(NavRecovery, "Navigation/Recovery.png", TallowText.Get("ui.main.0007"));
        IconAssets.SetNavLogo(NavSettings, "Navigation/Settings.png", TallowText.Get("ui.main.0008"));
        IconAssets.SetNavLogo(NavSources, "Navigation/Sources.png", TallowText.Get("ui.main.0009"));
        Cursor = Cursors.Arrow;
    }

    private bool _hasUnreadActivity;
    private void UpdateActivityIcon() => IconAssets.SetNavLogo(NavActivity,
        "Navigation/" + (_hasUnreadActivity ? "ActivityUnread" : "ActivityEmpty") + ".png",
        TallowText.Get("ui.main.0006"));

    private void ShowYettaNotice(string title, string message, WarningKind kind = WarningKind.Dax) =>
        YettaWarningWindow.Show(this,
            new WarningRequest(kind, title, message, Subject: "Yetta", ReportOnly: true),
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
        Windows.UI.ViewManagement.UISettings? ui = null;
        try { ui = new Windows.UI.ViewManagement.UISettings(); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
        { DaxAudit.Write("theme", "uisettings_failed", ex.GetType().Name); }
        static Color Native(Windows.UI.Color c) => Color.FromArgb(c.A, c.R, c.G, c.B);
        static Color Mix(Color a, Color b, double amount) => Color.FromRgb(
            (byte)(a.R * (1 - amount) + b.R * amount),
            (byte)(a.G * (1 - amount) + b.G * amount),
            (byte)(a.B * (1 - amount) + b.B * amount));
        Color foreground = ui is null ? SystemColors.WindowTextColor :
            Native(ui.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground));
        bool dark = foreground.R + foreground.G + foreground.B > 384;
        Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        Color background = dark ? C("#24212B") : C("#F5F2F8");
        foreground = dark ? C("#F3EFF8") : C("#292431");
        Color accent = dark ? C("#CCA9F0") : C("#794DC5");
        if (SystemParameters.HighContrast)
        {
            background = SystemColors.WindowColor;
            foreground = SystemColors.WindowTextColor;
            accent = SystemColors.HighlightColor;
        }
        SetColor("WindowBackground", background);
        SetColor("SidebarBackground", SystemParameters.HighContrast ? background : dark ? C("#2A2633") : C("#ECE7F2"));
        SetColor("CardBackground", SystemParameters.HighContrast ? background : dark ? C("#302B3A") : C("#FCFAFF"));
        SetColor("HoverBackground", SystemParameters.HighContrast ? SystemColors.ControlColor : dark ? C("#3E364A") : C("#E7DEEF"));
        SetColor("AccentBrush", accent);
        SetColor("AccentForeground", SystemParameters.HighContrast ? SystemColors.HighlightTextColor : Colors.White);
        SetBrush("AccentFillBrush", SystemParameters.HighContrast ? SystemColors.HighlightBrush :
            new LinearGradientBrush(new GradientStopCollection {
                new(C("#AE4FB9"), 0), new(C("#9450C2"), 0.44), new(C("#794DC5"), 1)
            }, new Point(0, 0), new Point(1, 1)));
        SetColor("TextPrimary", foreground);
        SetColor("TextSecondary", SystemParameters.HighContrast ? foreground : Mix(foreground, background, 0.30));
        SetColor("SelectedBrush", SystemParameters.HighContrast ? SystemColors.HighlightColor :
            dark ? C("#493A60") : C("#E7DAF4"));
        GlassDialog.IsGlass = glass;
        ApplyBackdrop();
        ThemeIndicator.Text = "Windows 11";
    }

    private void ApplyBackdrop() => WindowsBackdrop.Configure(this);

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
        if (!_navigation.ContainsKey(page) || page == "developers" && !AdministratorActive) return;
        _page = page;
        if (page == "activity") { _hasUnreadActivity = false; UpdateActivityIcon(); }
        _consoleOutput = null;
        _subsyViews.Clear();
        _diagnosticsStatus = null;
        _consoleInput = null;
        ConsoleHost.Child = null;
        ConsoleHost.Visibility = page == "console" ? Visibility.Visible : Visibility.Collapsed;
        MainScroll.Visibility = page == "console" ? Visibility.Collapsed : Visibility.Visible;
        _consoleFollow?.Dispose();
        _consoleFollow = null;

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
            ["sources"] = (TallowText.Get("ui.main.0009"), TallowText.Get("ui.main.0024")),
            ["developers"] = ("Для разработчиков", "")
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
            case "developers": BuildDeveloperSecurity(); break;
        }
        MainScroll.ScrollToTop();
        UiMotion.AnimatePage(ContentPanel);
    }

    private TextBlock Text(string value, double size = 13, bool heading = false, bool muted = false)
    {
        var label = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            FontSize = size,
            FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = muted ? Muted : Ink,
            LineHeight = size * 1.55
        };
        if (!heading) TextSelection.Allow(label);
        return label;
    }

    private Border Card(UIElement child)
    {
        return new Border
        {
            Background = Surface,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
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
        var accessTile = SummaryTile(TallowText.Get("ui.main.0029"),
            AccessProfiles.DisplayName(_settings.ActiveProfile),
            AccessProfiles.Description(_settings.ActiveProfile));
        var accessButton = new Button { Content = accessTile, Style = (Style)FindResource("FlatTile"), ToolTip = "Подробнее об уровне доступа" };
        accessButton.Click += (_, _) => ShowAccessInfo();
        tiles.Children.Add(accessButton);
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
        try { return CurrentTokenIsAdministrator(); }
        catch (SystemException) { return false; }
    }

    private static bool CurrentTokenIsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
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
        var copySummary = ActionButton("Скопировать сводку", () =>
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
        });
        copySummary.Margin = new Thickness(0, 12, 0, 0);
        deviceCard.Children.Add(copySummary);
        ContentPanel.Children.Add(Card(deviceCard));
        AddNotice(TallowText.Get("ui.main.0065"), TallowText.Get("ui.main.0066"));
    }

    private void BuildGuard()
    {
        AddIntro("Yetta / DAX Guard", "Менеджер уровней доступа",
            "Выбери профиль, чтобы посмотреть его возможности и условия активации.");

        BuildProfilePicker();

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
            if (!ConfirmDaxAction(TallowText.Get("ui.main.0082"),
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
                ShowDaxNotice(TallowText.Get("ui.main.0086"), ex.Message, WarningKind.Caution);
            }
        }));
        ttyActions.Children.Add(ActionButton(TallowText.Get("ui.main.0087"), () =>
        {
            if (!ConfirmDaxAction(TallowText.Get("ui.main.0088"),
                TallowText.Get("ui.main.0089")))
                return;
            try
            {
                _ = TtyKeyStore.Rotate();
                Notify(TallowText.Get("ui.main.0004"), TallowText.Get("ui.main.0090"));
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
            {
                ShowDaxNotice(TallowText.Get("ui.main.0086"), ex.Message, WarningKind.Caution);
            }
        }, true));
        ttyPanel.Children.Add(ttyActions);
        ContentPanel.Children.Add(Card(ttyPanel));
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
        _hasUnreadActivity = _page != "activity";
        UpdateActivityIcon();
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
            CornerRadius = new CornerRadius(8),
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
            BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
            Style = (Style)Application.Current.FindResource("DialogCloseButton")
        };
        UiMotion.AttachButtonFeedback(close);
        close.Click += (_, _) => ToastHost.Children.Remove(toast);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var icon = IconAssets.Asset("Navigation/ActivityUnread.png", 25);
        icon.Margin = new Thickness(0, 0, 10, 0);
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
        var rows = new StackPanel();
        ContentPanel.Children.Add(Text("Поиск по источнику и тексту", 12, muted: true));
        var filter = SearchField("Поиск в Центре активности");
        ContentPanel.Children.Add(filter);
        ContentPanel.Children.Add(rows);
        void Render()
        {
            var query = filter.Text.Trim();
            rows.Children.Clear();
            var filtered = _notifications.Where(note =>
                note.Source.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                note.Message.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .OrderByDescending(note => note.Time).Take(150).ToArray();
            if (filtered.Length == 0)
                rows.Children.Add(Text("Ничего не найдено.", 13, muted: true));
            foreach (var note in filtered)
            {
                var panel = new StackPanel();
                panel.Children.Add(Text(note.Source + "  ·  " + note.Time.ToString("dd.MM HH:mm"), 12, heading: true));
                panel.Children.Add(Text(note.Message, 12, muted: true));
                rows.Children.Add(Card(panel));
            }
        }
        filter.TextChanged += (_, _) => Render();
        Render();
    }

    private void BuildLogs()
    {
        AddIntro(TallowText.Get("ui.main.0105"), TallowText.Get("ui.main.0106"),
            TallowText.Get("ui.main.0107"));
        var rows = new StackPanel();
        BuildAuditSearch(rows);
        ContentPanel.Children.Add(rows);
    }

    private void BuildRecovery()
    {
        AddIntro("DAX", "Диагностика и восстановление",
            "Проверь связь с модулем и наличие файлов Yetta.");
        BuildDiagnostics();
    }

    private void BuildConsole()
    {
        ConsoleHost.Background = ConsoleSurface;
        var layout = new DockPanel();
        ConsoleHost.Child = layout;
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(toolbar, Dock.Top);
        layout.Children.Add(toolbar);
        BuildMarvineWorkspace(layout, toolbar);
        toolbar.Children.Add(Text("Marvine", 16, heading: true));
    }

    private TextBox BuildTerminalInput(string hint, Action execute)
    {
        var field = new TextBox
        {
            Background = Brushes.Transparent, Foreground = ConsoleInk, CaretBrush = ConsoleInk,
            BorderThickness = new Thickness(0), FontFamily = ConsoleFont, FontSize = 14,
            Padding = new Thickness(4, 0, 6, 0), VerticalContentAlignment = VerticalAlignment.Center,
            MaxLength = 4096, ToolTip = hint
        };
        System.Windows.Automation.AutomationProperties.SetName(field, hint);
        SpellCheck.SetIsEnabled(field, false);
        field.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None || field.IsReadOnly) return;
            e.Handled = true;
            execute();
        };
        return field;
    }

    private TextBlock ConsoleLine(string message, bool prompt)
    {
        var line = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 13,
            Foreground = ConsoleInk,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 9)
        };
        string[] parts = message.Split('\n');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) line.Inlines.Add(new LineBreak());
            string part = parts[i].TrimEnd('\r');
            Brush color = ConsoleInk;
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
                    _ => ConsoleInk
                };
            }
            if (_settings.MarvineColorset == "EGA" && Ink is SolidColorBrush ink && ink.Color.R < 120)
                color = part.StartsWith("[✖]") ? Hex("#AD243B") : part.StartsWith("[⚠]") ? Hex("#795400") :
                    part.StartsWith("[✔]") ? Hex("#006948") : ConsoleInk;
            if (_settings.MarvineColorset == "dracula")
            {
                color = part switch
                {
                    _ when prompt => Hex("#8BE9FD"),
                    _ when part.StartsWith("[✔]") || part.StartsWith("[Зелёный]") => Hex("#50FA7B"),
                    _ when part.StartsWith("[✖]") || part.StartsWith("[Красный]") => Hex("#FF5555"),
                    _ when part.StartsWith("[⚠]") || part.StartsWith("[Жёлтый]") => Hex("#F1FA8C"),
                    _ when part.StartsWith("[Серый]") => Hex("#6272A4"),
                    _ when part.StartsWith("[☄]") => Hex("#FF79C6"),
                    _ when part.StartsWith("[⌘]") => Hex("#BD93F9"),
                    _ => Hex("#F8F8F2")
                };
            }
            part = Regex.Replace(part, @"^\[(Зелёный|Красный|Жёлтый|Серый|Синий|Голубой|Белый)\]\s*", "");
            if (part.StartsWith('/') && !Dracula) color = ConsoleAccent;
            if (prompt || part.Contains("DAX TTY:", StringComparison.Ordinal))
                line.Inlines.Add(new Run(part) { Foreground = color });
            else
                AddFormattedConsoleText(line, part, color);
        }
        return line;
    }

    private void AddFormattedConsoleText(TextBlock line, string value, Brush color)
    {
        if (value.Length > 4096)
        {
            line.Inlines.Add(new Run(value) { Foreground = color });
            return;
        }
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

    private bool _marvineCommandRunning;

    private async void RunMarvine()
    {
        if (_consoleInput is not { } input || _marvineCommandRunning) return;
        string command = input.Text.Trim();
        if (command.Length == 0) return;
        input.Clear();
        RememberMarvineCommand(command);
        var response = ConsoleEngine.Execute(command,
            new ConsoleContext(_settings.Theme, _settings.ActiveProfile, ReleaseVersion,
                _settings.MarvineSubsyEnabled, _settings.MarvineColorset));
        if (response.ClearOutput)
        {
            _history.RemoveAll(entry => !entry.AwaitsReply);
            _activeSubsy.Clear();
            _subsyViews.Clear();
            if (_consoleOutput is { } display)
            {
                display.Reset();
                foreach (var entry in _history) AppendMarvineEntry(display, entry);
            }
            return;
        }

        AddConsoleEntry(new ConsoleEntry(command, ""));
        bool samePage = true;
        _marvineCommandRunning = true;
        input.IsReadOnly = true;
        try
        {
            string message = response.Message;
            if (response.Request is not null)
                message = await HandleMarvineRequestAsync(response.Request, response.Argument);
            if (message.Length > 0) AddConsoleEntry(new ConsoleEntry("", message));
            samePage = ReferenceEquals(input, _consoleInput);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or
            InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or
            InvalidOperationException or ArgumentException or TimeoutException or OperationCanceledException or
            System.Text.Json.JsonException or System.Xml.XmlException)
        {
            if (!_updateStop.IsCancellationRequested)
                AddConsoleEntry(new ConsoleEntry("", "[✖] Marvine: " + FriendlyMarvineError(ex)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            string code = $"{ex.GetType().FullName}/0x{ex.HResult:X8}";
            AddConsoleEntry(new ConsoleEntry("", "[✖] Неизвестная ошибка: " + code)
            {
                Render = () =>
                {
                    var row = new WrapPanel();
                    row.Children.Add(ConsoleLine("[✖] Неизвестная ошибка", false));
                    row.Children.Add(SubsyButton("Код ошибки", () =>
                    {
                        try { Clipboard.SetText(code); }
                        catch (System.Runtime.InteropServices.ExternalException) { Notify("Marvine", "Буфер обмена занят."); }
                    }));
                    return row;
                }
            });
        }
        finally
        {
            _marvineCommandRunning = false;
            if (_page == "console" && _consoleInput is { } activeInput)
                activeInput.IsReadOnly = false;
        }

        if (samePage && ReferenceEquals(input, _consoleInput) && response.NavigateTo is string page)
            Navigate(page);
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
        if (request.StartsWith("SNOB_", StringComparison.Ordinal)) return await RunSnobAsync(request, argument);
        switch (request)
        {
            case "MARVINE_OPEN":
                return OpenMarvineDirectory(argument!);
            case "HELP":
                return ShowMarvineHelp(argument ?? "");
            case "TEST_WARN":
                return ShowWarningTest(argument ?? "");
            case "SUBSY_DEMO":
                return await PreviewSubsyAsync();
            case "SUBSY_GALLERY":
                return await PreviewSubsyGalleryAsync();
            case "VERSION":
            {
                var run = Guid.NewGuid();
                bool visual = _settings.MarvineSubsyEnabled;
                if (visual) ShowScriptOutput(new(run, "Обновления", "@subsy busy Запрос релизов GitHub"));
                IReadOnlyList<UpdateRelease> releases;
                try { releases = await YettaUpdater.ListReleasesAsync(_updateStop.Token); }
                catch
                {
                    if (visual) EndSubsyActivity(run, "Обновления", "Не удалось получить список релизов", failed: true);
                    throw;
                }
                if (visual) EndSubsyActivity(run, "Обновления", "Список получен");
                string title = "Установлена " + ReleaseVersion + (releases.Count == 0
                    ? " · релизы не найдены" : " · последняя " + releases[0].Version);
                return PageMarvineResult(title, releases.Select(item => item.Version + " · ID " +
                    (item.Id == 0 ? item.Tag : item.Id.ToString())).ToArray());
            }
            case "UPDATE":
                return await RunMarvineUpdateAsync();
            case "DOWNLOAD":
            {
                UpdateRelease? release;
                if (long.TryParse(argument, out long id) && id > 0)
                    release = await YettaUpdater.FindReleaseByIdAsync(id, _updateStop.Token);
                else
                    release = YettaUpdater.FindReleaseByTag(argument);
                if (release is null && string.IsNullOrWhiteSpace(argument))
                    return "[⚠] Marvine: укажи ID или тег из команды /yetta version.";
                if (release is null) return "[✖] Marvine: релиз недоступен. Минимальная версия для установки — 0.3.3.";
                if (!ConfirmYettaAction("Установить Yetta " + release.Tag,
                    "Будет загружен и запущен установщик релиза " + release.Tag + "."))
                    return "[ℹ] Marvine: установка отменена.";
                if (!await _updateGate.WaitAsync(0)) return "[♨] Marvine: другое обновление уже выполняется.";
                var run = Guid.NewGuid();
                bool visual = _settings.MarvineSubsyEnabled;
                if (visual) ShowScriptOutput(new(run, "Обновления", "@subsy busy Подготовка " + release.Tag));
                int shown = -1;
                try
                {
                    await InstallReleaseAsync(release, consoleProgress: visual ? value =>
                    {
                        int percent = (int)Math.Round(value * 100);
                        if (percent <= shown) return;
                        shown = percent;
                        ShowScriptOutput(new(run, "Обновления", $"@subsy progress {percent} {release.Tag}"));
                    } : null);
                    if (visual) EndSubsyActivity(run, "Обновления", "Установщик проверен и открыт");
                }
                catch
                {
                    if (visual) EndSubsyActivity(run, "Обновления", "Загрузка или установка прервана", failed: true);
                    throw;
                }
                finally { _updateGate.Release(); }
                return "[♨] Marvine: установка запущена.";
            }
            case "TTY_INFO":
                return await ConfirmInlineAsync("Показать ключ TTY", "Ключ появится в истории текущей консоли. Продолжить?")
                    ? "[☄] DAX TTY: " + TtyKeyStore.Current()
                    : "[ℹ] Marvine: показ ключа отменён.";
            case "TTY_UPDATE":
                return await ConfirmInlineAsync("Обновить ключ TTY", "Старый ключ перестанет работать. Продолжить?")
                    ? "[✔] DAX TTY: ключ обновлён. Новый ключ: " + TtyKeyStore.Rotate()
                    : "[ℹ] Marvine: обновление ключа отменено.";
            case "SUBSY":
                _settings.MarvineSubsyEnabled = argument == "enable";
                if (!_settings.MarvineSubsyEnabled) _activeSubsy.Clear();
                SavePreferences();
                return "[✔] Marvine Subsy: " + (_settings.MarvineSubsyEnabled ? "включена." : "выключена.");
            case "COLORSET":
                _settings.MarvineColorset = argument is "EGA" or "No Color" or "dracula" ? argument : "EGA";
                SavePreferences();
                bool following = _consoleFollow?.Following ?? true;
                double offset = _consoleFollow?.Offset ?? 0;
                Navigate("console");
                if (!following) _consoleFollow?.PauseAt(offset);
                return "[✔] Marvine: палитра " + _settings.MarvineColorset + ".";
            case "SCRIPT_LIST":
            {
                var scripts = _scripts.List();
                return PageMarvineResult(scripts.Count == 0 ? "Python-скриптов пока нет" : "Python-скрипты",
                    scripts.Select(item => item.Id + " · " + item.Path +
                        " · " + (item.Running ? "запущен" : "завершён")).ToArray());
            }
            case "SCRIPT_RUN":
            {
                if (await AuthorizeOperationAsync(DaxOperation.RunScript) is { Allowed: false } denied)
                    return "[✖] " + denied.Reason;
                bool elevated;
                try { elevated = CurrentTokenIsAdministrator(); }
                catch (SystemException) { return "[✖] Windows не подтвердила права процесса. Запуск отменён."; }
                if (argument is null)
                {
                    var picker = new OpenFileDialog { Title = "Выбери скрипт Python", Filter = "Скрипты Python (*.py)|*.py", InitialDirectory = MarvineDirectory };
                    if (picker.ShowDialog(this) != true) return "[ℹ] Выбор скрипта отменён.";
                    argument = picker.FileName;
                }
                string path = ResolveMarvinePath(argument);
                if (!File.Exists(path) || !path.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                    return "[⚠] Нужен существующий файл .py. Введи script run, чтобы выбрать файл.";
                string? interpreter = ResolvePythonExecutable();
                if (interpreter is null) return "[ℹ] Выбор Python отменён.";
                string? id = await PromptInlineIdAsync();
                if (id is null) return "[ℹ] Запуск отменён.";
                string rights = elevated ? "Администратор Windows" : "текущий пользователь Windows";
                string workingDirectory = _marvineDirectory ?? Path.GetDirectoryName(path)!;
                if (!ConfirmYettaAction("Запустить Python-скрипт", $"Файл: {path}\nПапка: {workingDirectory}\nИнтерпретатор: {interpreter}\nПрава: {rights}."))
                    return "[ℹ] Запуск отменён.";
                var dax = await _daxGate.WaitForScriptAgentAsync(_updateStop.Token);
                var grant = await DaxAdministratorClient.RequestAsync(DaxAdministratorOperation.Status, _updateStop.Token);
                if (dax.State != DaxState.Ready || grant != DaxAdministratorState.Active)
                {
                    DaxAudit.Write("script", "denied", $"{dax.State}, {grant}");
                    return grant != DaxAdministratorState.Active
                        ? "[✖] DAX не подтвердил активный профиль Администратор. Проверь профиль в DAX Guard."
                        : "[✖] DAX: " + DaxStateLabel(dax.State) +
                          (dax.State == DaxState.Notice && !dax.DesktopPresent ? " · не найден файл Yetta." :
                           dax.State == DaxState.Notice && !dax.CatalogPresent ? " · не найден каталог локализации." :
                           ". Открой DAX Recovery для диагностики.");
                }
                if (elevated && await DaxAgentGate.RequestAsync("AUTHORIZE_SCRIPT", _updateStop.Token) != "AUTHORIZED")
                {
                    DaxAudit.Write("script", "denied", "Elevated script refused by DAX.");
                    return "[✖] DAX отклонил повышенный запуск. Открой Yetta обычным способом для запуска с правами пользователя.";
                }
                OpenScriptSession(id, path, interpreter, workingDirectory);
                DaxAudit.Write("script", "started", "Marvine script ID: " + id);
                return "[✔] Marvine: скрипт " + id + " запущен.";
            }
            case "SCRIPT_STOP":
                return _scripts.Stop(ResolveMarvinePath(argument ?? ""), argument?.Trim().Trim('"'))
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
            Margin = new Thickness(0, 0, 8, 0), Style = (Style)FindResource("SecondaryButton") };
        UiMotion.AttachButtonFeedback(cancel);
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var ok = new Button { Content = "Продолжить", MinWidth = 110, MinHeight = 34, IsDefault = true,
            Style = (Style)FindResource("PrimaryButton") };
        UiMotion.AttachButtonFeedback(ok);
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
        if (_settings.PythonExecutablePath is string configured && Path.IsPathFullyQualified(configured) && File.Exists(configured) &&
            Path.GetFileName(configured).ToLowerInvariant() is "python.exe" or "py.exe")
            return configured;
        string bundled = Path.Combine(AppContext.BaseDirectory, "Python", "python.exe");
        if (File.Exists(bundled)) return bundled;
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
        colorset.Items.Add("dracula");
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

    private async Task<string> CheckAndInstallUpdateAsync(bool manual, Action<double>? consoleProgress = null)
    {
        if (!await _updateGate.WaitAsync(0)) return "Другая проверка или загрузка уже выполняется";
        try
        {
            var latest = await YettaUpdater.FindUpdateAsync(CurrentVersion, _updateStop.Token);
            if (latest is null)
            {
                if (manual && consoleProgress is null)
                    ShowYettaNotice(TallowText.Get("ui.main.0175"), TallowText.Get("ui.main.0196"));
                return "Установлена последняя версия";
            }
            if (!manual && latest.Version == _dismissedAutoVersion) return "Обновление отложено";
            if (!manual && !_settings.AutoUpdatesEnabled) return "Автообновления выключены";
            if (manual && !ConsentDialog.Confirm(this,
                TallowText.Get("ui.main.0197") + latest.Version + "?",
                TallowText.Get("ui.main.0198") +
                TallowText.Get("ui.main.0199") +
                TallowText.Get("ui.main.0200") +
                TallowText.Get("ui.main.0201") +
                TallowText.Get("ui.main.0202"),
                YettaWarningLevel.Consent))
                return "Установка отменена";

            await InstallReleaseAsync(latest, automatic: !manual, consoleProgress: consoleProgress);
            return "Установщик подготовлен";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or
            InvalidDataException or InvalidOperationException or
            System.ComponentModel.Win32Exception or OperationCanceledException or
            System.Text.Json.JsonException or System.Xml.XmlException)
        {
            if (ex is OperationCanceledException && (_dismissedAutoVersion is not null || _updateStop.IsCancellationRequested))
                return "Загрузка отменена";
            string message = FriendlyMarvineError(ex);
            DaxAudit.Write("update", "check_failed", ex.GetType().Name);
            if (consoleProgress is not null) throw;
            if (manual)
                ShowYettaNotice(TallowText.Get("ui.main.0203"), message, WarningKind.Caution);
            else if (DateTimeOffset.UtcNow >= _nextAutoUpdateErrorNotice)
            {
                _nextAutoUpdateErrorNotice = DateTimeOffset.UtcNow.AddHours(6);
                Notify("Обновления Yetta", message);
            }
            return message;
        }
        finally { _downloadCancel = null; _updateGate.Release(); }
    }

    private async Task InstallReleaseAsync(UpdateRelease latest, bool automatic = false, Action<double>? consoleProgress = null, bool defaultDirectory = false)
    {
        using var downloadCancel = CancellationTokenSource.CreateLinkedTokenSource(_updateStop.Token);
        _downloadCancel = downloadCancel;
        var status = Text("Загрузка Yetta " + latest.Version + "…", 13, heading: true);
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 5, Margin = new Thickness(0, 12, 0, 10) };
        var cancel = ActionButton("Отменить загрузку", () => { _dismissedAutoVersion = latest.Version; downloadCancel.Cancel(); });
        var panel = new StackPanel { Children = { status, bar, cancel } };
        var card = new Border { Background = Surface, CornerRadius = new CornerRadius(10), Padding = new Thickness(18),
            HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 10), Child = panel };
        UpdateHost.Content = card;
        bool active = true;
        int lastPercent = -1;
        bool installing = false;

        var progress = new Progress<double>(value =>
        {
            if (!active || installing) return;
            int percent = (int)(value * 100);
            if (percent == lastPercent) return;
            lastPercent = percent;
            bar.Value = value;
            string stage = value >= 1 ? "Готово к установке" : value >= .99 ? "Проверка файла…" : "Скачивание…";
            status.Text = $"Yetta {latest.Version} · {stage} {percent}%";
            consoleProgress?.Invoke(value);
        });
        try
        {
            // Hashing and streaming never occupy the dispatcher, including cached downloads.
            string path = await Task.Run(() => YettaUpdater.DownloadInstallerAsync(latest, progress, downloadCancel.Token));
            if (automatic && !_settings.AutoUpdatesEnabled) return;
            installing = true;
            status.Text = "Запуск установщика…";
            bar.IsIndeterminate = true;
            cancel.IsEnabled = false;
            var log = Path.Combine(Path.GetDirectoryName(path)!, "YettaSetup-" + latest.Version + ".log");
            var start = new ProcessStartInfo(path) { UseShellExecute = true };
            foreach (string argument in new[] { "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS", "/LOG=" + log })
                start.ArgumentList.Add(argument);
            if (!defaultDirectory) start.ArgumentList.Add("/DIR=" + Path.GetDirectoryName(Environment.ProcessPath!)!);
            if (latest.Unified)
                start.ArgumentList.Add("/PAYLOAD=" + Path.Combine(Path.GetDirectoryName(path)!, latest.PayloadName));
            // Process.Start can synchronously wait for antivirus/installer extraction. Keep Yetta responsive until launch succeeds.
            using var installer = await Task.Run(() => Process.Start(start)) ?? throw new InvalidOperationException("Установщик не запустился.");
            status.Text = "Установка запущена. Yetta откроется после завершения.";
            try { await DaxAgentGate.RequestAsync("STOP_FOR_UPDATE", _updateStop.Token); }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
            { DaxAudit.Write("update", "dax_stop_unavailable", ex.GetType().Name); }
            // The visible installer owns installation progress and the one restart.
            Close();
        }
        finally
        {
            active = false;
            UpdateHost.Content = null;
            _downloadCancel = null;
        }
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
