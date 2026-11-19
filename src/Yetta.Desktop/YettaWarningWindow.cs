using Yetta.Core;
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
    WarningCauseType CauseType = WarningCauseType.Auto,
    bool IsPreview = false,
    bool RequirePassword = false,
    string? TechnicalDetails = null,
    IReadOnlyList<string>? RelatedPaths = null);

/// <summary>
/// Central policy dispatch for all seven Yetta warnings. UI styling never grants
/// Windows privileges, changes UAC, or skips the DAX confirmation requirements.
/// </summary>
internal static class YettaWarningWindow
{
    /// <summary>Cause and severity are independent, including fatal and DAX warnings.</summary>
    internal static int ObjectIcon(WarningRequest request) => WarningCauseResolver.Icon(request);

    /// <summary>Status square for the main zone; 0 means "no square".</summary>
    internal static int Status(WarningRequest request, string activeProfile)
    {
        // Status communicates safety only when a user must decide or acknowledge.
        if (request.Kind is WarningKind.Fatal or WarningKind.Information) return 0;
        if (activeProfile == "Guest" || ConsentDialog.InWindowsSafeMode) return 35;
        return request.PotentialDamage ? 26 : 27;
    }

    /// <summary>Header icon: what the warning is about, not how severe it is.</summary>
    internal static int HeaderIcon(WarningRequest request)
    {
        if (request.HeaderIconId is >= 1 and <= 50) return request.HeaderIconId.Value;
        return request.Kind switch
        {
            WarningKind.Fatal => 48,
            WarningKind.Agreement => 2,
            WarningKind.Dax => 22,
            WarningKind.Caution => 23,
            WarningKind.Dangerous or WarningKind.Critical => WindowsOnlyDanger(request) ? 25 : 24,
            _ => 22
        };
    }

    private static bool WindowsOnlyDanger(WarningRequest request)
    {
        var path = request.SourcePath;
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            return IsUnderWindows(Path.GetFullPath(path));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException
            or PathTooLongException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool IsUnderWindows(string fullPath)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrEmpty(windows)) return false;
        var root = windows.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string Extension(string path)
    {
        var extension = Path.GetExtension(path);
        return string.IsNullOrEmpty(extension) ? string.Empty : extension.ToLowerInvariant();
    }

    internal static bool Show(Window owner, WarningRequest request,
        string activeProfile = "User", bool nonModal = false) =>
        TallowWarningDispatcher.Show(owner, request, activeProfile, nonModal);
}
