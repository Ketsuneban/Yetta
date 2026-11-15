using System.Net.Http;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Yetta.Core;

namespace Yetta.Desktop;

public partial class MainWindow : Window
{
    private const string ReleaseVersion = "0.3.15-preview";
    private readonly SettingsStore _store = new();
    private readonly TallowNotificationStore _activityStore = new();
    private readonly AppSettings _settings;
    private readonly Dictionary<string, Button> _navigation;
    private readonly List<(string Command, string Reply)> _history = [];
    private readonly List<(DateTime Time, string Source, string Message)> _notifications = [];
    private string _page = "overview";
    private string _consoleMode = "Marvine";
    private StackPanel? _consoleOutput;
    private TextBox? _consoleInput;
    private EmbeddedShellSession? _embeddedShell;
    private DaxProcessMonitor? _processMonitor;
    private readonly DaxAgentGate _daxGate;

    private Brush Ink => Resource("TextPrimary");
    private Brush Muted => Resource("TextSecondary");
    private Brush Accent => Resource("AccentBrush");
    private Brush Line => Resource("OutlineBrush");
    private Brush Surface => Resource("CardBackground");

    public MainWindow()
    {
        InitializeComponent();
        _settings = _store.Load();
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
        Navigate("overview");
        _daxGate = new DaxAgentGate(this);
        Closed += (_, _) => { _daxGate.Dispose(); _processMonitor?.Dispose(); _processMonitor = null; _embeddedShell?.Dispose(); _embeddedShell = null; };
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
        Cursor = IconAssets.CursorFrom(17, 2, 2) ?? Cursors.Arrow;
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
        CaptionMaximize.Content = maximized ? "❐" : "□";
        CaptionMaximize.ToolTip = maximized ? TallowText.Get("ui.main.0010") : TallowText.Get("ui.main.0011");
    }

    private static Brush Resource(string key) => (Brush)Application.Current.Resources[key];
    private static void SetColor(string key, Color color) =>
        Application.Current.Resources[key] = new SolidColorBrush(color);
    private static void SetBrush(string key, Brush brush) =>
        Application.Current.Resources[key] = brush;

    private void ApplyTheme()
    {
        bool glass = _settings.Theme == "Glass";
        if (glass)
        {
            // Distinctive glass treatment throughout the application's Glass theme.
            // Ambient backdrop, layered translucent panels and softer specular borders.
            var backdrop = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            backdrop.GradientStops.Add(new GradientStop(Color.FromRgb(36, 45, 75), 0));
            backdrop.GradientStops.Add(new GradientStop(Color.FromRgb(17, 23, 43), 0.52));
            backdrop.GradientStops.Add(new GradientStop(Color.FromRgb(41, 28, 69), 1));
            SetBrush("WindowBackground", backdrop);
            SetBrush("SidebarBackground", new LinearGradientBrush(
                Color.FromArgb(230, 32, 44, 71), Color.FromArgb(239, 24, 27, 48), 110));
            var glassCard = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            glassCard.GradientStops.Add(new GradientStop(Color.FromArgb(182, 95, 119, 155), 0));
            glassCard.GradientStops.Add(new GradientStop(Color.FromArgb(140, 53, 63, 99), 0.46));
            glassCard.GradientStops.Add(new GradientStop(Color.FromArgb(120, 37, 46, 77), 1));
            SetBrush("CardBackground", glassCard);
            SetColor("HoverBackground", Color.FromRgb(57, 62, 91));
            SetColor("AccentBrush", Color.FromRgb(190, 173, 255));
            SetColor("AccentForeground", Color.FromRgb(27, 18, 49));
            SetColor("TextPrimary", Color.FromRgb(242, 243, 255));
            SetColor("TextSecondary", Color.FromRgb(174, 185, 207));
            SetColor("OutlineBrush", Colors.Transparent);
            SetColor("SelectedBrush", Color.FromRgb(57, 48, 88));
        }
        else
        {
            SetColor("WindowBackground", Color.FromRgb(8, 13, 21));
            SetColor("SidebarBackground", Color.FromRgb(16, 27, 39));
            SetBrush("CardBackground", new LinearGradientBrush(
                Color.FromRgb(26, 43, 55), Color.FromRgb(17, 29, 43), 120));
            SetColor("HoverBackground", Color.FromRgb(39, 59, 74));
            SetColor("AccentBrush", Color.FromRgb(120, 232, 208));
            SetColor("AccentForeground", Color.FromRgb(6, 34, 30));
            SetColor("TextPrimary", Color.FromRgb(234, 246, 245));
            SetColor("TextSecondary", Color.FromRgb(146, 168, 173));
            SetColor("OutlineBrush", Colors.Transparent);
            SetColor("SelectedBrush", Color.FromRgb(32, 69, 63));
        }
        GlassDialog.IsGlass = glass;
        ApplyBackdrop();
        ThemeIndicator.Text = glass ? "Liquid Glass" : "Windows 11 Fluent";
    }

