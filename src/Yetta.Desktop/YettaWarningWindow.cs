using System.IO;
using System.Windows;

namespace Yetta.Desktop;

internal enum WarningKind { Standard, Caution, Dangerous, Critical, Fatal, Agreement, Dax }

// The upper illustration represents the EVENT, independently of warning severity.
internal enum WarningHeaderKind
{
    Generic, AccessDenied, WindowsAndFilesRisk, WindowsFilesRisk,
    UnverifiedSecurity, DaxBlocked, Information, Agreement, Fatal
}

internal enum WarningObjectKind
{
    Auto, Windows, Python, ImportantFile, TextFile, ProtectedFile,
    Executable, SystemFile, CloudFile, Folder, Archive, Application,
    FatalSkull, UnknownFile, MediaFile, WindowsServer, Dax
}

// A red status must come from a DAX assessment, not from the warning title.
internal enum WarningSafetyAssessment { Unknown, VerifiedSafe, PotentialRisk }

internal sealed record WarningRequest(
    WarningKind Kind,
    string Reason,
    string Description,
    string Subject = "Yetta",
    string? SourcePath = null,
    string? Publisher = null,
    bool RequireAcknowledgement = false,
    bool RequireTty = false,
    bool PotentialDamage = false,
    Action? RecoveryNavigation = null,
    int? HeaderIconId = null,
    WarningHeaderKind HeaderKind = WarningHeaderKind.Generic,
    WarningObjectKind ObjectKind = WarningObjectKind.Auto,
    WarningSafetyAssessment SafetyAssessment = WarningSafetyAssessment.Unknown,
    bool InformationOnly = false);

/// <summary>
/// Policy and icon routing for the seven Yetta warning types. A UI colour or
/// status is never an authorization decision or a substitute for DAX/Windows.
/// </summary>
internal static class YettaWarningWindow
{
    internal static int HeaderIcon(WarningRequest request)
    {
        if (request.Kind == WarningKind.Fatal) return 48;
        if (request.Kind == WarningKind.Agreement) return 2;
        if (request.InformationOnly || request.HeaderKind == WarningHeaderKind.Information)
            return 31;
        if (request.HeaderIconId is >= 1 and <= 50) return request.HeaderIconId.Value;
        return request.HeaderKind switch
        {
            WarningHeaderKind.AccessDenied => 28,
            WarningHeaderKind.WindowsAndFilesRisk => 24,
            WarningHeaderKind.WindowsFilesRisk => 25,
            WarningHeaderKind.UnverifiedSecurity => 23,
            WarningHeaderKind.DaxBlocked => 33,
            WarningHeaderKind.Information => 31,
            WarningHeaderKind.Agreement => 2,
            WarningHeaderKind.Fatal => 48,
            _ => request.Kind == WarningKind.Dax ? 31 : 22
        };
    }

    internal static WarningObjectKind ResolveObjectKind(WarningRequest request)
    {
        if (request.Kind == WarningKind.Fatal) return WarningObjectKind.FatalSkull;
        if (request.ObjectKind != WarningObjectKind.Auto) return request.ObjectKind;
        if (request.Kind == WarningKind.Dax || request.Subject.Equals("DAX", StringComparison.OrdinalIgnoreCase))
            return WarningObjectKind.Dax;
        if (request.Subject.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            return WarningObjectKind.Windows;
        if (request.Subject.StartsWith("Yetta", StringComparison.OrdinalIgnoreCase))
            return WarningObjectKind.Application;

        string? path = request.SourcePath;
        if (string.IsNullOrWhiteSpace(path)) return WarningObjectKind.UnknownFile;
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            (uri.Scheme is "http" or "https"))
            return WarningObjectKind.CloudFile;
        if (Directory.Exists(path)) return WarningObjectKind.Folder;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".py" => WarningObjectKind.Python,
            ".txt" or ".md" or ".rtf" or ".doc" or ".docx" or ".pdf"
                => WarningObjectKind.TextFile,
            ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1"
                => WarningObjectKind.Executable,
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".cab"
                => WarningObjectKind.Archive,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp"
                or ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv"
                => WarningObjectKind.MediaFile,
            ".dll" or ".sys" or ".ini" or ".reg" or ".cfg" or ".config"
                => WarningObjectKind.SystemFile,
            ".p7m" or ".p7s" or ".enc" => WarningObjectKind.ProtectedFile,
            ".appx" or ".msix" => WarningObjectKind.Application,
            _ => WarningObjectKind.UnknownFile
        };
    }

    // 0=no status, 16=verified safe, 17=potential risk found, 18=safe mode/
    // Guest/SafeUser/fatal. These are NEW warning artwork numbers, not the
    // previous 32px atlas IDs.
    internal static int StatusArtwork(WarningRequest request, string activeProfile)
    {
        // An informational board does not propose an action, so no status
        // graphic or background square is rendered at all.
        if (request.InformationOnly || request.Kind == WarningKind.Dax)
            return 0;
        if (request.Kind == WarningKind.Fatal ||
            activeProfile is "Guest" or "SafeUser" ||
            ConsentDialog.InWindowsSafeMode)
            return 18;
        if (request.SafetyAssessment == WarningSafetyAssessment.PotentialRisk ||
            request.PotentialDamage) // Set by an actual DAX classifier only.
            return 17;
        if (request.SafetyAssessment == WarningSafetyAssessment.VerifiedSafe)
            return 16;
        return 0; // No verified result: do not advertise safety or danger.
    }

    internal static bool Show(Window owner, WarningRequest request,
        string activeProfile = "User", bool nonModal = false) =>
        StandardWarningDialog.Show(owner, request, activeProfile, nonModal);
}
