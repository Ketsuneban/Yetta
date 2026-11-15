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

    // Administrator is persisted as an installed choice only after a successful
    // broker connection. This method is NOT a security check; the broker still
    // authenticates the Windows SID and allowlists every privileged operation.
    public static bool CanPersistProfile(string profile) =>
        CanInstallLogicalProfile(profile) || profile == "Administrator";

    public static bool RecordVerifiedAdministrator(AppSettings settings, bool verified)
    {
        if (!verified) return false;
        settings.Normalize();
        if (!settings.InstalledProfiles.Contains("Administrator", StringComparer.Ordinal))
            settings.InstalledProfiles.Add("Administrator");
        settings.ActiveProfile = "Administrator";
        return true;
    }


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
        if (profile is "Administrator" or "System") return false;
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
        "Administrator" => "Защищённая Windows-служба выполняет только ограниченные административные операции Yetta; требуется разовая установка с UAC. Обычная консоль и сторонние программы не повышаются.",
        "System" => "Запланированный отдельный защищённый системный брокер с TTY. Не реализовано.",
        _ => "Только интерфейс Yetta, локальные настройки и ограниченная Mavrine. Системные изменения и запуск оболочек недоступны."
    };

    public static bool CanLaunchUnprivilegedTerminal(AppSettings settings)
    {
        settings.Normalize();
        return settings.ActiveProfile == "User";
    }

    public static bool CanLaunchElevatedShell(AppSettings settings) => false;

    // Broker availability is checked by the desktop IPC client at runtime.
    // This Core library cannot prove that an OS service exists or is authorized.
    public static bool HasPrivilegedBroker => false;
}
