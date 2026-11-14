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
    private const string ReleaseVersion = "0.3.5-preview";
    private readonly SettingsStore _store = new();
    private readonly TtyKeyStore _ttyKeys = new();
    private readonly AppSettings _settings;
    private readonly Dictionary<string, Button> _navigation;
    private readonly List<(string Command, string Reply)> _history = [];
    private readonly List<(DateTime Time, string Source, string Message)> _notifications = [];
    private string _page = "overview";
    private string _consoleMode = "Marvine";
    private StackPanel? _consoleOutput;
    private TextBox? _consoleInput;
    private EmbeddedShellSession? _embeddedShell;

    private Brush Ink => Resource("TextPrimary");
    private Brush Muted => Resource("TextSecondary");
    private Brush Accent => Resource("AccentBrush");
    private Brush Line => Resource("OutlineBrush");
    private Brush Surface => Resource("CardBackground");

    public MainWindow()
    {
        InitializeComponent();
        _settings = _store.Load();
        _navigation = new()
        {
            ["overview"] = NavOverview,
            ["info"] = NavInfo,
            ["console"] = NavConsole,
            ["guard"] = NavGuard,
            ["process"] = NavProcess,
            ["logs"] = NavLogs,
            ["activity"] = NavActivity,
            ["recovery"] = NavRecovery,
            ["settings"] = NavSettings,
            ["sources"] = NavSources
        };
        ApplyTheme();
        InstallNavigationIcons();
        UpdateCaptionState();
        StateChanged += (_, _) => UpdateCaptionState();
        SourceInitialized += (_, _) => UpdateSystemBackdrop();
        Navigate("overview");
        Closed += (_, _) => { _embeddedShell?.Dispose(); _embeddedShell = null; };
    }

    private void InstallNavigationIcons()
    {
        foreach (var (page, button) in _navigation)
        {
            var raw = button.Content?.ToString() ?? page;
            int separator = raw.IndexOf("   ", StringComparison.Ordinal);
            var title = separator < 0 ? raw : raw[(separator + 3)..];
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = IconLibrary.Create(IconLibrary.NavigationIcon(page), 22);
            icon.Margin = new Thickness(0, 0, 13, 0);
            row.Children.Add(icon);
            row.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
            button.Content = row;
        }
        CaptionMinimize.Content = IconLibrary.Create(15, 18);
        CaptionClose.Content = IconLibrary.Create(16, 19);
    }

    private void OnWindowMinimize(object sender, RoutedEventArgs e) => WindowState = System.Windows.WindowState.Minimized;

    private void OnWindowMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == System.Windows.WindowState.Maximized ? System.Windows.WindowState.Normal : System.Windows.WindowState.Maximized;

    private void OnWindowClose(object sender, RoutedEventArgs e) => Close();

    private void UpdateCaptionState()
    {
        if (CaptionMaximize is null) return;
        bool maximized = WindowState == System.Windows.WindowState.Maximized;
        CaptionMaximize.Content = IconLibrary.Create(maximized ? 15 : 14, 18);
        CaptionMaximize.ToolTip = maximized ? "Восстановить" : "Развернуть";
    }

    private static Brush Resource(string key) => (Brush)Application.Current.Resources[key];
    private static void SetColor(string key, Color color) =>
        Application.Current.Resources[key] = new SolidColorBrush(color);
    private static void SetBrush(string key, Brush brush) =>
        Application.Current.Resources[key] = brush;

    private void UpdateSystemBackdrop()
    {
        if (_settings.Theme == "Glass") NativeBackdrop.TryApply(this, acrylic: false);
        else NativeBackdrop.Remove(this);
    }

    private void ApplyTheme()
    {
        bool glass = _settings.Theme == "Glass";
        if (glass)
        {
            // Distinctive glass treatment throughout the application's Glass theme.
            // Ambient backdrop, layered translucent panels and softer specular borders.
            var backdrop = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            backdrop.GradientStops.Add(new GradientStop(Color.FromArgb(208, 36, 45, 75), 0));
            backdrop.GradientStops.Add(new GradientStop(Color.FromArgb(215, 17, 23, 43), 0.52));
            backdrop.GradientStops.Add(new GradientStop(Color.FromArgb(220, 41, 28, 69), 1));
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
            SetColor("OutlineBrush", Color.FromArgb(158, 160, 177, 215));
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
            SetColor("OutlineBrush", Color.FromRgb(53, 76, 83));
            SetColor("SelectedBrush", Color.FromRgb(32, 69, 63));
        }
        ThemeIndicator.Text = glass ? "Liquid Glass" : "Windows 11";
        if (IsLoaded) UpdateSystemBackdrop();
    }

    private void SetTheme(string theme)
    {
        if (theme is not ("Fluent" or "Glass")) return;
        _settings.Theme = theme;
        ApplyTheme();
        SavePreferences();
        Navigate(_page);
        Notify("Yetta", "Оформление изменено: " + theme + ".");
    }

    private void SavePreferences()
    {
        try { _store.Save(_settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            YettaMessageBox.Show(this, "Не удалось сохранить настройки:\n" + ex.Message,
                "Yetta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page }) Navigate(page);
    }

    private void Navigate(string page)
    {
        if (!_navigation.ContainsKey(page)) return;
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
        }

        var headings = new Dictionary<string, (string Name, string Hint)>
        {
            ["overview"] = ("Главная", "Рабочее пространство Yetta"),
            ["info"] = ("Информация", "Приложение, права и устройство"),
            ["console"] = ("Консоль", "Marvine и встроенные консоли Windows"),
            ["guard"] = ("DAX Guard", "Уровни доступа и их установка"),
            ["process"] = ("DAX Process", "Проверка и запуск выбранных программ"),
            ["logs"] = ("DAX Logs", "Будущий журнал событий безопасности"),
            ["activity"] = ("Центр активности", "Локальные уведомления текущей сессии"),
            ["recovery"] = ("DAX Recovery", "Центр будущего восстановления"),
            ["settings"] = ("Настройки", "Предпочтения приложения"),
            ["sources"] = ("Источники", "Разработка, релизы, авторство")
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
            case "process": BuildProcess(); break;
            case "logs": BuildLogs(); break;
            case "activity": BuildActivity(); break;
            case "recovery": BuildRecovery(); break;
            case "settings": BuildSettings(); break;
            case "sources": BuildSources(); break;
        }
        MainScroll.ScrollToTop();
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
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
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
            Margin = new Thickness(0, 0, 10, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = enabled
        };
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
        AddIntro("Yetta / Desktop Preview", "Windows. На твоих условиях.",
            "Начальная версия единой платформы настройки Windows 11. Пользователь управляет изменениями, а критические функции будут добавляться только после внедрения и проверки DAX.");
        var tiles = new WrapPanel();
        tiles.Children.Add(SummaryTile("Версия", ReleaseVersion, null));
        tiles.Children.Add(SummaryTile("Уровень доступа",
            AccessProfiles.DisplayName(_settings.ActiveProfile),
            AccessProfiles.Description(_settings.ActiveProfile)));
        var daxTile = SummaryTile("DAX", "В разработке",
            "Нажмите для информации о DAX.");
        daxTile.Cursor = Cursors.Hand;
        daxTile.MouseLeftButtonUp += (_, _) => ShowDaxInfo();
        tiles.Children.Add(daxTile);
        ContentPanel.Children.Add(tiles);
        AddNotice("Заглушка", "Этот блок зарезервирован для будущих сведений Yetta. Новые действия здесь не выполняются.");
    }

    private void ShowDaxInfo()
    {
        DaxWarningDialog.Show(this, new DaxWarningRequest(
            YettaWarningLevel.Dax,
            "DAX · Defense & Access eXecutive",
            "Исполнительный модуль защиты и доступа.\\n\\n" +
            "Текущее состояние: проектная спецификация, DAX runtime не установлен.\\n\\n" +
            "Назначение: контроль операций, инициированных из Yetta; " +
            "проверка политик, предупреждения, аудит, запуск программ и восстановление.\\n\\n" +
            "Архитектура: DAX Guard / Gate, DAX Process, DAX Threat, " +
            "DAX System, DAX Audit, DAX Update.\\n\\n" +
            "Пока отсутствует защищённая системная служба. " +
            "Получение SYSTEM и автоматический откат ещё не реализованы.",
            SubjectName: "DAX",
            SubjectIconId: 29),
            activeProfile: _settings.ActiveProfile,
            safeMode: ConsentDialog.InWindowsSafeMode,
            modeless: true);
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
        AddIntro("Yetta / Device Information", "Состояние приложения и Windows",
            "Техническая информация показывается локально. Логический профиль Yetta не заменяет разрешения Windows и никогда не повышает их автоматически.");

        var status = new StackPanel();
        status.Children.Add(Text("YETTA " + ReleaseVersion, 21, heading: true));
        status.Children.Add(Text("Активный уровень: " + AccessProfiles.DisplayName(_settings.ActiveProfile), 14));
        status.Children.Add(Text(AccessProfiles.Description(_settings.ActiveProfile), 12, muted: true));
        status.Children.Add(Text("Установлено профилей: " +
            string.Join(", ", _settings.InstalledProfiles.Select(AccessProfiles.DisplayName)), 12, muted: true));
        ContentPanel.Children.Add(Card(status));

        AddSection("Фактические полномочия процесса");
        var capabilities = new StackPanel();
        capabilities.Children.Add(Text(IsProcessAdministrator()
            ? "Токен Windows: повышенный административный"
            : "Токен Windows: не повышен", 13, heading: true));
        capabilities.Children.Add(Text(
            "Yetta выполняется в рамках токена текущего пользователя. Нет установленной службы SYSTEM, драйвера, защищённого брокера, специальных разрешений на системные файлы или полномочий изменять защиту Windows.", 12, muted: true));
        capabilities.Children.Add(Text(
            "Чтение файлов возможно только там, где Windows разрешает это текущему процессу. Запись в системные папки, управление службами и изменения реестра Yetta не выполняет. Marvine допускает только внутренние команды. Windows Terminal доступен через DAX Guard лишь при активном профиле «Пользователь».", 12, muted: true));
        ContentPanel.Children.Add(Card(capabilities));

        AddSection("Информация об устройстве");
        var specs = new (string Label, string Value)[]
        {
            ("Операционная система", SystemSnapshot.Os),
            ("Архитектура ОС", SystemSnapshot.Architecture),
            (".NET", SystemSnapshot.Runtime),
            ("Логические процессоры", SystemSnapshot.LogicalProcessors.ToString()),
            ("Пользователь", SystemSnapshot.User),
            ("Устройство", SystemSnapshot.Machine),
            ("Время работы ОС", SystemSnapshot.Uptime.ToString(@"d\.hh\:mm\:ss")),
            ("Режим оформления", _settings.Theme),
            ("Путь настроек", _store.FilePath)
        };
        var deviceCard = new StackPanel();
        foreach (var (label, value) in specs)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            row.Children.Add(Text(label, 10, heading: true, muted: true));
            row.Children.Add(Text(value, 13));
            deviceCard.Children.Add(row);
        }
        deviceCard.Children.Add(Text("Папка данных Yetta: " +
            (Path.GetDirectoryName(_store.FilePath) ?? "Недоступно"), 12, muted: true));
        ContentPanel.Children.Add(Card(deviceCard));
        AddNotice("Прозрачность", "Yetta не собирает и не отправляет на сервер сведения об устройстве. Эта страница — только локальный просмотр.");
    }

    private void BuildGuard()
    {
        AddIntro("Yetta / DAX Guard", "Менеджер уровней доступа",
            "В этой версии DAX Guard проверяет только логические политики Yetta. Установка Safe User и User не меняет токен Windows. Администратор и Система пока не устанавливаются.");

        var current = new StackPanel();
        current.Children.Add(Text("ТЕКУЩИЙ УРОВЕНЬ", 10, heading: true, muted: true));
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

        AddSection("Профили", "Сначала «Установить», затем «Применить». Активный профиль отображается в выпадающем списке.");
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
                applied ? "Применено" : installed ? "Применить" : "Установить",
                () => InstallOrApplyProfile(profile), applied == false && installed, enabled: !applied);
            button.VerticalAlignment = VerticalAlignment.Center;
            button.Margin = new Thickness(13, 0, 0, 0);
            Grid.SetColumn(button, 1);
            row.Children.Add(button);
            ContentPanel.Children.Add(Card(row));
        }
        AddSection("TTY-Ключ", "64 символа, защищённое хранилище Windows DPAPI (текущая учётная запись).");
        var ttyInfo = new StackPanel();
        ttyInfo.Children.Add(Text(_ttyKeys.IsConfigured
            ? "Ключ установлен ••••••••••••••••"
            : "TTY-ключ ещё не выпущен", 14, heading: true));
        ttyInfo.Children.Add(Text(
            "Ключ нужен для дополнительного подтверждения критических операций. " +
            "Он не предоставляет права Windows и не заменяет UAC. " +
            "Не сохраняйте скопированный ключ в незащищённом файле; буфер обмена доступен другим программам.", 12, muted: true));
        ContentPanel.Children.Add(Card(ttyInfo));
        var ttyActions = new WrapPanel();
        ttyActions.Children.Add(ActionButton("Скопировать TTY-Ключ", () =>
        {
            if (!ConsentDialog.Confirm(this,
                "Скопировать защищённый TTY-Ключ?",
                "Ключ будет временно помещён в буфер обмена Windows. " +
                "Другие приложения могут читать его. Не публикуйте ключ и не сохраняйте его в открытом виде.",
                YettaWarningLevel.Consent)) return;
            try
            {
                Clipboard.SetText(_ttyKeys.GetOrCreate());
                Notify("DAX Guard", "TTY-Ключ скопирован в буфер обмена.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or System.Security.Cryptography.CryptographicException or InvalidDataException)
            {
                YettaMessageBox.Show(this, "Не удалось получить TTY-Ключ: " + ex.Message,
                    "DAX Guard", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }));
        ttyActions.Children.Add(ActionButton(_ttyKeys.IsConfigured ? "Перевыпустить ключ" : "Создать TTY-Ключ", () =>
        {
            if (!ConsentDialog.Confirm(this,
                "Выдать новый TTY-Ключ?",
                "Новый 64-символьный ключ заменит предыдущий. " +
                "Старый ключ перестанет подходить для подтверждений. " +
                "Ключ хранится только в зашифрованном виде DPAPI.",
                YettaWarningLevel.Consent)) return;
            try
            {
                _ttyKeys.IssueNew();
                Notify("DAX Guard", "Новый TTY-Ключ выдан. Предыдущий больше не подходит.");
                Navigate("guard");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or System.Security.Cryptography.CryptographicException)
            {
                YettaMessageBox.Show(this, "Не удалось перевыпустить ключ: " + ex.Message,
                    "DAX Guard", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }));
        ContentPanel.Children.Add(ttyActions);
        AddNotice("Ограничение уровня SYSTEM",
            "Выбор в приложении не способен предоставить SYSTEM-права. Для уровней Администратор и Система потребуются отдельная служба, UAC/TTY, независимый аудит и проверка доступа.");
    }

    private void InstallOrApplyProfile(string profile)
    {
        if (!AccessProfiles.CanInstallLogicalProfile(profile))
        {
            YettaMessageBox.Show(this,
                "Профиль «" + AccessProfiles.DisplayName(profile) +
                "» пока недоступен. Нельзя считать его установленным без настоящего механизма повышения прав и проверки безопасности.",
                "DAX Guard", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_settings.InstalledProfiles.Contains(profile))
            AccessProfiles.Apply(_settings, profile);
        else
            AccessProfiles.Install(_settings, profile);
        SavePreferences();
        Notify("DAX Guard", "Профиль " + AccessProfiles.DisplayName(profile) + " изменён. Это внутренний уровень Yetta, а не полномочия Windows.");
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
        _notifications.Add((DateTime.Now, source, message));
        if (_notifications.Count > 100) _notifications.RemoveAt(0);

        var toast = new Border
        {
            Width = 314,
            Background = Surface,
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
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
        body.Children.Add(ActionButton("Подробнее", () =>
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
        AddIntro("Yetta / Activity Center", "Центр активности",
            "Локальные события текущего запуска Yetta. Это не журнал безопасности DAX и не мониторинг Windows.");
        if (_notifications.Count == 0)
        {
            AddNotice("Пока нет событий", "После изменений оформления или уровня Yetta события появятся здесь.");
            return;
        }
        foreach (var note in _notifications.AsEnumerable().Reverse())
        {
            var panel = new StackPanel();
            panel.Children.Add(Text(note.Source + " · " + note.Time.ToString("HH:mm:ss"), 13, heading: true));
            panel.Children.Add(Text(note.Message, 12, muted: true));
            ContentPanel.Children.Add(Card(panel));
        }
    }

    private void BuildProcess()
    {
        AddIntro("Yetta / DAX Process", "Проверка запускаемых программ",
            "Выберите EXE, чтобы проверить подпись и запросить локальное сканирование Defender. " +
            "Только процессы, запущенные этой кнопкой, и их потомки отслеживаются DAX. " +
            "Система не просматривает другие программы Windows.");

        AddSection("Реальные ограничения");
        AddNotice("User · стандартный токен Windows",
            "Запуск доступен только в логическом профиле USER и если сама Yetta не запущена " +
            "с правами администратора. DAX не повышает привилегии, не подменяет Defender " +
            "и не доказывает полную безопасность исполняемых файлов.");

        ContentPanel.Children.Add(ActionButton("Выбрать и проверить EXE", async () =>
        {
            await LaunchSelectedExecutableAsync();
        }, true, enabled: _settings.ActiveProfile == "User" &&
            !IsProcessAdministrator()));
        AddSection("Наблюдение");
        ContentPanel.Children.Add(Card(Text(
            "После запуска DAX наблюдает за корневым PID и известными дочерними процессами " +
            "через WMI с периодическим опросом. Короткоживущие процессы могут быть пропущены; " +
            "никаких глобальных драйверов или скрытых полномочий не устанавливается.", 12, muted: true)));
    }

    private async Task LaunchSelectedExecutableAsync()
    {
        if (_settings.ActiveProfile != "User" || IsProcessAdministrator())
        {
            DaxWarningDialog.Show(this, new DaxWarningRequest(
                YettaWarningLevel.Dax,
                "Запуск отклонён",
                "Необходим логический профиль USER без повышенных прав самого приложения.",
                SubjectName: "DAX Guard", AccessDenied: true),
                activeProfile: _settings.ActiveProfile,
                safeMode: ConsentDialog.InWindowsSafeMode);
            return;
        }
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите программу для проверки DAX",
            Filter = "Windows executable (*.exe)|*.exe",
            CheckFileExists = true, Multiselect = false
        };
        if (picker.ShowDialog(this) != true) return;
        string file = picker.FileName;
        DaxFileAssessment analysis;
        try
        {
            Notify("DAX Process", "Выполняется проверка выбранного файла…");
            analysis = await DaxFileScanner.AssessAsync(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidDataException or System.Security.Cryptography.CryptographicException)
        {
            YettaMessageBox.Show(this, "Не удалось проверить файл: " + ex.Message,
                "DAX Process", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        string subject = Path.GetFileName(file);
        string publisher = "";
        try { publisher = FileVersionInfo.GetVersionInfo(file).CompanyName ?? ""; }
        catch (System.ComponentModel.Win32Exception) { }
        string details = "Подпись Authenticode: " + analysis.SignatureStatus +
            "\\nSHA-256: " + analysis.Sha256 + "\\n" + analysis.DefenderStatus;
        if (analysis.ScanFailedOrThreat)
        {
            DaxWarningDialog.Show(this, new DaxWarningRequest(
                YettaWarningLevel.Dangerous,
                "DAX заблокировал запуск",
                details + "\\n\\nПричина результата Defender неоднозначна: это может быть угроза " +
                "или ошибка сканирования. Без подтверждённого защищённого брокера " +
                "продолжение не разрешено.",
                SubjectName: subject, TargetPath: file,
                Publisher: publisher, SubjectType: WarningSubjectType.Executable,
                DaxDenied: true, PotentialHarm: true,
                AcknowledgmentOnly: true),
                activeProfile: _settings.ActiveProfile,
                safeMode: ConsentDialog.InWindowsSafeMode);
            DaxAuditLog.Append(YettaWarningLevel.Dangerous,
                subject, "Defender blocked/failed", "blocked");
            return;
        }
        bool uncertain = analysis.DefenderUnavailable ||
            analysis.SignatureStatus != "Valid";
        var accepted = DaxWarningDialog.Show(this, new DaxWarningRequest(
            uncertain ? YettaWarningLevel.Caution : YettaWarningLevel.Standard,
            "Разрешить запуск выбранной программы?",
            details + "\\n\\nПроверка не гарантирует отсутствия угроз. " +
            "После запуска DAX сможет наблюдать только за выбранным деревом процессов.",
            SubjectName: subject, TargetPath: file, Publisher: publisher,
            SubjectType: WarningSubjectType.Executable,
            CheckRequired: uncertain, PotentialHarm: uncertain),
            ttyStore: _ttyKeys,
            activeProfile: _settings.ActiveProfile,
            safeMode: ConsentDialog.InWindowsSafeMode);
        if (accepted != DaxWarningResult.Allowed) return;
        try
        {
            // Recheck identity to reduce time-of-check/time-of-use races.
            byte[] fresh;
            using (var stream = File.OpenRead(file))
                fresh = await System.Security.Cryptography.SHA256.HashDataAsync(stream);
            if (!string.Equals(Convert.ToHexStringLower(fresh),
                analysis.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("Файл изменился после сканирования.");

            var process = Process.Start(new ProcessStartInfo(file)
            {
                // Do not use ShellExecute: elevation manifests cannot silently
                // escape the USER-only DAX launch path, and Windows returns a
                // directly supervised process PID.
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = Path.GetDirectoryName(file) ?? ""
            });
            if (process is null)
                throw new InvalidOperationException("Windows не вернула идентификатор процесса.");
            int pid = process.Id;
            DaxProcessTreeMonitor.Monitor(process, line =>
                Dispatcher.BeginInvoke(new Action(() => Notify("DAX Process", line))));
            Notify("DAX Process", "Запущен " + subject + " (PID " + pid + ").");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidDataException or System.ComponentModel.Win32Exception or
            InvalidOperationException)
        {
            YettaMessageBox.Show(this, "Запуск не выполнен: " + ex.Message,
                "DAX Process", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BuildLogs()
    {
        AddIntro("Yetta / DAX Logs", "Журнал решений DAX",
            "Локальные записи предупреждений и решений, сделанных внутри Yetta. " +
            "Это не глобальный журнал процессов и не утверждение о полной антивирусной защите.");
        AddNotice("Файл журнала", DaxAuditLog.FilePath);
        var events = DaxAuditLog.Recent();
        if (events.Count == 0)
        {
            AddNotice("Записей пока нет", "Реальные подтверждения и предупреждения Yetta будут отображены здесь.");
            return;
        }
        foreach (var e in events)
        {
            var panel = new StackPanel();
            panel.Children.Add(Text(e.Time.ToString("dd.MM.yyyy HH:mm:ss") +
                "  ·  " + e.Category + "  ·  " + e.Result, 12, heading: true));
            panel.Children.Add(Text(e.Subject + ": " + e.Detail, 12, muted: true));
            ContentPanel.Children.Add(Card(panel));
        }
    }

    private void BuildRecovery()
    {
        AddIntro("Yetta / DAX Recovery", "Восстановление Windows и DAX",
            "В будущем здесь будут точки восстановления перед критическими системными действиями, а также проверка целостности и восстановление служб DAX.");
        AddNotice("Заглушка — восстановление отключено",
            "Yetta не создаёт точки восстановления и не изменяет конфигурацию Windows. Функциональность требует настоящих Windows API, проверки доступности защиты системы, резервного плана и независимого тестирования.");
    }

    private void BuildConsole()
    {
        AddIntro("Yetta / Console", "Встроенная консоль",
            "Marvine — команды Yetta. Вкладки Windows Terminal и Windows PowerShell используют встроенную текстовую панель, которая передаёт введённые команды настоящему процессу Windows PowerShell текущего пользователя.");
        var tabs = new WrapPanel();
        foreach (string mode in new[] { "Marvine", "Windows Terminal", "Windows PowerShell" })
        {
            string selected = mode;
            tabs.Children.Add(ActionButton(mode, () =>
            {
                _consoleMode = selected;
                Navigate("console");
            }, _consoleMode == mode));
        }
        ContentPanel.Children.Add(tabs);
        var terminal = new Border
        {
            BorderBrush = Line, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(_consoleMode == "Windows PowerShell" ? 5 : 14),
            Background = _consoleMode switch
            {
                "Windows PowerShell" => new SolidColorBrush(Color.FromRgb(1, 36, 86)),
                "Windows Terminal" => new SolidColorBrush(Color.FromRgb(15, 15, 18)),
                _ => Resource("SidebarBackground")
            },
            Margin = new Thickness(0, 12, 0, 14),
            Padding = new Thickness(17),
            MinHeight = 290
        };
        var body = new StackPanel();
        var heading = Text(_consoleMode + "  ·  " + (_consoleMode == "Marvine" ? "Yetta" : "PowerShell engine"), 14, heading: true);
        heading.Foreground = _consoleMode == "Windows PowerShell" ? Brushes.White : Accent;
        heading.Margin = new Thickness(0, 0, 0, 12);
        body.Children.Add(heading);

        if (_consoleMode == "Marvine")
        {
            var display = new StackPanel();
            if (_history.Count == 0)
            {
                display.Children.Add(ConsoleLine("Marvine / Yetta " + ReleaseVersion, true));
                display.Children.Add(ConsoleLine("Введите help. Системные команды здесь не исполняются.", false));
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
                Height = 238, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = display
            });
            _consoleInput = BuildTerminalInput("Команда Marvine", RunMarvine);
            body.Children.Add(_consoleInput);
            body.Children.Add(ActionButton("Выполнить ↵", RunMarvine, true));
        }
        else if (!AccessProfiles.CanLaunchUnprivilegedTerminal(_settings))
        {
            var note = Text(
                "Для использования настоящего PowerShell установите и примените профиль USER в DAX Guard. " +
                "Этот профиль не повышает полномочия Windows.", 13);
            note.Foreground = Brushes.White;
            body.Children.Add(note);
            body.Children.Add(ActionButton("Перейти в DAX Guard", () => Navigate("guard")));
        }
        else if (IsProcessAdministrator())
        {
            var note = Text(
                "Yetta запущена с повышенными правами Windows. Чтобы встроенная оболочка не наследовала " +
                "административный токен, закройте Yetta и запустите её обычным способом, без повышения.", 13);
            note.Foreground = Brushes.White;
            body.Children.Add(note);
        }
        else if (!_settings.EmbeddedShellConsentGranted)
        {
            var note = Text(
                "Команды могут изменять или удалять файлы, к которым у вашей учётной записи есть доступ. " +
                "Нужно явное подтверждение. Предоставить Windows-права или обойти UAC это не может.", 13);
            note.Foreground = Brushes.White;
            note.Margin = new Thickness(0, 0, 0, 15);
            body.Children.Add(note);
            body.Children.Add(ActionButton("Прочитать и принять условия", () =>
            {
                if (ConsentDialog.Confirm(this,
                    "Разрешить выполнение собственных команд в PowerShell?",
                    "Yetta откроет настоящий powershell.exe в текстовой панели. Любая введённая команда " +
                    "будет выполняться с действующими разрешениями текущего пользователя Windows. " +
                    "Команды могут изменять личные файлы и настройки. " +
                    "Используйте эту функцию только осознанно. Отозвать согласие можно в Настройках.",
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
                "Реальный Windows PowerShell внутри интерфейса Yetta. " +
                "Обработка текстовых команд доступна; полноэкранные TTY-программы, цветовые escape-коды " +
                "и нативный интерфейс Windows Terminal пока не поддерживаются.", 11);
            description.Foreground = Brushes.LightGray;
            description.Margin = new Thickness(0, 0, 0, 10);
            body.Children.Add(description);

            var display = new StackPanel();
            _consoleOutput = display;
            var scrolling = new ScrollViewer
            {
                Height = 238, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = display
            };
            body.Children.Add(scrolling);
            display.Children.Add(ConsoleLine("Windows PowerShell · текущий пользователь · без UAC / SYSTEM", true));
            _consoleInput = BuildTerminalInput("Введите команду PowerShell", RunPowerShell);
            body.Children.Add(_consoleInput);
            var controls = new WrapPanel();
            controls.Children.Add(ActionButton("Выполнить ↵", RunPowerShell, true));
            controls.Children.Add(ActionButton("Остановить сессию", () =>
            {
                _embeddedShell?.Dispose();
                _embeddedShell = null;
                display.Children.Add(ConsoleLine("Сессия завершена. Переключите вкладку, чтобы начать новую.", false));
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
            }
            catch (Exception ex) when (ex is InvalidOperationException or
                System.ComponentModel.Win32Exception or IOException)
            {
                display.Children.Add(ConsoleLine("Не удалось запустить PowerShell: " + ex.Message, false));
            }
        }

        terminal.Child = body;
        ContentPanel.Children.Add(terminal);
        AddNotice("Реальные права Windows",
            "Запуск команд разрешён только на профиле USER и только без повышения самого приложения. " +
            "Принятое согласие не предоставляет ADMIN или SYS и не отключает защиту Windows. " +
            "Новые привилегированные функции потребуют отдельного подтверждения и аудита.");
        _consoleInput?.Focus();
    }

    private TextBox BuildTerminalInput(string hint, Action execute)
    {
        var field = new TextBox
        {
            Background = Surface, Foreground = Ink,
            BorderBrush = Line, BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("pack://application:,,,/Assets/#Inter"),
            FontSize = 13, Padding = new Thickness(12, 10, 12, 10),
            CaretBrush = Accent, ToolTip = hint
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
        _consoleInput.Clear();
        _consoleOutput?.Children.Add(ConsoleLine("> " + command, true));
        try { await _embeddedShell.SendAsync(command); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or
            ObjectDisposedException)
        {
            _consoleOutput?.Children.Add(ConsoleLine("Ошибка: " + ex.Message, false));
        }
    }

    private TextBlock ConsoleLine(string message, bool prompt)
    {
        var line = Text(message, 12);
        line.FontFamily = new FontFamily("pack://application:,,,/Assets/#Inter");
        line.Foreground = prompt ? Accent : Ink;
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
        AddIntro("Yetta / Settings", "Настройки приложения",
            "Здесь меняются только параметры самой Yetta. Настройки Windows не затрагиваются.");
        AddSection("Оформление");
        var modes = new WrapPanel();
        modes.Children.Add(ActionButton("Windows 11 / Fluent",
            () => SetTheme("Fluent"), primary: _settings.Theme == "Fluent"));
        modes.Children.Add(ActionButton("Glass Preview",
            () => SetTheme("Glass"), primary: _settings.Theme == "Glass"));
        ContentPanel.Children.Add(modes);

        AddSection("Уведомления");
        var sounds = new CheckBox
        {
            Content = "Воспроизводить звук уведомлений",
            IsChecked = _settings.NotificationSoundsEnabled,
            Foreground = Ink,
            Margin = new Thickness(0, 3, 0, 3)
        };
        sounds.Checked += (_, _) => { _settings.NotificationSoundsEnabled = true; SavePreferences(); };
        sounds.Unchecked += (_, _) => { _settings.NotificationSoundsEnabled = false; SavePreferences(); };
        ContentPanel.Children.Add(Card(sounds));

        AddSection("Подтверждения");
        var confirmation = new CheckBox
        {
            Content = "Спрашивать перед открытием внешних приложений и ссылок",
            IsChecked = _settings.ConfirmExternalLinks,
            Foreground = Ink,
            Margin = new Thickness(0, 3, 0, 3)
        };
        confirmation.Checked += (_, _) => { _settings.ConfirmExternalLinks = true; SavePreferences(); };
        confirmation.Unchecked += (_, _) => { _settings.ConfirmExternalLinks = false; SavePreferences(); };
        ContentPanel.Children.Add(Card(confirmation));

        AddSection("Встроенная PowerShell");
        ContentPanel.Children.Add(Card(Text(
            _settings.EmbeddedShellConsentGranted
                ? "Согласие на пользовательскую PowerShell принято. Его можно отозвать."
                : "Согласие ещё не предоставлено. При первом входе в консоль появится серое предупреждение.",
            12, muted: true)));
        ContentPanel.Children.Add(ActionButton("Отозвать согласие PowerShell", () =>
        {
            _settings.EmbeddedShellConsentGranted = false;
            _embeddedShell?.Dispose();
            _embeddedShell = null;
            SavePreferences();
            Navigate("settings");
        }, enabled: _settings.EmbeddedShellConsentGranted));

        AddSection("Предпросмотр предупреждений", "Проверка семи типов окон без выполнения системных операций.");
        var previews = new WrapPanel();
        foreach (var (level, label) in new[]
        {
            (YettaWarningLevel.Standard, "Стандартное"),
            (YettaWarningLevel.Caution, "Предостережение"),
            (YettaWarningLevel.Dangerous, "Опасное"),
            (YettaWarningLevel.Critical, "Критическое"),
            (YettaWarningLevel.Fatal, "Смертельное"),
            (YettaWarningLevel.Consent, "Соглашение"),
            (YettaWarningLevel.Dax, "DAX")
        })
        {
            var kind = level;
            var name = label;
            previews.Children.Add(ActionButton(label, () =>
            {
                bool fatal = kind == YettaWarningLevel.Fatal;
                bool consent = kind == YettaWarningLevel.Consent;
                DaxWarningDialog.Show(this, new DaxWarningRequest(
                    kind, "Демонстрация · " + name,
                    "Это только предварительный просмотр дизайна окна Yetta. " +
                    "Никаких угроз, повреждений Windows и операций восстановления не произошло. " +
                    "Текст внутри этой зоны прокручивается отдельно; кнопки снизу закреплены.",
                    SubjectName: "Демонстрация интерфейса",
                    SubjectIconId: fatal ? 48 : consent ? 2 : 46,
                    CheckRequired: consent,
                    KeyRequired: kind == YettaWarningLevel.Critical),
                    _ttyKeys,
                    onRecovery: () => Navigate("recovery"),
                    activeProfile: _settings.ActiveProfile,
                    safeMode: ConsentDialog.InWindowsSafeMode,
                    modeless: true);
            }));
        }
        ContentPanel.Children.Add(previews);
        ContentPanel.Children.Add(Card(Text(
            "Предпросмотр не свидетельствует о наличии угроз. В реальной операции " +
            "DAX классифицирует риск по результатам проверок. Для критического " +
            "подтверждения требуется существующий TTY-ключ из DAX Guard.", 11, muted: true)));

        AddSection("Обновления Yetta", "По нажатию проверяется GitHub Releases. Автоматической проверки в фоне нет.");
        ContentPanel.Children.Add(Card(Text(
            "Версия: " + ReleaseVersion +
            "\nУстановщик для текущего пользователя: YettaSetup.exe. " +
            "Скачанный файл проверяется по SHA-256 из GitHub Releases. " +
            "В preview-период цифровая подпись пока отсутствует; SHA-256 не доказывает личность издателя.",
            12, muted: true)));
        ContentPanel.Children.Add(ActionButton("Проверить и обновить", async () =>
        {
            await CheckAndInstallUpdateAsync();
        }, true));

        AddSection("Локальные данные");
        ContentPanel.Children.Add(Card(Text(
            "Файл настроек: " + _store.FilePath +
            "\nВнутренняя история Marvine удаляется после завершения работы.", 12, muted: true)));
        ContentPanel.Children.Add(ActionButton("Сбросить оформление", () =>
        {
            if (YettaMessageBox.Show(this, "Сбросить оформление и подтверждения? Уровни доступа сохранятся.",
                "Yetta", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
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
            var latest = await YettaUpdater.FindUpdateAsync(new Version(0, 3, 5));
            if (latest is null)
            {
                YettaMessageBox.Show(this, "Новой версии с установщиком пока нет.",
                    "Обновления Yetta", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!ConsentDialog.Confirm(this,
                "Установить обновление Yetta " + latest.Version + "?",
                "Установщик будет загружен с официального GitHub Releases. Yetta проверит SHA-256 файла, " +
                "но этот ранний релиз пока не подписан цифровой подписью. После загрузки будет запущен " +
                "стандартный установщик с правами текущего пользователя; он может закрыть Yetta для замены " +
                "её файлов. Закройте несохранённую работу. Для будущих привилегированных компонентов " +
                "потребуется отдельная подписанная цепочка обновления.",
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
            YettaMessageBox.Show(this, "Ошибка получения обновления: " + ex.Message,
                "Yetta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BuildSources()
    {
        AddIntro("Yetta / Sources", "Источники и контакты",
            "Ссылки на открытый проект, официальные релизы и ресурсы, используемые или запланированные для оформления Yetta.");
        AddSection("Проект");
        AddSource("GitHub · исходный код", "https://github.com/Ketsuneban/Yetta");
        AddSource("GitHub Releases · загрузка EXE", "https://github.com/Ketsuneban/Yetta/releases");
        AddSource("Сайт Yetta", "https://yetta.pages.dev/");
        AddSection("Визуальные ресурсы");
        AddSource("Icons8 · источник иконок", "https://icons8.ru/");
        ContentPanel.Children.Add(Card(Text(
            "Указание Icons8 — ссылка на поставщика иконок для будущего использования. Встроенные символы текущего прототипа не являются скачанным паком Icons8. Для добавленных в будущем иконок нужно соблюдать условия Icons8.", 12, muted: true)));
        AddSection("Связь");
        AddSource("invzum@gmail.com", "mailto:invzum@gmail.com");
        ContentPanel.Children.Add(Card(Text(
            "Лицензия исходного кода: MIT. Шрифт Inter: SIL Open Font License 1.1. Компилятор и библиотеки — системные Microsoft .NET/WPF; сторонний прикладной исходный код не копируется.", 12, muted: true)));
    }

    private void AddSource(string title, string address)
    {
        var panel = new StackPanel();
        panel.Children.Add(Text(title, 15, heading: true));
        panel.Children.Add(Text(address, 11, muted: true));
        panel.Children.Add(ActionButton("Открыть ↗", () => OpenExternal(address, title)));
        ContentPanel.Children.Add(Card(panel));
    }

    private void OpenExternal(string target, string title)
    {
        // Only constant URLs and the system's real Windows Terminal are passed
        // by Yetta's own UI. No untrusted command arguments or shell strings.
        if (target == "wt.exe" && !AccessProfiles.CanLaunchUnprivilegedTerminal(_settings))
        {
            YettaMessageBox.Show(this, "DAX Guard: сначала примените уровень «Пользователь».",
                "Yetta", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_settings.ConfirmExternalLinks && YettaMessageBox.Show(this,
            "Открыть «" + title + "» с действующими правами Windows?",
            "Подтверждение Yetta", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or
            InvalidOperationException or FileNotFoundException)
        {
            YettaMessageBox.Show(this, "Не удалось открыть «" + title + "»:\n" + ex.Message,
                "Yetta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
