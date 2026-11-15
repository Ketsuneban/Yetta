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
        if (request.Kind == WarningKind.Fatal) return 48;
        if (request.Kind == WarningKind.Agreement) return 2;
        if (request.Kind == WarningKind.Dax) return 29;
        var path = request.SourcePath;
        if (string.IsNullOrWhiteSpace(path)) return 46;
        if (Directory.Exists(path)) return 43;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" => 47,
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => 44,
            ".txt" or ".doc" or ".docx" or ".pdf" or ".rtf" or ".md" => 39,
            ".py" => 37,
            ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" => 41,
            ".sys" or ".dll" => 42,
            _ => 46
        };
    }

    internal static int Status(WarningRequest request, string activeProfile)
    {
        if (request.Kind == WarningKind.Fatal) return 0;
        if (request.Kind is WarningKind.Agreement or WarningKind.Dax) return 35;
        if (activeProfile is "Guest" or "SafeUser" || ConsentDialog.InWindowsSafeMode) return 35;
        return request.PotentialDamage || request.Kind is WarningKind.Dangerous or WarningKind.Critical
            ? 26 : 27;
    }

    internal static bool Show(Window owner, WarningRequest request,
        string activeProfile = "User", bool nonModal = false) =>
        StandardWarningDialog.Show(owner, request, activeProfile, nonModal);
}
