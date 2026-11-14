namespace Yetta.Core;

/// <summary>Local Yetta-only preferences and logical (not OS) access profiles.</summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Fluent";
    public bool ConfirmExternalLinks { get; set; } = true;
    public List<string> InstalledProfiles { get; set; } = ["Guest"];
    public string ActiveProfile { get; set; } = "Guest";

    public void Normalize()
    {
        if (Theme is not ("Fluent" or "Glass")) Theme = "Fluent";
        InstalledProfiles ??= [];
        InstalledProfiles = InstalledProfiles
            .Where(p => AccessProfiles.CanInstallLogicalProfile(p))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (!InstalledProfiles.Contains("Guest", StringComparer.Ordinal))
            InstalledProfiles.Insert(0, "Guest");
        if (!InstalledProfiles.Contains(ActiveProfile, StringComparer.Ordinal))
            ActiveProfile = "Guest";
    }
}