    private void ApplyBackdrop()
    {
        bool enabled = WindowsBackdrop.Configure(this, _settings.Theme == "Glass");
        if (enabled)
        {
            // Opaque WPF content otherwise hides the native Acrylic backdrop.
            SetBrush("WindowBackground", new LinearGradientBrush(
                Color.FromArgb(125, 24, 32, 58),
                Color.FromArgb(162, 20, 25, 46), 135));
        }
    }

    private void SetTheme(string theme)
    {
        if (theme is not ("Fluent" or "Glass")) return;
        _settings.Theme = theme;
        ApplyTheme();
        SavePreferences();
        Navigate(_page);
        Notify(TallowText.Get("ui.main.0012"), TallowText.Get("ui.main.0013") + theme + ".");
    }

    private void SavePreferences()
    {
        try { _store.Save(_settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowYettaNotice(TallowText.Get("ui.main.0014"), TallowText.Get("ui.main.0015") + ex.Message, WarningKind.Caution);
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
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(22),
            Margin = new Thickness(0, 0, 0, 14),
            Child = child
        };
    }

    private Button ActionButton(string label, Action callback, bool primary = false, bool enabled = true)
    {
        var button = new Button
        {
            Content = label,
            Style = (Style)FindResource(primary ? "PrimaryButton" : "SecondaryButton"),
            Margin = new Thickness(0, 0, 10, 10),
            MinHeight = 44,
            MinWidth = Math.Max(142, label.Length * 8 + 28),
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
                "DAX · Defense & Access eXecutive\\n\\n" +
                TallowText.Get("ui.main.0036") +
                TallowText.Get("ui.main.0037") +
                TallowText.Get("ui.main.0038") +
                TallowText.Get("ui.main.0039") +
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
            (TallowText.Get("ui.main.0061"), _settings.Theme),
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
            if (choices.SelectedItem is ComboBoxItem { Tag: string profile } &&
                profile != _settings.ActiveProfile && AccessProfiles.Apply(_settings, profile))
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
                "Guest" => "GUEST", "SafeUser" => "sUSER",
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
        if (!AccessProfiles.CanInstallLogicalProfile(profile))
        {
            ShowYettaNotice(TallowText.Get("ui.main.0093"),
                TallowText.Get("ui.main.0094") + AccessProfiles.DisplayName(profile) +
                TallowText.Get("ui.main.0095"),
                WarningKind.Dax);
            return;
        }
        if (_settings.InstalledProfiles.Contains(profile))
            AccessProfiles.Apply(_settings, profile);
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
            Background = new SolidColorBrush(Color.FromRgb(12, 12, 12)),
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
            Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)),
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
                    ? new SolidColorBrush(Color.FromRgb(15, 15, 15))
                    : new SolidColorBrush(Color.FromRgb(39, 39, 39)),
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
            Background = new SolidColorBrush(Color.FromRgb(12, 12, 12)),
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
                    display.Children.Add(ConsoleLine("> " + cmd, true));
                    display.Children.Add(ConsoleLine(response, false));
                }
            }
            _consoleOutput = display;
            body.Children.Add(new ScrollViewer
            {
                Height = 298, Focusable = false, FocusVisualStyle = null,
                BorderThickness = new Thickness(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = display
            });
            _consoleInput = BuildTerminalInput(TallowText.Get("ui.main.0121"), RunMarvine);
            body.Children.Add(_consoleInput);
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
                Height = 298, Focusable = false, FocusVisualStyle = null,
                BorderThickness = new Thickness(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = display
            };
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
                            display.Children.Add(ConsoleLine(line, false));
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
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
            Foreground = Brushes.White,
            FocusVisualStyle = null,
            BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14, Padding = new Thickness(10, 10, 10, 10),
            CaretBrush = Brushes.White, ToolTip = hint
        };
        field.KeyDown += (_, e) =>
        {
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
        var line = Text(message, 12);
        line.FontFamily = new FontFamily("Consolas");
        line.FontSize = 14;
        line.Foreground = prompt ? Brushes.White : new SolidColorBrush(Color.FromRgb(217, 217, 217));
        line.Margin = new Thickness(0, 0, 0, 10);
        return line;
    }

    private void RunMarvine()
    {
        if (_consoleInput is null) return;
        string command = _consoleInput.Text.Trim();
        if (command.Length == 0) return;
        _consoleInput.Clear();
        var response = ConsoleEngine.Execute(command, new ConsoleContext(_settings.Theme, _settings.ActiveProfile));
        if (response.ClearOutput)
        {
            _history.Clear();
            _consoleOutput?.Children.Clear();
            return;
        }
        _history.Add((command, response.Message));
        if (_history.Count > 100) _history.RemoveAt(0);
        _consoleOutput?.Children.Add(ConsoleLine("> " + command, true));
        _consoleOutput?.Children.Add(ConsoleLine(response.Message, false));
        if (response.SwitchThemeTo is string theme) SetTheme(theme);
        if (response.NavigateTo is string page) Navigate(page);
    }

    private void BuildSettings()
    {
        AddIntro(TallowText.Get("ui.main.0155"), TallowText.Get("ui.main.0156"),
            TallowText.Get("ui.main.0157"));
        AddSection(TallowText.Get("ui.main.0158"));
        var modes = new WrapPanel();
        modes.Children.Add(ActionButton(TallowText.Get("ui.main.0159"),
            () => SetTheme("Fluent"), primary: _settings.Theme == "Fluent"));
        modes.Children.Add(ActionButton(TallowText.Get("ui.main.0160"),
            () => SetTheme("Glass"), primary: _settings.Theme == "Glass"));
        ContentPanel.Children.Add(modes);

        AddSection(TallowText.Get("ui.main.0161"));
        var sounds = new CheckBox
        {
            Content = TallowText.Get("ui.main.0162"),
            IsChecked = _settings.NotificationSoundsEnabled,
            Foreground = Ink,
            Margin = new Thickness(0, 3, 0, 3)
        };
        sounds.Checked += (_, _) => { _settings.NotificationSoundsEnabled = true; SavePreferences(); };
        sounds.Unchecked += (_, _) => { _settings.NotificationSoundsEnabled = false; SavePreferences(); };
        ContentPanel.Children.Add(Card(sounds));
        var retention = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 4), Foreground = Ink };
        foreach (int hours in new[] { 1, 6, 24, 72, 168, 720 })
        {
            var option = new ComboBoxItem
            {
                Content = hours switch
                {
                    1 => TallowText.Get("ui.main.0163"), 6 => TallowText.Get("ui.main.0164"), 24 => TallowText.Get("ui.main.0165"),
                    72 => TallowText.Get("ui.main.0166"), 168 => TallowText.Get("ui.main.0167"), _ => TallowText.Get("ui.main.0168")
                },
                Tag = hours
            };
            retention.Items.Add(option);
            if (hours == _settings.NotificationRetentionHours)
                retention.SelectedItem = option;
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
        ContentPanel.Children.Add(Card(new StackPanel
        {
            Children =
            {
                Text(TallowText.Get("ui.main.0169"), 12, muted: true),
                retention
            }
        }));


        AddSection(TallowText.Get("ui.main.0170"));
        var confirmation = new CheckBox
        {
            Content = TallowText.Get("ui.main.0171"),
            IsChecked = _settings.ConfirmExternalLinks,
            Foreground = Ink,
            Margin = new Thickness(0, 3, 0, 3)
        };
        confirmation.Checked += (_, _) => { _settings.ConfirmExternalLinks = true; SavePreferences(); };
        confirmation.Unchecked += (_, _) => { _settings.ConfirmExternalLinks = false; SavePreferences(); };
        ContentPanel.Children.Add(Card(confirmation));

        AddSection(TallowText.Get("ui.main.0153"));
        ContentPanel.Children.Add(Card(Text(
            _settings.EmbeddedShellConsentGranted
                ? TallowText.Get("ui.main.0172")
                : TallowText.Get("ui.main.0173"),
            12, muted: true)));
        ContentPanel.Children.Add(ActionButton(TallowText.Get("ui.main.0174"), () =>
        {
            _settings.EmbeddedShellConsentGranted = false;
            _processMonitor?.Dispose();
            _processMonitor = null;
            _embeddedShell?.Dispose();
            _embeddedShell = null;
            SavePreferences();
            Navigate("settings");
        }, enabled: _settings.EmbeddedShellConsentGranted));

        AddSection(TallowText.Get("ui.main.0175"), TallowText.Get("ui.main.0176"));
        ContentPanel.Children.Add(Card(Text(
            TallowText.Get("ui.main.0177") + ReleaseVersion +
            TallowText.Get("ui.main.0178") +
            TallowText.Get("ui.main.0179") +
            TallowText.Get("ui.main.0180"),
            12, muted: true)));
        ContentPanel.Children.Add(ActionButton(TallowText.Get("ui.main.0181"), async () =>
        {
            await CheckAndInstallUpdateAsync();
        }, true));

        AddSection(TallowText.Get("ui.main.0182"), TallowText.Get("ui.main.0183"));
        var demos = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
        foreach (WarningKind kind in Enum.GetValues<WarningKind>())
        {
            WarningKind choice = kind;
            var btn = ActionButton(TallowText.Get("ui.main.0184") + kind, () =>
            {
                _ = YettaWarningWindow.Show(this,
                    new WarningRequest(choice, TallowText.Get("ui.main.0185"),
                        TallowText.Get("ui.main.0186") +
                        TallowText.Get("ui.main.0187") +
                        TallowText.Get("ui.main.0188") +
                        TallowText.Get("ui.main.0189"),
                        Subject: "Yetta · UI Test",
                        RequireAcknowledgement: choice is WarningKind.Caution or WarningKind.Dangerous,
                        PotentialDamage: choice is WarningKind.Dangerous or WarningKind.Critical,
                        RecoveryNavigation: choice == WarningKind.Fatal
                            ? () => Navigate("recovery") : null),
                    _settings.ActiveProfile, nonModal: true);
            });
            demos.Children.Add(btn);
        }
        ContentPanel.Children.Add(demos);

        AddSection(TallowText.Get("ui.main.0190"));
        ContentPanel.Children.Add(Card(Text(
            TallowText.Get("ui.main.0191") + _store.FilePath +
            TallowText.Get("ui.main.0192"), 12, muted: true)));
        ContentPanel.Children.Add(ActionButton(TallowText.Get("ui.main.0193"), () =>
        {
            if (!ConfirmYettaAction(TallowText.Get("ui.main.0194"),
                TallowText.Get("ui.main.0195")))
                return;
            _settings.Theme = "Fluent";
            _settings.ConfirmExternalLinks = true;
            ApplyTheme();
            SavePreferences();
            Navigate("settings");
        }));
    }

    private async Task CheckAndInstallUpdateAsync()
    {
        try
        {
            var latest = await YettaUpdater.FindUpdateAsync(new Version(0, 3, 15));
            if (latest is null)
            {
                ShowYettaNotice(TallowText.Get("ui.main.0175"), TallowText.Get("ui.main.0196"));
                return;
            }
            if (!ConsentDialog.Confirm(this,
                TallowText.Get("ui.main.0197") + latest.Version + "?",
                TallowText.Get("ui.main.0198") +
                TallowText.Get("ui.main.0199") +
                TallowText.Get("ui.main.0200") +
                TallowText.Get("ui.main.0201") +
                TallowText.Get("ui.main.0202"),
                YettaWarningLevel.Consent))
                return;

            var path = await YettaUpdater.DownloadInstallerAsync(latest);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            Close();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or
            InvalidDataException or InvalidOperationException or
            System.ComponentModel.Win32Exception or TaskCanceledException)
        {
            ShowYettaNotice(TallowText.Get("ui.main.0203"), ex.Message, WarningKind.Caution);
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
