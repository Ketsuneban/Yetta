namespace Yetta.Core;

/// <summary>
/// Yetta's user-selected capability policy. This class cannot grant OS rights.
/// Administrator/System require a future separately audited Windows broker.
/// </summary>
public static class AccessProfiles
{
    public static readonly string[] All = ["Guest", "SafeUser", "User", "Administrator", "System"];

    public static bool CanInstallLogicalProfile(string profile) =>
        profile is "Guest" or "SafeUser" or "User";

    public static bool Install(AppSettings settings, string profile)
    {
        settings.Normalize();
        if (!CanInstallLogicalProfile(profile)) return false;
        if (!settings.InstalledProfiles.Contains(profile, StringComparer.Ordinal))
            settings.InstalledProfiles.Add(profile);
        settings.Normalize();
        return true;
    }

    public static bool Apply(AppSettings settings, string profile)
    {
        settings.Normalize();
        if (!settings.InstalledProfiles.Contains(profile, StringComparer.Ordinal))
            return false;
        settings.ActiveProfile = profile;
        return true;
    }

    public static string DisplayName(string profile) => profile switch
    {
        "SafeUser" => "Безопасный пользователь",
        "User" => "Пользователь",
        "Administrator" => "Администратор",
        "System" => "Система",
        _ => "Гость"
    };

    public static string Description(string profile) => profile switch
    {
        "SafeUser" => "Чтение разрешённых Windows данных без изменения файлов или настроек; не предоставляет доступ к защищённым ресурсам.",
        "User" => "Просмотр данных и запуск обычных программ с правами текущей учётной записи Windows. Никакого повышения прав.",
        "Administrator" => "Запланированное выполнение отдельных операций после настоящего UAC и проверки прав. Не реализовано.",
        "System" => "Запланированный отдельный защищённый системный брокер с TTY. Не реализовано.",
        _ => "Только интерфейс Yetta, локальные настройки и ограниченная Mavrine. Системные изменения и запуск оболочек недоступны."
    };

    public static bool CanLaunchUnprivilegedTerminal(AppSettings settings)
    {
        settings.Normalize();
        return settings.ActiveProfile == "User";
    }

    public static bool CanLaunchElevatedShell(AppSettings settings) => false;

    public static bool HasPrivilegedBroker => false;
}
