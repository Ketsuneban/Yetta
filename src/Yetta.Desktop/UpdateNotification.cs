using System.Globalization;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;
namespace Yetta.Desktop;

internal sealed class UpdateNotification
{
    internal const string Template = "<toast duration=\"long\"><visual><binding template=\"ToastGeneric\"><text>Обновление Yetta</text><progress title=\"{title}\" value=\"{value}\" valueStringOverride=\"{percent}\" status=\"{status}\"/></binding></visual><actions><action content=\"Отменить загрузку\" arguments=\"action=cancel-update\" activationType=\"background\"/></actions></toast>";
    private readonly object _sync = new();
    private double _lastValue;
    private readonly string _title;
    private uint _sequence;
    private bool _shown;
    internal UpdateNotification(string version) => _title = "Версия " + version;
    private NotificationData Data(double value, string status) => new(new Dictionary<string, string>
    {
        ["title"] = _title, ["value"] = Math.Clamp(value, 0, 1).ToString(CultureInfo.InvariantCulture),
        ["percent"] = Math.Round(value * 100).ToString(CultureInfo.InvariantCulture) + "%", ["status"] = status
    }, ++_sequence);
    internal void Show()
    {
        lock (_sync) ShowCore();
    }
    private void ShowCore()
    {
        try
        {
            var xml = new Windows.Data.Xml.Dom.XmlDocument(); xml.LoadXml(Template);
            var toast = new ToastNotification(xml) { Tag = "yetta-update", Group = "updates", Data = Data(_lastValue, "Скачивание…") };
            toast.Failed += (_, error) => DaxAudit.Write("update", "toast_failed", error.ErrorCode.ToString());
            var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
            if (notifier.Setting != NotificationSetting.Enabled)
                DaxAudit.Write("update", "toast_disabled", notifier.Setting.ToString());
            notifier.Show(toast); _shown = true;
        }
        catch (Exception ex) { DaxAudit.Write("update", "toast_unavailable", ex.GetType().Name); }
    }
    internal void Update(double value, string status)
    {
        lock (_sync) UpdateCore(value, status);
    }
    private void UpdateCore(double value, string status)
    {
        if (!_shown) return;
        value = _lastValue = Math.Max(value, _lastValue);
        try
        {
            var result = ToastNotificationManagerCompat.CreateToastNotifier().Update(Data(value, status), "yetta-update", "updates");
            if (result == NotificationUpdateResult.NotificationNotFound) { Show(); }
        }
        catch (Exception ex) { DaxAudit.Write("update", "toast_progress_failed", ex.GetType().Name); }
    }
}
