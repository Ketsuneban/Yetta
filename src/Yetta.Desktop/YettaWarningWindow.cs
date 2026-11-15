using System.IO;
using System.Windows;

namespace Yetta.Desktop;

internal enum WarningKind { Standard, Caution, Dangerous, Critical, Fatal, Agreement, Dax }

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
    int? HeaderIconId = null);

/// <summary>
/// Central policy dispatch for all seven Yetta warnings. UI styling never grants
/// Windows privileges, changes UAC, or skips the DAX confirmation requirements.
/// </summary>
internal static class YettaWarningWindow
{
    internal static int ObjectIcon(WarningRequest request)
    {
        if (request.Kind == WarningKind.Fatal) return 13; // skull: cause, not header
        if (request.Kind == WarningKind.Agreement) return 12; // Yetta application
        if (request.Kind == WarningKind.Dax) return 29; // DAX shield from existing atlas
        var path = request.SourcePath;
        if (string.IsNullOrWhiteSpace(path)) return 14;
        if (path.StartsWith(@"\\\\", StringComparison.Ordinal)) return 19; // Windows server-side resource
        if (Directory.Exists(path)) return 10;
        var full = Path.GetFullPath(path);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), windows.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return 1; // the Windows operating system as a whole
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".py" => 2,
            ".txt" or ".doc" or ".docx" or ".pdf" or ".rtf" or ".md" => 4,
            ".exe" or ".com" or ".bat" or ".cmd" or ".ps1" or ".msi" => 7,
            ".sys" or ".dll" or ".ini" or ".cfg" or ".reg" => 8,
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => 11,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" or ".mp4" or ".mkv" or ".avi" or ".mov" => 15,
            ".lnk" or ".appx" or ".msix" => 12,
            _ => 14
        };
    }

    internal static int Status(WarningRequest request, string activeProfile)
    {
        // Status communicates safety only when a user must decide or acknowledge.
        if (request.Kind == WarningKind.Information || request.Kind == WarningKind.Dax)
            return 0;
        if (request.Kind == WarningKind.Fatal) return 18;
        if (activeProfile is "Guest" or "SafeUser" || ConsentDialog.InWindowsSafeMode) return 18;
        return request.PotentialDamage ? 17 : 16;
    }

    internal static int HeaderIcon(WarningRequest request) =>
        request.HeaderIconId is >= 1 and <= 50 ? request.HeaderIconId.Value : request.Kind switch
        {
            WarningKind.Fatal => 48,
            WarningKind.Agreement => 2,
            WarningKind.Dax => 33,
            WarningKind.Dangerous or WarningKind.Critical => 24,
            WarningKind.Caution => 23,
            _ => 22
        };

    internal static bool Show(Window owner, WarningRequest request,
        string activeProfile = "User", bool nonModal = false) =>
        StandardWarningDialog.Show(owner, request, activeProfile, nonModal);
}
