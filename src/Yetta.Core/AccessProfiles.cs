namespace Yetta.Core;

/// <summary>
/// Yetta's user-selected capability policy. This class cannot grant OS rights.
/// Every privileged action must also check the actual Windows token.
/// </summary>
public static class AccessProfiles
{
    public static readonly string[] All = ["Guest", "User", "Administrator", "System"];

    public static bool CanInstallLogicalProfile(string profile) =>
        profile is "Guest" or "User";

    public static void SyncAdministrator(AppSettings settings, DaxAdministratorState state)
    {
        settings.Normalize();
        bool installed = state is DaxAdministratorState.Active or DaxAdministratorState.Inactive;
        if (installed && !settings.InstalledProfiles.Contains("Administrator"))
            settings.InstalledProfiles.Add("Administrator");
        if (!installed) settings.InstalledProfiles.Remove("Administrator");
        if (state == DaxAdministratorState.Active)
            settings.ActiveProfile = "Administrator";
        else if (settings.ActiveProfile == "Administrator")
            settings.ActiveProfile = settings.InstalledProfiles.Contains("User") ? "User" : "Guest";
    }

    public static bool Install(AppSettings settings, string profile)
    {
        settings.Normalize();
        if (!CanInstallLogicalProfile(profile)) return false;
        if (!CanInstallLogicalProfile(profile) ||
            !settings.InstalledProfiles.Contains(profile, StringComparer.Ordinal))
            settings.InstalledProfiles.Add(profile);
        settings.Normalize();
        return true;
    }

    public static bool Apply(AppSettings settings, string profile)
    {
        settings.Normalize();
        if (!CanInstallLogicalProfile(profile) ||
            !settings.InstalledProfiles.Contains(profile, StringComparer.Ordinal))
            return false;
        settings.ActiveProfile = profile;
        return true;
    }

    public static string DisplayName(string profile) => profile switch
    {
        "User" => TallowText.Get("ui.main.0058"),
        "Administrator" => TallowText.Get("core.access.0002"),
        "System" => TallowText.Get("core.access.0003"),
        _ => TallowText.Get("core.access.0004")
    };

    public static string Description(string profile) => profile switch
    {
        "User" => TallowText.Get("core.access.0006"),
        "Administrator" => "Профиль управляется службой DAX и доступен после подтверждения Windows.",
        "System" => TallowText.Get("core.access.0008"),
        _ => TallowText.Get("core.access.0009")
    };
}
