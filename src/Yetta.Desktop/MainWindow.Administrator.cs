using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls;
using Yetta.Core;

namespace Yetta.Desktop;

public partial class MainWindow
{
    private DaxAdministratorState _administratorState;
    private bool _administratorBusy;
    private bool AdministratorActive => _administratorState == DaxAdministratorState.Active;

    private async Task WatchAdministratorAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            await RefreshAdministratorAsync();
            while (await timer.WaitForNextTickAsync(_updateStop.Token))
                await RefreshAdministratorAsync();
        }
        catch (OperationCanceledException) when (_updateStop.IsCancellationRequested) { }
    }

    private async Task RefreshAdministratorAsync()
    {
        if (_administratorBusy || _updateStop.IsCancellationRequested) return;
        _administratorBusy = true;
        try
        {
            var state = await DaxAdministratorClient.RequestAsync(DaxAdministratorOperation.Status, _updateStop.Token);
            UpdateAdministratorState(state);
        }
        catch (OperationCanceledException) when (_updateStop.IsCancellationRequested) { }
        finally { _administratorBusy = false; }
    }

    private void UpdateAdministratorState(DaxAdministratorState state, bool redraw = true)
    {
        bool changed = _administratorState != state;
        _administratorState = state;
        AccessProfiles.SyncAdministrator(_settings, state);
        NavDevelopers.Visibility = AdministratorActive ? Visibility.Visible : Visibility.Collapsed;
        if (!AdministratorActive && _page == "developers")
            Navigate("guard");
        else if (redraw && changed && _page is "guard" or "info" or "overview")
            Navigate(_page);
    }

    private string? _previewProfile;

    private static string Requirement(string profile) => profile switch
    {
        "Guest" => "Доступен сразу после запуска Yetta.",
        "User" => "Требуется текущая учётная запись Windows.",
        "Administrator" => "При первом подключении требуется согласие администратора Windows.",
        _ => "Системный брокер пока недоступен."
    };

    private StackPanel ProfileDetails(string profile)
    {
        var heading = new DockPanel { Margin = new Thickness(0, 10, 0, 16) };
        var icon = IconAssets.Asset("Profiles/" + profile + ".png", 46);
        icon.Margin = new Thickness(0, 0, 14, 0);
        heading.Children.Add(icon);
        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Text(AccessProfiles.DisplayName(profile), 21, heading: true));
        title.Children.Add(Text(profile == _settings.ActiveProfile ? "Активный профиль" :
            _settings.InstalledProfiles.Contains(profile) ? "Установлен" : "Не установлен", 12, muted: true));
        heading.Children.Add(title);
        var panel = new StackPanel { Children = { heading } };
        panel.Children.Add(Text("Возможности", 13, heading: true));
        panel.Children.Add(Text(profile == "Administrator"
            ? "Вкладка «Для разработчиков» и запуск локальных Python-скриптов в Marvine. Привилегированные операции проверяет служба DAX."
            : AccessProfiles.Description(profile), 13));
        var req = Text("Требования", 13, heading: true);
        req.Margin = new Thickness(0, 12, 0, 0);
        panel.Children.Add(req);
        panel.Children.Add(Text(Requirement(profile), 13, muted: true));
        return panel;
    }

    private void ShowAccessInfo()
    {
        var panel = ProfileDetails(_settings.ActiveProfile);
        panel.Margin = new Thickness(24);
        var info = new Window
        {
            Title = "Уровень доступа", Owner = this, Background = Surface, Content = panel,
            Width = 430, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
        };
        var link = ActionButton("Открыть DAX Guard →", () => { info.Close(); Navigate("guard"); });
        link.Margin = new Thickness(0, 20, 0, 0);
        panel.Children.Add(link);
        info.ShowDialog();
    }

    private void BuildProfilePicker()
    {
        var panel = new StackPanel();
        var rail = new UniformGrid { Rows = 1 };
        var slide = new TranslateTransform();
        var highlight = new Border { CornerRadius = new CornerRadius(20), Background = Resource("SelectedBrush"),
            HorizontalAlignment = HorizontalAlignment.Left, RenderTransform = slide, IsHitTestVisible = false };
        var track = new Grid { Children = { highlight, rail } };
        panel.Children.Add(new Border { Background = Resource("HoverBackground"), CornerRadius = new CornerRadius(24),
            Padding = new Thickness(4), Child = track });
        var details = new ContentControl { Margin = new Thickness(8, 12, 8, 4) };
        panel.Children.Add(details);
        void Move(bool animate)
        {
            double width = track.ActualWidth / AccessProfiles.All.Length;
            highlight.Width = width;
            double to = Array.IndexOf(AccessProfiles.All, _previewProfile) * width;
            double from = slide.X;
            slide.BeginAnimation(TranslateTransform.XProperty, null);
            slide.X = to;
            if (animate && UiMotion.AnimationsEnabled)
                slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(170))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        }
        foreach (string item in AccessProfiles.All)
        {
            var tab = new Button { Style = (Style)FindResource("FlatTile"), MinHeight = 36,
                Content = new Border { Background = Brushes.Transparent, Padding = new Thickness(3, 8, 3, 8),
                    Child = new TextBlock { Text = AccessProfiles.DisplayName(item), Foreground = Ink,
                        TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap } } };
            tab.Click += (_, _) => Select(item);
            rail.Children.Add(tab);
        }
        track.SizeChanged += (_, _) => Move(false);
        void Select(string profile)
        {
            _previewProfile = profile;
            Move(true);
            var body = ProfileDetails(profile);
            bool active = profile == _settings.ActiveProfile;
            bool needsInstall = profile == "Administrator" && _administratorState is not
                (DaxAdministratorState.Active or DaxAdministratorState.Inactive);
            var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
            Button? apply = null;
            apply = ActionButton(active ? "Активен" : profile == "System" ? "В разработке" :
                needsInstall ? "Установить и включить" : "Выбрать профиль", async () =>
            {
                if (_administratorBusy) return;
                apply!.IsEnabled = false;
                apply.Content = "Подготовка…";
                try
                {
                    if (profile == "Administrator" && needsInstall && !await InstallAdministratorAsync()) return;
                    await SelectAccessProfileAsync(profile);
                }
                finally { if (_page == "guard" && details.IsLoaded) Select(profile); }
            }, true, !active && profile != "System");
            apply.Margin = new Thickness(0, 0, 10, 0);
            actions.Children.Add(apply);
            if (profile == "Administrator" && _administratorState is DaxAdministratorState.Active or DaxAdministratorState.Inactive)
                actions.Children.Add(ActionButton("Отозвать доступ", async () =>
                {
                    if (ConfirmDaxAction("Отозвать Administrator", "Для восстановления доступа потребуется согласие администратора Windows."))
                        await ChangeAdministratorAsync(DaxAdministratorOperation.Revoke);
                }));
            body.Children.Add(actions);
            details.Content = body;
            UiMotion.AnimatePage(body);
        }
        Select(_previewProfile ?? _settings.ActiveProfile);
        ContentPanel.Children.Add(Card(panel));
    }

    private async Task<bool> InstallAdministratorAsync()
    {
        if (_administratorBusy) return false;
        _administratorBusy = true;
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "YettaDaxAdministratorSetup.exe");
            string? expected = typeof(MainWindow).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "DaxInstallerSha256")?.Value;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(file, _updateStop.Token));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Контрольная сумма установщика DAX не совпала.");
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
            if (process is null) return false;
            await process.WaitForExitAsync(_updateStop.Token);
            if (process.ExitCode != 0) { Notify("DAX Guard", "Установка профиля не завершена."); return false; }
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { Notify("DAX Guard", "Установка отменена."); return false; }
        catch (OperationCanceledException) when (_updateStop.IsCancellationRequested) { return false; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { ShowDaxNotice("Не удалось установить профиль", ex.Message); return false; }
        finally { _administratorBusy = false; }
    }

    private async Task SelectAccessProfileAsync(string profile)
    {
        if (_administratorBusy) return;
        if (profile == "Administrator")
        {
            await ChangeAdministratorAsync(DaxAdministratorOperation.Enable);
            return;
        }
        if (!AccessProfiles.CanInstallLogicalProfile(profile))
        {
            ShowDaxNotice("Недоступный уровень доступа", "Профиль System пока недоступен.");
            return;
        }
        if (AdministratorActive && !await ChangeAdministratorAsync(DaxAdministratorOperation.Disable))
            return;
        AccessProfiles.Install(_settings, profile);
        AccessProfiles.Apply(_settings, profile);
        SavePreferences();
        Navigate("guard");
    }

    private async Task<bool> ChangeAdministratorAsync(DaxAdministratorOperation operation)
    {
        if (_administratorBusy) return false;
        _administratorBusy = true;
        try
        {
            var expected = operation switch
            {
                DaxAdministratorOperation.Enable => DaxAdministratorState.Active,
                DaxAdministratorOperation.Disable => DaxAdministratorState.Inactive,
                _ => DaxAdministratorState.Revoked
            };
            var state = await DaxAdministratorClient.RequestAsync(operation, _updateStop.Token);
            UpdateAdministratorState(state, redraw: false);
            if (_page == "guard") Navigate("guard");
            if (state == expected)
            {
                SavePreferences();
                DaxAudit.Write("access", "administrator_" + state.ToString().ToLowerInvariant(), "DAX подтвердил изменение профиля.");
                return true;
            }
            ShowDaxNotice("DAX не подтвердил изменение", "Проверь состояние Administrator в DAX Guard.");
            return false;
        }
        catch (OperationCanceledException) when (_updateStop.IsCancellationRequested) { return false; }
        finally { _administratorBusy = false; }
    }
}
