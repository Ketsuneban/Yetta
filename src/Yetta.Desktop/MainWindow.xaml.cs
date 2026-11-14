using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Yetta.Core;

namespace Yetta.Desktop;

public partial class MainWindow : Window
{
    private const string ReleaseVersion = "0.2.0-preview";
    private readonly SettingsStore _store = new();
    private readonly AppSettings _settings;
    private readonly Dictionary<string, Button> _navigation;
    private readonly List<(string Command, string Reply)> _history = [];
    private string _page = "overview";
    private string _consoleMode = "Mavrine";
    private StackPanel? _consoleOutput;
    private TextBox? _consoleInput;

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
            ["logs"] = NavLogs,
            ["recovery"] = NavRecovery,
            ["settings"] = NavSettings,
            ["sources"] = NavSources
        };
        ApplyTheme();
        Navigate("overview");
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
            SetColor("WindowBackground", Color.FromRgb(13, 16, 30));
            SetColor("SidebarBackground", Color.FromRgb(22, 27, 45));
            SetBrush("CardBackground", new LinearGradientBrush(
                Color.FromRgb(44, 53, 78), Color.FromRgb(25, 30, 47), 115));
            SetColor("HoverBackground", Color.FromRgb(57, 62, 91));
            SetColor("AccentBrush", Color.FromRgb(190, 173, 255));
            SetColor("AccentForeground", Color.FromRgb(27, 18, 49));
            SetColor("TextPrimary", Color.FromRgb(242, 243, 255));
            SetColor("TextSecondary", Color.FromRgb(174, 185, 207));
            SetColor("OutlineBrush", Color.FromRgb(77, 83, 110));
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
        ThemeIndicator.Text = glass ? "Glass Preview" : "Windows 11";
    }

    private void SetTheme(string theme)
    {
        if (theme is not ("Fluent" or "Glass")) return;
        _settings.Theme = theme;
        ApplyTheme();
        SavePreferences();
        Navigate(_page);
    }

    private void SavePreferences()
    {
        try { _store.Save(_settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Не удалось сохранить настройки:\n" + ex.Message,
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
            ["console"] = ("Консоль", "Mavrine и встроенные консоли Windows"),
            ["guard"] = ("DAX Guard", "Уровни доступа и их установка"),
            ["logs"] = ("DAX Logs", "Будущий журнал событий безопасности"),
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
            case "logs": BuildLogs(); break;
            case "recovery": BuildRecovery(); break;
            case "settings": BuildSettings(); break;
            case "sources": BuildSources(); break;
        }
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
        tiles.Children.Add(SummaryTile("DAX", "В разработке",
            "Анализ процессов и защита не установлены."));
        ContentPanel.Children.Add(tiles);
        AddNotice("Заглушка", "Этот блок зарезервирован для будущих сведений Yetta. Новые действия здесь не выполняются.");
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
            "Чтение файлов возможно только там, где Windows разрешает это текущему процессу. Запись в системные папки, управление службами и изменения реестра Yetta не выполняет. Mavrine допускает только внутренние команды. Windows Terminal доступен через DAX Guard лишь при активном профиле «Пользователь».", 12, muted: true));
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
        foreach (var (label, value) in specs)
        {
            var row = new StackPanel();
            row.Children.Add(Text(label, 10, heading: true, muted: true));
            row.Children.Add(Text(value, 13));
            ContentPanel.Children.Add(Card(row));
        }
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
            Foreground = Brushes.Black,
            Background = Brushes.White,
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
                Content = (profile == _settings.ActiveProfile ? "✓  " : "    ") + AccessProfiles.DisplayName(profile)
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
        foreach (var (profile, number) in AccessProfiles.All.Select((name, i) => (name, i + 1)))
        {
            bool installed = _settings.InstalledProfiles.Contains(profile);
            bool applied = profile == _settings.ActiveProfile;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var description = new StackPanel();
            description.Children.Add(Text(number.ToString("00") + "  " +
                AccessProfiles.DisplayName(profile), 15, heading: true));
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
        AddNotice("Ограничение уровня SYSTEM",
            "Выбор в приложении не способен предоставить SYSTEM-права. Для уровней Администратор и Система потребуются отдельная служба, UAC/TTY, независимый аудит и проверка доступа.");
    }

    private void InstallOrApplyProfile(string profile)
    {
        if (!AccessProfiles.CanInstallLogicalProfile(profile))
        {
            MessageBox.Show(this,
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
        Navigate("guard");
    }

    private void BuildLogs()
    {
        AddIntro("Yetta / DAX Logs", "Журнал событий DAX",
            "Раздел зарезервирован для аудита решений DAX Guard, проверок угроз и операций, запущенных из Yetta.");
        AddNotice("Заглушка — нет активного логирования",
            "DAX Process, Threat, Audit пока не реализованы. Эта страница не заявляет, что выполняется наблюдение за процессами, и не содержит фиктивных записей.");
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
        AddIntro("Yetta / Console", "Консоль и терминалы",
            "Mavrine встроена в Yetta. Windows Terminal и PowerShell — настоящие приложения Windows: при разрешении DAX Guard они открываются в собственных окнах, сохраняя оригинальные функции.");
        var tabs = new WrapPanel();
        foreach (string mode in new[] { "Mavrine", "Windows Terminal", "Windows PowerShell" })
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
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(_consoleMode == "Windows PowerShell" ? 4 : 14),
            Background = _consoleMode switch
            {
                "Windows PowerShell" => new SolidColorBrush(Color.FromRgb(1, 36, 86)),
                "Windows Terminal" => new SolidColorBrush(Color.FromRgb(15, 15, 18)),
                _ => Resource("SidebarBackground")
            },
            Margin = new Thickness(0, 12, 0, 14),
            Padding = new Thickness(17),
            MinHeight = 280
        };
        var body = new StackPanel();
        var heading = Text(_consoleMode + "  ·  " + (_consoleMode == "Mavrine" ? "Yetta" : "Windows"), 14, heading: true);
        heading.Foreground = _consoleMode == "Windows PowerShell" ? Brushes.White : Accent;
        heading.Margin = new Thickness(0, 0, 0, 12);
        body.Children.Add(heading);
        if (_consoleMode != "Mavrine")
        {
            string warning = _consoleMode == "Windows Terminal"
                ? "Обычный терминал Windows без повышения прав. Доступ разрешается только в профиле «Пользователь». Встроить все функции Windows Terminal в окно Yetta без полноценного терминального хоста нельзя."
                : "Административная Windows PowerShell запускается только после настоящей проверки прав и Windows UAC. Сейчас у Yetta нет такого брокера, поэтому запуск отключён.";
            var desc = Text(warning, 13);
            desc.Foreground = Brushes.White;
            body.Children.Add(desc);
            if (_consoleMode == "Windows Terminal")
            {
                body.Children.Add(ActionButton("Открыть Windows Terminal ↗",
                    () => OpenExternal("wt.exe", "Windows Terminal"),
                    enabled: AccessProfiles.CanLaunchUnprivilegedTerminal(_settings)));
                if (!AccessProfiles.CanLaunchUnprivilegedTerminal(_settings))
                    body.Children.Add(Text("Для запуска установите и примените уровень «Пользователь» в DAX Guard.", 12));
            }
            else
            {
                body.Children.Add(ActionButton("Запуск PowerShell (Администратор) — недоступен",
                    () => {}, enabled: AccessProfiles.CanLaunchElevatedShell(_settings)));
            }
        }
        else
        {
            var display = new StackPanel();
            if (_history.Count == 0)
            {
                display.Children.Add(ConsoleLine("Mavrine / Yetta " + ReleaseVersion, true));
                display.Children.Add(ConsoleLine("Введите help. Произвольные команды Windows не выполняются.", false));
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
                Height = 238,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = display
            });
            _consoleInput = new TextBox
            {
                Background = Surface,
                Foreground = Ink,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Padding = new Thickness(12, 10, 12, 10),
                CaretBrush = Accent,
                ToolTip = "Введите команду Mavrine"
            };
            _consoleInput.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    RunMavrine();
                    e.Handled = true;
                }
            };
            body.Children.Add(_consoleInput);
            body.Children.Add(ActionButton("Выполнить ↵", RunMavrine, true));
        }
        terminal.Child = body;
        ContentPanel.Children.Add(terminal);
        AddNotice("Разделение прав",
            "Windows Terminal не наследует права системы Yetta: он запускается с токеном текущего пользователя. Mavrine соблюдает логическую политику Yetta, но не предоставляет права администратора или SYSTEM.");
        _consoleInput?.Focus();
    }

    private TextBlock ConsoleLine(string message, bool prompt)
    {
        var line = Text(message, 12);
        line.FontFamily = new FontFamily("Consolas");
        line.Foreground = prompt ? Accent : Ink;
        line.Margin = new Thickness(0, 0, 0, 10);
        return line;
    }

    private void RunMavrine()
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

        AddSection("Локальные данные");
        ContentPanel.Children.Add(Card(Text(
            "Файл настроек: " + _store.FilePath +
            "\nВнутренняя история Mavrine удаляется после завершения работы.", 12, muted: true)));
        ContentPanel.Children.Add(ActionButton("Сбросить оформление", () =>
        {
            if (MessageBox.Show(this, "Сбросить оформление и подтверждения? Уровни доступа сохранятся.",
                "Yetta", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            _settings.Theme = "Fluent";
            _settings.ConfirmExternalLinks = true;
            ApplyTheme();
            SavePreferences();
            Navigate("settings");
        }));
    }

    private void BuildSources()
    {
        AddIntro("Yetta / Sources", "Источники и контакты",
            "Ссылки на открытый проект, официальные релизы и ресурсы, используемые или запланированные для оформления Yetta.");
        AddSection("Проект");
        AddSource("GitHub · исходный код", "https://github.com/Ketsuneban/Yetta");
        AddSource("GitHub Releases · загрузка EXE", "https://github.com/Ketsuneban/Yetta/releases/latest");
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
            MessageBox.Show(this, "DAX Guard: сначала примените уровень «Пользователь».",
                "Yetta", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_settings.ConfirmExternalLinks && MessageBox.Show(this,
            "Открыть «" + title + "» с действующими правами Windows?",
            "Подтверждение Yetta", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or
            InvalidOperationException or FileNotFoundException)
        {
            MessageBox.Show(this, "Не удалось открыть «" + title + "»:\n" + ex.Message,
                "Yetta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
