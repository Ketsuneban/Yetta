using System.Windows;
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
        else if (redraw && changed && _page is "guard" or "info")
            Navigate(_page);
    }

    private void BuildAdministratorCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(Text("Administrator", 15, heading: true));
        panel.Children.Add(Text(_administratorState switch
        {
            DaxAdministratorState.Active => "Активен для этой учётной записи Windows.",
            DaxAdministratorState.Inactive => "Установлен для этой учётной записи. Профиль выключен.",
            DaxAdministratorState.Revoked => "Доступ отозван. Для повторного предоставления запусти установщик DAX.",
            DaxAdministratorState.NotInstalled => "Установи DAX Administrator отдельным пакетом. Windows запросит разрешение.",
            DaxAdministratorState.NotGranted => "Этой учётной записи доступ не предоставлен.",
            _ => "Нет подтверждения от службы DAX. Доступ выключен."
        }, 12, muted: true));

        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        if (_administratorState == DaxAdministratorState.Inactive)
            actions.Children.Add(ActionButton("Включить", async () => await SelectAccessProfileAsync("Administrator"), true));
        if (_administratorState == DaxAdministratorState.Active)
            actions.Children.Add(ActionButton("Выключить", async () => await SelectAccessProfileAsync(
                _settings.InstalledProfiles.Contains("User") ? "User" : "Guest")));
        if (_administratorState is DaxAdministratorState.Active or DaxAdministratorState.Inactive)
            actions.Children.Add(ActionButton("Отозвать доступ", async () =>
            {
                if (!_administratorBusy && ConfirmDaxAction("Отозвать Administrator", "Чтобы вернуть доступ, снова запусти установщик DAX."))
                    await ChangeAdministratorAsync(DaxAdministratorOperation.Revoke);
            }));
        else
            actions.Children.Add(ActionButton("Установщик DAX", () => OpenExternal(
                "https://github.com/Ketsuneban/Yetta/releases/tag/v0.5.0-preview", "DAX Administrator · YettaDaxAdministratorSetup.exe"), true));
        actions.Children.Add(ActionButton("Проверить статус", async () => await RefreshAdministratorAsync()));
        panel.Children.Add(actions);
        ContentPanel.Children.Add(Card(panel));
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
