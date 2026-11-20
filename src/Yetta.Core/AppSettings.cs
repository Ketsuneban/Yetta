namespace Yetta.Core;

/// <summary>Local Yetta-only preferences and logical (not OS) access profiles.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Fluent";
    public bool ConfirmExternalLinks { get; set; } = true;
    public bool NotificationSoundsEnabled { get; set; } = true;
    public bool AutoUpdatesEnabled { get; set; } = true;
    public int NotificationRetentionHours { get; set; } = 24;
    public List<string> InstalledProfiles { get; set; } = ["Guest"];
    public string ActiveProfile { get; set; } = "Guest";
    public bool MarvineSubsyEnabled { get; set; } = true;
    public string MarvineColorset { get; set; } = "EGA";
    public int MarvineHistoryLimit { get; set; } = 1000;
    public string? PythonExecutablePath { get; set; }

    public void Normalize()
    {
        Theme = "Fluent";
        if (NotificationRetentionHours is < 1 or > 720) NotificationRetentionHours = 24;
        if (MarvineColorset is not ("EGA" or "No Color" or "dracula")) MarvineColorset = "EGA";
        MarvineHistoryLimit = Math.Clamp(MarvineHistoryLimit, 100, 10000);
        InstalledProfiles ??= [];
        if (ActiveProfile == "SafeUser") ActiveProfile = "Guest";
        InstalledProfiles = InstalledProfiles
            .Where(p => AccessProfiles.CanInstallLogicalProfile(p) || p == "Administrator")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (!InstalledProfiles.Contains("Guest", StringComparer.Ordinal))
            InstalledProfiles.Insert(0, "Guest");
        if (!InstalledProfiles.Contains(ActiveProfile, StringComparer.Ordinal))
            ActiveProfile = "Guest";
    }
}
