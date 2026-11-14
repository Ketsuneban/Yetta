using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Yetta.Core;

namespace Yetta.Desktop;

public partial class MainWindow : Window
{
    private readonly SettingsStore _store = new();
    private readonly AppSettings _settings;
    private readonly List<(string Command, string Reply)> _history = [];
    private readonly Dictionary<string, Button> _navigation;
    private string _page = "overview";
    private StackPanel? _consoleOutput;
    private ScrollViewer? _consoleScroll;
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

        _navigation = new Dictionary<string, Button>
        {
            ["overview"] = NavOverview,
            ["modules"] = NavModules,
            ["console"] = NavConsole,
            ["access"] = NavAccess,
            ["dax"] = NavDax,
            ["settings"] = NavSettings,
            ["about"] = NavAbout
        };

        ApplyTheme();
        Navigate("overview");
    }

    private static Brush Resource(string name) =>
        (Brush)Application.Current.Resources[name];

    private static void BrushSet(string key, Color color) =>
        Application.Current.Resources[key] = new SolidColorBrush(color);

    private static void BrushSet(string key, Brush brush) =>
        Application.Current.Resources[key] = brush;

    private void ApplyTheme()
    {
        bool glass = _settings.Theme == "Glass";
        if (glass)
        {
            BrushSet("WindowBackground", Color.FromRgb(13, 16, 30));
            BrushSet("SidebarBackground", Color.FromRgb(22, 27, 45));
            BrushSet("CardBackground", new LinearGradientBrush(
                Color.FromArgb(245, 44, 53, 78), Color.FromArgb(242, 25, 30, 47), 115));
            BrushSet("HoverBackground", Color.FromRgb(57, 62, 91));
            BrushSet("AccentBrush", Color.FromRgb(190, 173, 255));
            BrushSet("AccentForeground", Color.FromRgb(27, 18, 49));
            BrushSet("TextPrimary", Color.FromRgb(242, 243, 255));
            BrushSet("TextSecondary", Color.FromRgb(174, 185, 207));
            BrushSet("OutlineBrush", Color.FromRgb(77, 83, 110));
            BrushSet("SelectedBrush", Color.FromRgb(57, 48, 88));
        }
        else
        {
            BrushSet("WindowBackground", Color.FromRgb(8, 13, 21));
            BrushSet("SidebarBackground", Color.FromRgb(16, 27, 39));
            BrushSet("CardBackground", new LinearGradientBrush(
                Color.FromRgb(26, 43, 55), Color.FromRgb(17, 29, 43), 120));
            BrushSet("HoverBackground", Color.FromRgb(39, 59, 74));
            BrushSet("AccentBrush", Color.FromRgb(120, 232, 208));
            BrushSet("AccentForeground", Color.FromRgb(6, 34, 30));
            BrushSet("TextPrimary", Color.FromRgb(234, 246, 245));
            BrushSet("TextSecondary", Color.FromRgb(146, 168, 173));
            BrushSet("OutlineBrush", Color.FromRgb(53, 76, 83));
            BrushSet("SelectedBrush", Color.FromRgb(32, 69, 63));
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
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Не удалось сохранить настройки:\n" + ex.Message,
                "Yetta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string page)
            Navigate(page);
    }

    private void Navigate(string page)
    {
        if (!_navigation.ContainsKey(page)) return;
        _page = page;
        _consoleOutput = null;
        _consoleScroll = null;
        _consoleInput = null;
        foreach (var pair in _navigation)
        {
            pair.Value.Background = pair.Key == page
                ? Resource("SelectedBrush") : Brushes.Transparent;
            pair.Value.Foreground = pair.Key == page ? Accent : Muted;
            pair.Value.FontWeight = pair.Key == page ? FontWeights.SemiBold : FontWeights.Normal;
        }

        var headings = new Dictionary<string, (string Title, string Caption)>
        {
            ["overview"] = ("Главная", "Ваше рабочее пространство"),
            ["modules"] = ("Модули", "Только безопасные действия в этом прототипе"),
            ["console"] = ("Консоль Yetta", "Команды приложения, не системный терминал"),
            ["access"] = ("Access Manager", "Текущие полномочия и планируемые профили"),
            ["dax"] = ("DAX Security", "Архитектура защиты и ограничения прототипа"),
            ["settings"] = ("Настройки", "Предпочтения интерфейса Yetta"),
            ["about"] = ("О проекте", "Yetta 0.1.0-preview")
        };
        PageTitle.Text = headings[page].Title;
        PageSubtitle.Text = headings[page].Caption;
        ContentPanel.Children.Clear();

        switch (page)
        {
            case "overview": BuildOverview(); break;
            case "modules": BuildModules(); break;
            case "console": BuildConsole(); break;
            case "access": BuildAccess(); break;
            case "dax": BuildDax(); break;
            case "settings": BuildSettings(); break;
            case "about": BuildAbout(); break;
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
            LineHeight = size * 1.65
        };
    }

    private StackPanel Column(double spacing = 8)
    {
        return new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0) };
    }

    private Border Card(UIElement child, Thickness? margin = null)
    {
        return new Border
        {
            Background = Surface,
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(22),
            Margin = margin ?? new Thickness(0, 0, 0, 14),
            Child = child
        };
    }

    private Button ActionButton(string name, Action callback, bool primary = false)
    {
        var button = new Button
        {
            Content = name,
            Style = (Style)FindResource(primary ? "PrimaryButton" : "SecondaryButton"),
            Margin = new Thickness(0, 0, 10, 8),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        button.Click += (_, _) => callback();
        return button;
    }

    private void AddIntro(string eyebrow, string headline, string description)
    {
        ContentPanel.Children.Add(new TextBlock
        {
            Text = eyebrow.ToUpperInvariant(),
            Foreground = Accent,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12)
        });
        ContentPanel.Children.Add(new TextBlock
        {
            Text = headline,
            Foreground = Ink,
            FontSize = 34,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 9)
        });
        var text = Text(description, 14, muted: true);
        text.Margin = new Thickness(0, 0, 0, 25);
        ContentPanel.Children.Add(text);
    }

    private void AddSection(string heading, string? hint = null)
    {
        var title = Text(heading, 20, heading: true);
        title.Margin = new Thickness(0, 18, 0, hint is null ? 15 : 3);
        ContentPanel.Children.Add(title);
        if (hint is not null)
        {
            var sub = Text(hint, 12, muted: true);
            sub.Margin = new Thickness(0, 0, 0, 14);
            ContentPanel.Children.Add(sub);
        }
    }

    private Border MiniCard(string label, string value, string detail)
    {
        var panel = Column();
        panel.Children.Add(Text(label.ToUpperInvariant(), 10, muted: true));
        var main = Text(value, 19, heading: true);
        main.Margin = new Thickness(0, 7, 0, 6);
        main.Foreground = Accent;
        panel.Children.Add(main);
        panel.Children.Add(Text(detail, 11, muted: true));
        var card = Card(panel, new Thickness(0, 0, 12, 12));
        card.Width = 214;
        return card;
    }

    private Border FeatureCard(string glyph, string heading, string description, string label)
    {
        var panel = Column();
        panel.Children.Add(new TextBlock
        {
            Text = glyph,
            FontSize = 28,
            Foreground = Accent,
            Margin = new Thickness(0, 0, 0, 10)
        });
        panel.Children.Add(Text(label, 10, muted: true));
        var head = Text(heading, 18, heading: true);
        head.Margin = new Thickness(0, 6, 0, 7);
        panel.Children.Add(head);
        panel.Children.Add(Text(description, 12, muted: true));
        var border = Card(panel, new Thickness(0, 0, 12, 12));
        border.Width = 280;
        border.MinHeight = 199;
        return border;
    }

    private void AddNotice(string heading, string explanation)
    {
        var contents = Column();
        var head = Text("ⓘ  " + heading, 14, heading: true);
        head.Foreground = Accent;
        contents.Children.Add(head);
        var body = Text(explanation, 12, muted: true);
        body.Margin = new Thickness(0, 8, 0, 0);
        contents.Children.Add(body);
        var notice = Card(contents, new Thickness(0, 8, 0, 15));
        notice.Background = Resource("SelectedBrush");
        ContentPanel.Children.Add(notice);
    }

    private void BuildOverview()
    {
        AddIntro("Yetta / Desktop preview", "Windows. На твоих условиях.",
            "Первый работающий прототип Yetta для Windows 11. Уже есть локальные настройки, темы, информация о системе и безопасная консоль. Управление Windows и DAX появятся только после отдельного тестирования.");

        var facts = new WrapPanel();
        facts.Children.Add(MiniCard("Статус", "PREVIEW 0.1", "Ранняя версия приложения"));
        facts.Children.Add(MiniCard("Профиль Yetta", "GUEST", "Повышение прав не реализовано"));
        facts.Children.Add(MiniCard("Защита DAX", "НЕ АКТИВНА", "Привилегированного сервиса нет"));
        ContentPanel.Children.Add(facts);

        AddSection("Быстрые действия", "Каждое действие запускается только по нажатию пользователя.");
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Настройки Windows ↗",
            () => OpenSystemApp("ms-settings:", "Настройки Windows"), true));
        actions.Children.Add(ActionButton("Проводник ↗",
            () => OpenSystemApp("explorer.exe", "Проводник")));
        actions.Children.Add(ActionButton("Открыть консоль →", () => Navigate("console")));
        ContentPanel.Children.Add(actions);

        AddSection("Основные компоненты");
        var cards = new WrapPanel();
        cards.Children.Add(FeatureCard("◈", "Персонализация",
            "Два оформления Yetta: Windows 11 и Glass Preview. Переключаются без перезапуска.", "INTERFACE"));
        cards.Children.Add(FeatureCard("⬡", "DAX Security",
            "Контур будущей защиты описан, но пока не установлен и не подменяет антивирус.", "PLANNED"));
        cards.Children.Add(FeatureCard("⛨", "Access Manager",
            "Пять профилей в архитектуре. Текущий прототип не запрашивает системных прав.", "PLANNED"));
        ContentPanel.Children.Add(cards);
        AddNotice("Безопасный прототип",
            "Эта версия не изменяет реестр, службы, защиту Windows и системные файлы. Все будущие операции будут проходить проверку доступа.");
    }

    private void BuildModules()
    {
        AddIntro("Yetta / Modules", "Модульная основа",
            "В текущей версии доступны только безопасные просмотр и запуск встроенных интерфейсов Windows. Ни один системный редактор ещё не установлен.");

        AddSection("Доступно в этой версии");
        var actions = Column();
        actions.Children.Add(ModuleRow("◉", "Сведения о системе",
            "ОС, архитектура, процессоры, среда .NET, пользователь и время работы.",
            "Открыть", () => ShowSystemInformation()));
        actions.Children.Add(ModuleRow("⚙", "Настройки Windows",
            "Открывает системное приложение параметров Windows по запросу пользователя.",
            "Запустить ↗", () => OpenSystemApp("ms-settings:", "Настройки Windows")));
        actions.Children.Add(ModuleRow("▣", "Проводник",
            "Запускает стандартный Проводник Windows.",
            "Запустить ↗", () => OpenSystemApp("explorer.exe", "Проводник")));
        actions.Children.Add(ModuleRow("⛨", "Безопасность Windows",
            "Открывает системную страницу безопасности; ничего не отключает.",
            "Открыть ↗", () => OpenSystemApp("ms-settings:windowsdefender", "Безопасность Windows")));
        ContentPanel.Children.Add(actions);

        AddSection("Запланировано");
        var future = new WrapPanel();
        future.Children.Add(FeatureCard("▥", "Редактор панели задач",
            "Изменение внешнего вида и поведения панели задач с поддержкой восстановления.", "НЕ РЕАЛИЗОВАНО"));
        future.Children.Add(FeatureCard("⌗", "Службы и автозагрузка",
            "Просмотр, сравнение и безопасное управление после внедрения DAX Guard.", "НЕ РЕАЛИЗОВАНО"));
        future.Children.Add(FeatureCard("◇", "Персонализация системы",
            "Модули системных параметров, интерфейса и компонентов Windows.", "НЕ РЕАЛИЗОВАНО"));
        ContentPanel.Children.Add(future);
    }

    private Border ModuleRow(string glyph, string title, string description, string action, Action callback)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock
        {
            Text = glyph, FontSize = 25, Foreground = Accent,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var copy = Column();
        copy.Children.Add(Text(title, 15, heading: true));
        copy.Children.Add(Text(description, 11, muted: true));
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);

        var button = ActionButton(action, callback);
        button.Margin = new Thickness(14, 4, 0, 0);
        button.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(button, 2);
        grid.Children.Add(button);
        return Card(grid);
    }

    private void ShowSystemInformation()
    {
        AddSection("Сведения о текущем устройстве");
        var details = Column();
        foreach (var (name, value) in new (string, string)[]
        {
            ("Операционная система", SystemSnapshot.Os),
            ("Архитектура", SystemSnapshot.Architecture),
            ("Среда выполнения", SystemSnapshot.Runtime),
            ("Логические процессоры", SystemSnapshot.LogicalProcessors.ToString()),
            ("Пользователь", SystemSnapshot.User),
            ("Имя компьютера", SystemSnapshot.Machine),
            ("Время работы", SystemSnapshot.Uptime.ToString(@"d\.hh\:mm\:ss"))
        })
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(195) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(Text(name, 12, muted: true));
            var valueBlock = Text(value, 12, heading: true);
            Grid.SetColumn(valueBlock, 1);
            row.Children.Add(valueBlock);
            details.Children.Add(row);
        }
        ContentPanel.Children.Add(Card(details));
    }

    private void BuildAccess()
    {
        AddIntro("Yetta / Access Manager", "Полномочия — под контролем.",
            "Система профилей находится на этапе проектирования. Установка повышенных прав, работа от администратора через Yetta и системный брокер пока отключены.");
        bool tokenIsElevated = false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            tokenIsElevated = new WindowsPrincipal(identity)
                .IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // This is diagnostic data only; never grant privileges based on it.
        }
        var panel = Column();
        panel.Children.Add(Text("УСТАНОВЛЕННЫЙ ПРОФИЛЬ YETTA", 10, muted: true));
        var current = Text("Guest", 26, heading: true);
        current.Foreground = Accent;
        current.Margin = new Thickness(0, 5, 0, 8);
        panel.Children.Add(current);
        panel.Children.Add(Text("Прототип не содержит привилегированного сервиса и не может выполнять системные изменения.", 12, muted: true));
        var diag = Text(tokenIsElevated
            ? "Текущий процесс Windows: токен администратора (диагностика, не профиль Yetta)."
            : "Текущий процесс Windows: обычный токен без повышения прав.", 12, muted: true);
        diag.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(diag);
        ContentPanel.Children.Add(Card(panel));
        AddSection("Будущие уровни доступа");
        var profiles = new (string, string, string)[]
        {
            ("01 / Guest", "Минимальные функции — профиль по умолчанию.", "ТЕКУЩИЙ"),
            ("02 / Safe User", "Просмотр ресурсов в пределах разрешений Windows, без изменений.", "ПЛАН"),
            ("03 / User", "Операции текущего стандартного пользователя.", "ПЛАН"),
            ("04 / Administrator", "Авторизованные операции с настоящим повышением прав Windows.", "ПЛАН"),
            ("05 / System", "Ограниченный набор операций через проверяемую службу и TTY.", "ПЛАН")
        };
        foreach (var (name, description, status) in profiles)
        {
            var item = Column();
            var header = new DockPanel();
            var badge = Text(status, 10, muted: true);
            DockPanel.SetDock(badge, Dock.Right);
            header.Children.Add(badge);
            header.Children.Add(Text(name, 15, heading: true));
            item.Children.Add(header);
            item.Children.Add(Text(description, 12, muted: true));
            ContentPanel.Children.Add(Card(item));
        }
        AddNotice("DAX Guard: не установлен",
            "Нельзя подменить выбор уровня доступа реально полученными правами. В этой версии нет операций, требующих повышения. Профили выше Guest недоступны.");
    }

    private void BuildDax()
    {
        AddIntro("Yetta / Defense & Access eXecutive", "DAX. Безопасность — по умолчанию.",
            "Будущая единая подсистема проверки доступа, отслеживания процессов Yetta и аудита операций. Здесь показана архитектура, а не активная защита.");
        var block = Column();
        block.Children.Add(Text("СТАТУС КОМПОНЕНТОВ", 10, muted: true));
        var status = Text("НЕ УСТАНОВЛЕНО", 28, heading: true);
        status.Foreground = Accent;
        status.Margin = new Thickness(0, 6, 0, 9);
        block.Children.Add(status);
        block.Children.Add(Text("DAX Gate, Process, Threat, System, Audit и Update пока не выполняются. Приложение не анализирует другие процессы и не заменяет Microsoft Defender.", 12, muted: true));
        ContentPanel.Children.Add(Card(block));
        AddSection("Запланированная архитектура");
        foreach (var (name, description) in new (string, string)[]
        {
            ("DAX Gate", "Проверка политик и разрешённого уровня перед операцией."),
            ("DAX Process", "Наблюдение только за программами, запущенными из Yetta, и их потомками."),
            ("DAX Threat", "Локальные проверки Microsoft Defender и репутация по хешу."),
            ("DAX System", "Атрибуция изменений файлов, реестра и служб там, где она технически возможна."),
            ("DAX Audit", "Журнал решений и результатов."),
            ("DAX Update", "Проверка подписи и целостности обновлений.")
        })
        {
            var details = Column();
            details.Children.Add(Text(name, 14, heading: true));
            details.Children.Add(Text(description, 12, muted: true));
            ContentPanel.Children.Add(Card(details));
        }
        AddNotice("Не является антивирусом",
            "Ни проверка вредоносных файлов, ни защита от изменений в текущем прототипе не функционируют. Любое будущее действие с повышенными правами требует аудита безопасности.");
    }

    private void BuildConsole()
    {
        AddIntro("Yetta / Console", "Контроль через команды.",
            "Это внутренняя консоль Yetta с фиксированным списком команд. Она не запускает CMD, PowerShell и исполняемые файлы.");

        var terminal = new Border
        {
            Background = Resource("SidebarBackground"),
            BorderBrush = Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18)
        };

        var outer = new DockPanel();
        var commandBar = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(commandBar, Dock.Bottom);
        var send = ActionButton("Выполнить ↵", RunConsole, true);
        send.Margin = new Thickness(10, 0, 0, 0);
        DockPanel.SetDock(send, Dock.Right);
        commandBar.Children.Add(send);

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
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Введите help для списка команд"
        };
        _consoleInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                RunConsole();
                e.Handled = true;
            }
        };
        commandBar.Children.Add(_consoleInput);
        outer.Children.Add(commandBar);

        _consoleOutput = Column();
        _consoleScroll = new ScrollViewer
        {
            Height = 290,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _consoleOutput
        };
        outer.Children.Add(_consoleScroll);
        terminal.Child = outer;
        ContentPanel.Children.Add(terminal);

        foreach (var (command, reply) in _history)
        {
            DrawConsoleLine("> " + command, true);
            if (!string.IsNullOrWhiteSpace(reply)) DrawConsoleLine(reply, false);
        }

        if (_history.Count == 0)
        {
            DrawConsoleLine("Yetta Console 0.1.0-preview", true);
            DrawConsoleLine("Безопасная консоль готова. Начните с команды help.", false);
        }
        _consoleInput.Focus();
    }

    private void DrawConsoleLine(string line, bool prompt)
    {
        if (_consoleOutput is null) return;
        var block = new TextBlock
        {
            Text = line,
            Foreground = prompt ? Accent : Ink,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        _consoleOutput.Children.Add(block);
        _consoleScroll?.ScrollToEnd();
    }

    private void RunConsole()
    {
        if (_consoleInput is null) return;
        string raw = _consoleInput.Text.Trim();
        if (raw.Length == 0) return;
        var result = ConsoleEngine.Execute(raw, new ConsoleContext(_settings.Theme));
        _consoleInput.Clear();

        if (result.ClearOutput)
        {
            _history.Clear();
            _consoleOutput?.Children.Clear();
            DrawConsoleLine("История вывода очищена.", false);
            return;
        }
        _history.Add((raw, result.Message));
        if (_history.Count > 100) _history.RemoveAt(0);
        DrawConsoleLine("> " + raw, true);
        DrawConsoleLine(result.Message, false);

        if (result.SwitchThemeTo is { } theme)
        {
            SetTheme(theme);
        }
        else if (result.NavigateTo is { } page)
        {
            Navigate(page);
        }
    }

    private void BuildSettings()
    {
        AddIntro("Yetta / Preferences", "Сделай Yetta своей.",
            "Настройки сохраняются только в вашем локальном профиле Windows. Переключение темы не меняет параметры операционной системы.");
        AddSection("Внешний вид");
        var themes = new WrapPanel();
        themes.Children.Add(ActionButton("Windows 11 · Fluent",
            () => SetTheme("Fluent"), _settings.Theme == "Fluent"));
        themes.Children.Add(ActionButton("Glass · Preview",
            () => SetTheme("Glass"), _settings.Theme == "Glass"));
        ContentPanel.Children.Add(themes);
        AddNotice("Режим Glass",
            "Это первая визуальная интерпретация полупрозрачных слоёв. Настоящий системный Acrylic/Mica и отдельный интерфейс WinUI 3 — возможный следующий этап.");

        AddSection("Подтверждения и конфиденциальность");
        var check = new CheckBox
        {
            Content = "Спрашивать подтверждение перед открытием других приложений",
            IsChecked = _settings.ConfirmExternalLinks,
            Foreground = Ink,
            Margin = new Thickness(0, 0, 0, 14)
        };
        check.Checked += (_, _) => { _settings.ConfirmExternalLinks = true; SavePreferences(); };
        check.Unchecked += (_, _) => { _settings.ConfirmExternalLinks = false; SavePreferences(); };
        ContentPanel.Children.Add(Card(check));

        var storage = Column();
        storage.Children.Add(Text("Файл предпочтений", 13, heading: true));
        storage.Children.Add(Text(_store.FilePath, 11, muted: true));
        storage.Children.Add(Text("Данные не отправляются в облако. Консольная история хранится только до закрытия программы.", 11, muted: true));
        ContentPanel.Children.Add(Card(storage));
        ContentPanel.Children.Add(ActionButton("Сбросить настройки Yetta", () =>
        {
            if (MessageBox.Show(this, "Сбросить только настройки интерфейса Yetta?",
                "Сброс", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            _settings.Theme = "Fluent";
            _settings.ConfirmExternalLinks = true;
            ApplyTheme();
            SavePreferences();
            Navigate("settings");
        }));
    }

    private void BuildAbout()
    {
        AddIntro("Yetta / Project", "Windows, on your terms.",
            "Публичный проект глубокой персонализации Windows 11. Этот выпуск — техническая пробная версия приложения, а не готовый системный оптимизатор.");
        var information = Column();
        information.Children.Add(Text("Yetta Desktop 0.1.0-preview", 22, heading: true));
        information.Children.Add(Text("C# · .NET 10 · WPF (временный UI-фреймворк)", 12, muted: true));
        information.Children.Add(Text("Оригинальная целевая концепция интерфейса — WinUI 3; WPF выбран для быстрого запуска и проверки архитектуры без сторонних зависимостей.", 12, muted: true));
        ContentPanel.Children.Add(Card(information));

        AddSection("Открытый проект");
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Исходный код на GitHub ↗",
            () => OpenSystemApp("https://github.com/Ketsuneban/Yetta", "GitHub"), true));
        actions.Children.Add(ActionButton("Сайт проекта ↗",
            () => OpenSystemApp("https://yetta.pages.dev/", "Сайт Yetta")));
        ContentPanel.Children.Add(actions);
        AddNotice("Ограничения версии",
            "Нет привилегированной службы, драйверов, DAX, установки профилей, TTY или модификации Windows. Из системных действий доступны только запуск стандартных системных интерфейсов пользователем.");
    }

    private void OpenSystemApp(string target, string title)
    {
        if (_settings.ConfirmExternalLinks &&
            MessageBox.Show(this, "Открыть «" + title + "»?",
                "Подтверждение действия", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or
            InvalidOperationException or System.IO.FileNotFoundException)
        {
            MessageBox.Show(this, "Не удалось запустить «" + title + "»:\n" + ex.Message,
                "Yetta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
