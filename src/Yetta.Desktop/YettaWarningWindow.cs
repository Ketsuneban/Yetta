using System.IO;
using System.Windows;

namespace Yetta.Desktop;

internal enum WarningKind { Standard, Information, Caution, Dangerous, Critical, Fatal, Agreement, Dax }

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
    int? CauseIconId = null);

/// <summary>
/// Central policy dispatch for all seven Yetta warnings. UI styling never grants
/// Windows privileges, changes UAC, or skips the DAX confirmation requirements.
/// </summary>
internal static class YettaWarningWindow
{
    internal static int ObjectIcon(WarningRequest request)
    {
        if (request.CauseIconId is >= 1 and <= 19) return request.CauseIconId.Value;
        if (request.Kind == WarningKind.Fatal) return 48; // skull/application cause icon; header remains 48 as separately selected
        if (request.Kind == WarningKind.Agreement) return 12; // Yetta application
        if (request.Kind == WarningKind.Dax) return 29; // DAX shield
        var path = request.SourcePath;
        if (string.IsNullOrWhiteSpace(path)) return 46;
        if (path.StartsWith(@"\\\\", StringComparison.Ordinal)) return 4; // Windows network/server source // Windows server-side resource
        if (Directory.Exists(path)) return 10;
        var full = Path.GetFullPath(path);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), windows.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return 4; // Windows logo
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".py" => 37,
            ".txt" or ".doc" or ".docx" or ".pdf" or ".rtf" or ".md" => 39,
            ".exe" or ".com" or ".bat" or ".cmd" or ".ps1" or ".msi" => 41,
            ".sys" or ".dll" or ".ini" or ".cfg" or ".reg" => 42,
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => 44,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" or ".mp4" or ".mkv" or ".avi" or ".mov" => 47,
            ".lnk" or ".appx" or ".msix" => 48,
            _ => 46
        };
    }

    internal static int Status(WarningRequest request, string activeProfile)
    {
        // Status communicates safety only when a user must decide or acknowledge.
        if (request.Kind is WarningKind.Information or WarningKind.Dax)
            return 0;
        if (request.Kind == WarningKind.Fatal) return 18;
        if (activeProfile is "Guest" or "SafeUser" || ConsentDialog.InWindowsSafeMode) return 18;
        return request.PotentialDamage ? 26 : 27;
    }

    internal static int HeaderIcon(WarningRequest request) =>
        request.HeaderIconId is >= 1 and <= 50 ? request.HeaderIconId.Value : request.Kind switch
        {
            WarningKind.Information => 30,
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
