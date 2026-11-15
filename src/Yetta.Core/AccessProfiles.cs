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
        "SafeUser" => TallowText.Get("core.access.0001"),
        "User" => TallowText.Get("ui.main.0058"),
        "Administrator" => TallowText.Get("core.access.0002"),
        "System" => TallowText.Get("core.access.0003"),
        _ => TallowText.Get("core.access.0004")
    };

    public static string Description(string profile) => profile switch
    {
        "SafeUser" => TallowText.Get("core.access.0005"),
        "User" => TallowText.Get("core.access.0006"),
        "Administrator" => TallowText.Get("core.access.0007"),
        "System" => TallowText.Get("core.access.0008"),
        _ => TallowText.Get("core.access.0009")
    };

    public static bool CanLaunchUnprivilegedTerminal(AppSettings settings)
    {
        settings.Normalize();
        return settings.ActiveProfile == "User";
    }

    public static bool CanLaunchElevatedShell(AppSettings settings) => false;

    public static bool HasPrivilegedBroker => false;
}
