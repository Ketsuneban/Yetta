namespace Yetta.Core;

public enum WarningCauseType { Auto, File, Application, Yetta, Dax, Tallow, Windows, Cloud, Folder }

/// <summary>Metadata only: classification never opens file content or executes the cause.</summary>
public sealed record WarningCauseFacts(string Path, bool IsDirectory = false, bool IsCloudOnly = false,
    bool IsProtected = false, bool IsWindowsConfiguration = false, bool IsImportant = false);

public static class WarningCause
{
    public static int Icon(WarningCauseType type, WarningCauseFacts? file = null)
    {
        if (type != WarningCauseType.File && type != WarningCauseType.Auto)
            return type switch
            {
                WarningCauseType.Application => 11, WarningCauseType.Tallow => 6,
                WarningCauseType.Windows => 7, WarningCauseType.Cloud => 8,
                WarningCauseType.Folder => 9, WarningCauseType.Yetta => 15,
                WarningCauseType.Dax => 16, _ => 12
            };
        if (file is null) return 12;
        if (file.IsCloudOnly) return 8;
        if (file.IsDirectory) return 9;
        if (file.IsWindowsConfiguration) return 14;
        if (file.IsProtected) return 4;
        if (file.IsImportant) return 2;
        // Handle Windows paths even in cross-platform tests.
        string name = file.Path.Replace('\\', '/').Split('/').Last();
        int dot = name.LastIndexOf('.');
        string extension = dot < 0 ? "" : name[dot..].ToLowerInvariant();
        return extension switch
        {
            ".py" or ".pyw" => 1,
            ".txt" or ".md" or ".log" or ".csv" or ".rtf" or ".doc" or ".docx" or ".odt" or ".pdf" => 3,
            ".exe" or ".com" or ".bat" or ".cmd" or ".ps1" or ".psm1" or ".msi" or ".vbs" or ".js" or ".wsf" or ".jar" or ".scr" => 5,
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".bz2" or ".xz" or ".iso" or ".cab" => 10,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" or ".ico" or ".heic" or ".tif" or ".tiff"
                or ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".wmv" or ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" or ".aac" => 13,
            ".reg" => 14,
            _ => 12
        };
    }
}
