using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
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
        var heading = Text("Установленные приложения", 17, heading: true);
        var count = Text("", 12, muted: true);
        var filter = new TextBox { ToolTip = "Фильтр по имени, издателю или ID", Padding = new Thickness(10, 6, 10, 6),
            Background = System.Windows.Media.Brushes.Transparent, Foreground = ConsoleInk, CaretBrush = ConsoleInk,
            VerticalContentAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(0) };
        System.Windows.Automation.AutomationProperties.SetName(filter, "Поиск установленных программ");
        var hint = Text("Поиск по имени, издателю или ID", 12, muted: true);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(10, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        var searchField = new Border { Background = ConsoleSurface, CornerRadius = new CornerRadius(7),
            MinHeight = 38, Child = new Grid { Children = { filter, hint } } };
        var groups = new ComboBox { ItemsSource = new[] { "Видимые", "Установленные", "Системные", "Скрытые", "MSIX", "Все" },
            SelectedIndex = 0, Width = 157, Margin = new Thickness(10, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(groups, "Тип приложения");
        var filters = new DockPanel { Margin = new Thickness(0, 9, 0, 8) };
        DockPanel.SetDock(groups, Dock.Right);
        filters.Children.Add(groups);
        filters.Children.Add(searchField);

        var list = new ListBox { Height = 250, Background = ConsoleSurface, Foreground = ConsoleInk,
            BorderThickness = new Thickness(0), Padding = new Thickness(2), ItemTemplate = AppCatalogTemplate(),
            ItemContainerStyle = AppCatalogRowStyle() };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        var empty = Text("Приложения не найдены", 13, muted: true);
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.VerticalAlignment = VerticalAlignment.Center;
        empty.IsHitTestVisible = false;
        var listArea = new Grid { Children = { list, empty } };

        var details = Text("", 12, muted: true);
        details.TextWrapping = TextWrapping.NoWrap;
        details.TextTrimming = TextTrimming.CharacterEllipsis;
        details.VerticalAlignment = VerticalAlignment.Center;
        var choose = SubsyButton("Подставить команду", () =>
        {
            if (list.SelectedItem is not WindowsPackages.Package selected || _consoleInput is not { IsReadOnly: false } input) return;
            string target = selected.Selector is "--product-code" or "--id" ? selected.Id : selected.Name;
            if (target.Contains('"') || target.Any(char.IsControl)) return;
            input.Text = $"snob uninstall \"{target}\"";
            input.CaretIndex = input.Text.Length;
            input.Focus();
        }, true);
        choose.Margin = new Thickness(12, 0, 0, 0);
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(choose, Dock.Right);
        footer.Children.Add(choose);
        footer.Children.Add(details);

        void UpdateSelection()
        {
            var selected = list.SelectedItem as WindowsPackages.Package;
            choose.IsEnabled = selected is not null;
            details.Text = selected is null ? "" : selected.Id + " · " + selected.Scope;
            details.ToolTip = selected?.Details;
        }

        void Refresh()
        {
            string term = filter.Text.Trim();
            hint.Visibility = filter.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            var results = apps.Where(p => (groups.SelectedIndex switch
            {
                0 => !p.Hidden, 1 => !p.Hidden && !p.System, 2 => p.System && !p.Hidden, 3 => p.Hidden,
                4 => p.PackageFullName is not null && !p.Hidden, _ => true
            }) && (p.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                p.Id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.Publisher.Contains(term, StringComparison.CurrentCultureIgnoreCase))).ToArray();
            list.ItemsSource = results;
            count.Text = $"Найдено: {results.Length}";
            empty.Visibility = results.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateSelection();
        }
        filter.TextChanged += (_, _) => Refresh();
        groups.SelectionChanged += (_, _) => Refresh();
        list.SelectionChanged += (_, _) => UpdateSelection();
        list.MouseDoubleClick += (_, e) =>
        {
            if (choose.IsEnabled && e.OriginalSource is DependencyObject hit &&
                ItemsControl.ContainerFromElement(list, hit) is ListBoxItem)
                choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && choose.IsEnabled)
            {
                choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
            }
        };
        Refresh();
        return SubsyCard(new StackPanel { Children = { heading, count, filters, listArea, footer } });
    }

    private DataTemplate AppCatalogTemplate()
    {
        var stack = new FrameworkElementFactory(typeof(StackPanel));
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(WindowsPackages.Package.Name)));
        name.SetValue(TextBlock.ForegroundProperty, ConsoleInk);
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        var metadata = new FrameworkElementFactory(typeof(TextBlock));
        metadata.SetValue(TextBlock.ForegroundProperty, ConsoleMuted);
        metadata.SetValue(TextBlock.FontSizeProperty, 11d);
        metadata.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        metadata.SetBinding(TextBlock.TextProperty, new Binding(nameof(WindowsPackages.Package.CatalogMetadata)));
        stack.AppendChild(name);
        stack.AppendChild(metadata);
        return new DataTemplate { VisualTree = stack };
    }

    private Style AppCatalogRowStyle()
    {
        var surface = new FrameworkElementFactory(typeof(Border));
        surface.Name = "Row";
        surface.SetValue(Border.PaddingProperty, new Thickness(10, 5, 10, 5));
        surface.SetValue(Border.MarginProperty, new Thickness(2, 1, 2, 1));
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        surface.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        surface.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = surface };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, ConsolePanel, "Row"));
        template.Triggers.Add(hover);
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, ConsolePanel, "Row"));
        selected.Setters.Add(new Setter(Border.BorderBrushProperty, ConsoleAccent, "Row"));
        selected.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2, 0, 0, 0), "Row"));
        template.Triggers.Add(selected);
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(TemplateProperty, template));
        return style;
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
        bool remove = request == "SNOB_UNINSTALL";
        if (remove && argument.Equals("Edge", StringComparison.OrdinalIgnoreCase))
            argument = "Microsoft Edge";

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
        if (remove && matches.Length > 1) return "[✖] Найдено несколько программ. Укажи точный ID из каталога.";
        if (remove && (IsWindowsSecurity(argument) || match is not null &&
            (IsWindowsSecurity(match.Name) || IsWindowsSecurity(match.Id))))
            return "[✖] Это компонент безопасности Windows.";
        if (remove && match?.RemovalRestriction is { } restriction)
            return ShowSnobDetails("Удаление ограничено", $"{match.Name}\n{restriction}", showInstalledApps: true);
        if (remove && (match?.Id == YettaUninstallId || argument.Equals("Yetta", StringComparison.OrdinalIgnoreCase)))
            return "[ℹ] Для удаления Yetta используй snob yetta uninstall.";
        string? packageFullName = remove ? match?.PackageFullName : null;
        if (remove && !ApproveAction("Удалить программу", match?.Label ?? argument,
            packageFullName is null ? "WinGet запустит зарегистрированное удаление выбранного приложения."
            : "Windows удалит приложение и его локальные данные для текущего пользователя.", destructive: true)) return "";
        if (!check && !remove && !ApproveAction("Установить программу", argument,
            "WinGet найдёт пакет с этим точным именем или ID и запустит его установщик.")) return "";

        string verb = check ? "show" : remove ? "uninstall" : "install";
        string selector = remove && match is not null ? match.Selector : "--query";
        string target = remove && match is not null ? (selector == "--name" ? match.Name : match.Id) : argument;
        var result = packageFullName is not null
            ? await WindowsPackages.RemoveMsixAsync(packageFullName)
            : await WindowsPackages.WinGetAsync(verb, selector, target, _updateStop.Token);
        if (check) return ShowSnobDetails(result.Code == 0 ? "Сведения WinGet" : "WinGet · сведения недоступны", result.Output);
        if (result.Code == 0) return remove ? "[✔] Программа удалена." : "[✔] Программа установлена.";
        if (remove) return ShowSnobDetails("Удаление не завершилось", $"{match?.Name ?? argument}\nКод: 0x{result.Code:X8}\n{result.Output}",
            showInstalledApps: true);
        return ShowSnobDetails("WinGet · операция не завершилась", $"Код: 0x{result.Code:X8}\n{result.Output}");
    }

    private static bool IsWindowsSecurity(string name) =>
        name.Equals("Windows Defender", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Microsoft Defender Antivirus", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Microsoft.SecHealthUI", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Microsoft.Windows.SecHealthUI", StringComparison.OrdinalIgnoreCase);

    private string ShowSnobDetails(string title, string details, bool showInstalledApps = false)
    {
        AddConsoleEntry(new ConsoleEntry("", title + "\n" + details)
        {
            Render = () =>
            {
                var info = LiteralConsoleLine(details);
                TextSelection.Allow(info);
                var scroll = new ScrollViewer { Content = info, MaxHeight = 310, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                var content = new StackPanel { Children = { LiteralConsoleLine(title, ConsoleInk), scroll } };
                if (showInstalledApps)
                    content.Children.Add(SubsyButton("Открыть установленные приложения", OpenInstalledApps));
                return SubsyCard(content);
            }
        });
        return "";
    }

    private void OpenInstalledApps()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception ex)
        { Notify("Windows", "Не удалось открыть установленные приложения. " + ex.Message); }
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
