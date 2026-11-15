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
    /// <summary>Cause icon for the main zone, following the numbered icon set.</summary>
    internal static int ObjectIcon(WarningRequest request)
    {
        if (request.Kind == WarningKind.Fatal) return 48;
        if (request.CauseIconId is >= 1 and <= 50) return request.CauseIconId.Value;
        var path = request.SourcePath;
        if (string.IsNullOrWhiteSpace(path))
            return string.IsNullOrWhiteSpace(request.Publisher) ? 46 : 45;
        try
        {
            if (Directory.Exists(path)) return 43;
            var full = Path.GetFullPath(path);
            if (File.Exists(full))
            {
                var attributes = File.GetAttributes(full);
                if (attributes.HasFlag(FileAttributes.System) && attributes.HasFlag(FileAttributes.ReadOnly)) return 40;
                if (IsUnderWindows(full)) return 42;
            }
            else if (IsUnderWindows(full))
            {
                return 42;
            }
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException
            or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            // Fall through to the extension table.
        }
        return Extension(path) switch
        {
            ".py" => 37,
            ".txt" or ".doc" or ".docx" or ".odt" or ".rtf" or ".pdf" or ".md" or ".log" or ".csv" => 39,
            ".exe" or ".com" or ".bat" or ".cmd" or ".ps1" or ".msi" or ".vbs" or ".jar" or ".scr" => 41,
            ".sys" or ".dll" or ".ini" or ".cfg" or ".reg" or ".drv" or ".ocx" => 42,
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".bz2" or ".xz" or ".iso" => 44,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" or ".ico"
                or ".mp4" or ".mkv" or ".avi" or ".mov" or ".mp3" or ".wav" => 47,
            ".lnk" => 38,
            _ => 46
        };
    }

    /// <summary>Status square for the main zone; 0 means "no square".</summary>
    internal static int Status(WarningRequest request, string activeProfile)
    {
        // Status communicates safety only when a user must decide or acknowledge.
        if (request.Kind is WarningKind.Fatal or WarningKind.Information) return 0;
        if (activeProfile is "Guest" or "SafeUser" || ConsentDialog.InWindowsSafeMode) return 35;
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
