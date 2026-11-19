using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Yetta.Core;

namespace Yetta.Desktop;

public partial class MainWindow
{
    private const string PersonalizationDenied = "[✖] Для изменения настроек Windows выбери профиль Пользователь или Администратор в DAX Guard.";
    private bool CanCustomizeWindows => _settings.ActiveProfile == "User" || AdministratorActive;

    private string ApplyWindowsPreference(WindowsPreference preference, int? desired)
    {
        if (desired is not null && !CanCustomizeWindows) return PersonalizationDenied;
        try
        {
            int before = WindowsPersonalization.Read(preference.Key);
            if (desired is null || desired == before) return $"{preference.Label} · {preference.Format(before)}";
            if (!ConfirmYettaAction(preference.Label, $"Текущее значение: {preference.Format(before)}. Новое: {preference.Format(desired.Value)}."))
                return "[ℹ] Изменение отменено.";
            if (!CanCustomizeWindows) return PersonalizationDenied;
            WindowsPersonalization.Write(preference.Key, desired.Value);
            int actual = WindowsPersonalization.Read(preference.Key);
            DaxAudit.Write("personalization", "write", preference.Key);
            return $"{preference.Label} · {preference.Format(actual)}\nВернуть прежнее значение: win {preference.Key} {preference.Format(before)}";
        }
        catch (Win32Exception ex) { return "[✖] Windows: " + ex.Message; }
    }

    private string SetDesktopWallpaper(string? argument)
    {
        if (!CanCustomizeWindows) return PersonalizationDenied;
        if (argument is null)
        {
            var picker = new OpenFileDialog { Title = "Обои рабочего стола", Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp" };
            if (picker.ShowDialog(this) != true) return "[ℹ] Выбор обоев отменён.";
            argument = picker.FileName;
        }
        var file = Path.GetFullPath(argument);
        if (!File.Exists(file) || Path.GetExtension(file).ToLowerInvariant() is not (".bmp" or ".png" or ".jpg" or ".jpeg"))
            return "[⚠] Укажи существующее изображение JPG, PNG или BMP.";
        if (!ConfirmYettaAction("Заменить обои на всех мониторах?", file)) return "[ℹ] Изменение отменено.";
        if (!CanCustomizeWindows) return PersonalizationDenied;
        try { WindowsPersonalization.SetWallpaper(file); }
        catch (COMException ex) { return "[✖] Windows не применила обои. " + ex.Message; }
        DaxAudit.Write("personalization", "write", "wallpaper");
        return "[✔] Обои установлены: " + Path.GetFileName(file);
    }

    private static string OpenWindowsPage(string? page)
    {
        if (page is null || !WindowsCommands.Pages.Values.Contains(page)) return "[⚠] Неизвестный раздел Windows.";
        try
        {
            using var process = Process.Start(new ProcessStartInfo("ms-settings:" + page) { UseShellExecute = true });
            return "[✔] Открыты параметры Windows.";
        }
        catch (Win32Exception ex) { return "[✖] Windows не открыла раздел. " + ex.Message; }
    }
}
