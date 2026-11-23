namespace Yetta.Core;

public enum WarningCauseType { Auto, File, Application, Yetta, Dax, Tallow, Windows, Cloud, Folder, Terminal }

/// <summary>Metadata only. IsImportant is retained for compatibility and has no icon category.</summary>
public sealed record WarningCauseFacts(string Path, bool IsDirectory = false, bool IsCloudOnly = false,
    bool IsProtected = false, bool IsWindowsConfiguration = false, bool IsImportant = false);

public static class WarningCause
{
    public static int WindowsIcon(int build) => build >= 22000 ? 11 : 13;
    public static bool IsTerminal(string path)
    {
        string name = path.Replace('\\', '/').Split('/').Last().ToLowerInvariant();
        return name is "powershell.exe" or "powershell" or "pwsh.exe" or "pwsh" or "cmd.exe" or "cmd"
            or "wt.exe" or "wt" or "windowsterminal.exe" or "windowsterminal" or "terminal";
    }

    public static int Icon(WarningCauseType type, WarningCauseFacts? file = null, int? windowsBuild = null)
    {
        if (type != WarningCauseType.File && type != WarningCauseType.Auto)
            return type switch
            {
                WarningCauseType.Application => file is not null && IsTerminal(file.Path) ? 12 : 3,
                WarningCauseType.Terminal => 12, WarningCauseType.Tallow => 18,
                WarningCauseType.Windows => WindowsIcon(windowsBuild ?? Environment.OSVersion.Version.Build),
                WarningCauseType.Cloud => 10, WarningCauseType.Folder => 2,
                WarningCauseType.Yetta => 16, WarningCauseType.Dax => 17, _ => 1
            };
        if (file is null) return 1;
        if (file.IsCloudOnly) return 10;
        if (file.IsDirectory) return 2;
        if (file.IsWindowsConfiguration) return 15;
        if (file.IsProtected) return 9;
        string name = file.Path.Replace('\\', '/').Split('/').Last();
        int dot = name.LastIndexOf('.');
        string extension = dot < 0 ? "" : name[dot..].ToLowerInvariant();
        return extension switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" or ".ico" or ".heic" or ".avif" or ".tif" or ".tiff" => 4,
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" or ".aac" or ".wma" or ".opus" => 5,
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".wmv" or ".m4v" or ".mpeg" or ".mpg" => 6,
            ".txt" or ".md" or ".log" or ".csv" or ".rtf" or ".doc" or ".docx" or ".odt" or ".pdf" => 7,
            ".exe" or ".com" or ".bat" or ".cmd" or ".py" or ".pyw" or ".ps1" or ".psm1" or ".msi" or ".vbs" or ".js" or ".wsf" or ".jar" or ".scr" => 8,
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".bz2" or ".xz" or ".iso" or ".cab" => 14,
            ".reg" => 15,
            _ => 1
        };
    }
}
