using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Yetta.Core;

namespace Yetta.Desktop;

public partial class MainWindow
{
    private const string YettaUninstallId = "{FAAE9E3E-7B5F-494F-9CC1-2CE4D2F33C9F}_is1";

    private async Task ShowUninstallCatalogAsync()
    {
        try
        {
            var apps = await Task.Run(WindowsPackages.List, _updateStop.Token);
            AddConsoleEntry(new ConsoleEntry("", $"Каталог удаления · {apps.Count} записей")
            {
                Render = () => BuildUninstallCatalog(apps)
            });
        }
        catch (OperationCanceledException) when (_updateStop.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { AddConsoleEntry(new ConsoleEntry("", "[✖] Не удалось прочитать каталог программ.")); }
    }

    private UIElement BuildUninstallCatalog(IReadOnlyList<WindowsPackages.Package> apps)
    {
        var heading = LiteralConsoleLine("Выбор программы для удаления", ConsoleInk);
        var filter = new TextBox { ToolTip = "Фильтр по имени, издателю или ID", Padding = new Thickness(8),
            Background = ConsoleSurface, Foreground = ConsoleInk, CaretBrush = ConsoleInk };
        System.Windows.Automation.AutomationProperties.SetName(filter, "Поиск установленных программ");
        var groups = new ComboBox { ItemsSource = new[] { "Все", "Установленные", "Системные", "Скрытые", "MSIX" },
            SelectedIndex = 0, Margin = new Thickness(0, 6, 0, 6) };
        var list = new ListBox { DisplayMemberPath = nameof(WindowsPackages.Package.Label), Height = 175,
            Background = ConsoleSurface, Foreground = ConsoleInk, BorderThickness = new Thickness(0) };
        var choose = SubsyButton("Подставить имя или ID", () =>
        {
            if (list.SelectedItem is not WindowsPackages.Package selected || _consoleInput is not { } input) return;
            string target = selected.Selector is "--product-code" or "--id" ? selected.Id : selected.Name;
            if (target.Contains('"') || target.Any(char.IsControl)) return;
            input.Text = $"snob uninstall \"{target}\"";
            input.CaretIndex = input.Text.Length;
            input.Focus();
        }, true);
        void Refresh()
        {
            string term = filter.Text.Trim();
            list.ItemsSource = apps.Where(p => (groups.SelectedIndex switch
            {
                1 => !p.Hidden && !p.System, 2 => p.System, 3 => p.Hidden,
                4 => p.Scope.StartsWith("MSIX", StringComparison.Ordinal), _ => true
            }) && (p.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                p.Id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Publisher.Contains(term, StringComparison.CurrentCultureIgnoreCase))).ToArray();
            list.SelectedIndex = list.Items.Count == 0 ? -1 : 0;
            choose.IsEnabled = list.SelectedIndex >= 0;
        }
        filter.TextChanged += (_, _) => Refresh();
        groups.SelectionChanged += (_, _) => Refresh();
        list.SelectionChanged += (_, _) => choose.IsEnabled = list.SelectedIndex >= 0;
        list.MouseDoubleClick += (_, _) => { if (choose.IsEnabled) choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
        Refresh();
        return SubsyCard(new StackPanel { Children = { heading, filter, groups, list, choose } });
    }

    private async Task<string> RunSnobAsync(string request, string? argument)
    {
        if (request == "SNOB_DAX")
        {
            var status = await DaxAgentGate.GetStatusAsync(_updateStop.Token);
            return PageMarvineResult("DAX", [DaxStateLabel(status.State), $"Версия: {status.AgentVersion}",
                "Профиль Yetta: " + AccessProfiles.DisplayName(_settings.ActiveProfile)]);
        }
        if (request == "SNOB_YETTA") return await RunSnobYettaAsync(argument);
        if (request == "SNOB_UNINSTALL" && argument is null)
        {
            await ShowUninstallCatalogAsync();
            return "";
        }
        if (string.IsNullOrWhiteSpace(argument) || argument.StartsWith('-') || argument.Any(char.IsControl))
            return "[✖] Укажи название приложения или его точный ID.";

        bool check = request == "SNOB_CHECK";
        if (!check && _settings.ActiveProfile == "Guest")
            return "[✖] Для установки и удаления приложений выбери профиль Пользователь в DAX Guard.";

        if (request == "SNOB_INSTALL" && argument.Equals("Yetta.yetta", StringComparison.OrdinalIgnoreCase))
        {
            if (!ApproveAction("Установить Yetta", "Yetta.yetta", "Установщик скачает проверенную копию приложения с GitHub.")) return "";
            if (!await _updateGate.WaitAsync(0)) return "[♨] Идёт другое обновление.";
            try
            {
                var release = await YettaUpdater.FindUpdateAsync(new Version(0, 5, 99), _updateStop.Token);
                if (release is null) return "[✖] Релиз Yetta не найден.";
                await InstallReleaseAsync(release, defaultDirectory: true);
                return "";
            }
            finally { _updateGate.Release(); }
        }

        var apps = await Task.Run(WindowsPackages.List, _updateStop.Token);
        var matches = apps.Where(p => WindowsPackages.Matches(p, argument)).ToArray();
        if (check && matches.Length > 0)
            return ShowSnobDetails("Программа в Windows", string.Join("\n\n", matches.Select(p => p.Details)));
        var match = matches.Length == 1 ? matches[0] : null;
        bool remove = request == "SNOB_UNINSTALL";
        if (remove && matches.Length > 1) return "[✖] Найдено несколько программ. Укажи точный ID из каталога.";
        if (remove && (match is { System: true } || IsWindowsSecurity(argument))) return "[✖] Эту системную запись можно просмотреть через snob check.";
        if (remove && (match?.Id == YettaUninstallId || argument.Equals("Yetta", StringComparison.OrdinalIgnoreCase)))
            return "[ℹ] Для удаления Yetta используй snob yetta uninstall.";
        if (remove && !ApproveAction("Удалить программу", match?.Label ?? argument,
            "WinGet запустит зарегистрированное удаление выбранного приложения.", destructive: true)) return "";
        if (!check && !remove && !ApproveAction("Установить программу", argument,
            "WinGet найдёт пакет с этим точным именем или ID и запустит его установщик.")) return "";

        string verb = check ? "show" : remove ? "uninstall" : "install";
        string selector = remove && match is not null ? match.Selector : "--query";
        string target = remove && match is not null ? (selector == "--name" ? match.Name : match.Id) : argument;
        var result = await WindowsPackages.WinGetAsync(verb, selector, target, _updateStop.Token);
        if (check) return ShowSnobDetails(result.Code == 0 ? "Сведения WinGet" : "WinGet · сведения недоступны", result.Output);
        if (result.Code == 0) return remove ? "[✔] Программа удалена." : "[✔] Программа установлена.";
        return ShowSnobDetails("WinGet · операция не завершилась", $"Код: 0x{result.Code:X8}\n{result.Output}");
    }

    private static bool IsWindowsSecurity(string name) =>
        name.Equals("Windows Defender", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Microsoft Defender Antivirus", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Microsoft.SecHealthUI", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Microsoft.Windows.SecHealthUI", StringComparison.OrdinalIgnoreCase);

    private string ShowSnobDetails(string title, string details)
    {
        AddConsoleEntry(new ConsoleEntry("", title + "\n" + details)
        {
            Render = () =>
            {
                var info = LiteralConsoleLine(details);
                TextSelection.Allow(info);
                var scroll = new ScrollViewer { Content = info, MaxHeight = 310, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                return SubsyCard(new StackPanel { Children = { LiteralConsoleLine(title, ConsoleInk), scroll } });
            }
        });
        return "";
    }

    private async Task<string> RunSnobYettaAsync(string? action)
    {
        switch (action)
        {
            case "close":
                Close();
                return "";
            case "exit":
                await DaxAgentGate.TryStopDaxAsync();
                Close();
                return "";
            case "uninstall":
                if (_settings.ActiveProfile == "Guest")
                    return "[✖] Для удаления выбери профиль Пользователь в DAX Guard.";
                return await UninstallYettaAsync();
            default:
                return "[ℹ] snob yetta close | exit | uninstall";
        }
    }

    private async Task<string> UninstallYettaAsync()
    {
        var apps = await Task.Run(WindowsPackages.List, _updateStop.Token);
        string? app = apps.FirstOrDefault(p => p.Id.Equals(YettaUninstallId, StringComparison.OrdinalIgnoreCase))?.UninstallPath;
        var dax = apps.FirstOrDefault(p => p.Id.Equals("YettaDaxAdministrator_is1", StringComparison.OrdinalIgnoreCase));
        if (app is null)
            return "[✖] Установленная копия Yetta не найдена. Портативный EXE можно удалить из папки загрузок.";
        if (dax is not null && dax.UninstallPath is null)
            return "[✖] Windows не нашла зарегистрированный деинсталлятор профиля DAX.";
        if (!ApproveAction("Удалить Yetta", "Yetta, локальные настройки и профиль DAX",
            "Будут удалены установленные компоненты, история, журнал и кэш Yetta текущего пользователя.", destructive: true))
            return "";
        if (dax?.UninstallPath is { } helper)
        {
            string trusted = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Yetta DAX");
            if (!string.Equals(Path.GetDirectoryName(helper), trusted, StringComparison.OrdinalIgnoreCase))
                return "[✖] Административный компонент установлен по неожиданному пути.";
            var elevated = new ProcessStartInfo(helper) { UseShellExecute = true, Verb = "runas", Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART" };
            try
            {
                using var removal = Process.Start(elevated);
                if (removal is null) return "[✖] Удаление профиля DAX не запустилось.";
                await removal.WaitForExitAsync(_updateStop.Token);
                if (removal.ExitCode != 0) return "[✖] Служба DAX не была удалена.";
            }
            catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 1223)
            { return "[ℹ] Windows отменила удаление. Yetta остаётся установленной."; }
        }
        await DaxAgentGate.TryStopDaxAsync();
        var start = new ProcessStartInfo(app) { UseShellExecute = true, Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /PURGEYETTADATA=1" };
        using var uninstaller = Process.Start(start);
        if (uninstaller is null) return "[✖] Деинсталлятор Yetta не запустился.";
        Close();
        return "";
    }
}
