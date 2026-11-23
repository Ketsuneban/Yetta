using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Controls;
using System.Windows.Media;
using Yetta.Core;

namespace Yetta.Desktop;

internal static class WarningCauseResolver
{
    internal static WarningCauseType Type(WarningRequest request)
    {
        if (request.CauseType != WarningCauseType.Auto) return request.CauseType;
        if (!string.IsNullOrWhiteSpace(request.SourcePath)) return WarningCauseType.File;
        return request.Subject.ToUpperInvariant() switch
        {
            "YETTA" => WarningCauseType.Yetta, "DAX" => WarningCauseType.Dax,
            "TALLOW" or "TAL" => WarningCauseType.Tallow, "WINDOWS" => WarningCauseType.Windows,
            "POWERSHELL" or "CMD" or "TERMINAL" or "ТЕРМИНАЛ" or "КОМАНДНАЯ СТРОКА" => WarningCauseType.Terminal,
            _ => WarningCauseType.Auto
        };
    }

    internal static int Icon(WarningRequest request) => WarningCause.Icon(Type(request), Facts(request.SourcePath));

    internal static Image Render(WarningRequest request, double size)
    {
        if (Type(request) == WarningCauseType.Terminal ||
            Type(request) == WarningCauseType.Application && request.SourcePath is { } terminal && WarningCause.IsTerminal(terminal))
            return IconAssets.Asset("Causes/12.png", size);
        // A document's registered editor icon does not identify the document itself.
        if (Type(request) == WarningCauseType.Application && request.SourcePath is { } path &&
            File.Exists(path) && ShellIcon.Load(path) is { } source)
        {
            var image = new Image { Source = source, Width = size, Height = size, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }
        return IconAssets.Asset("Causes/" + Icon(request) + ".png", size);
    }


    // Only inspect ACL metadata. Do not probe protection by writing to a user's file.
    private static bool HasProtection(string path, FileAttributes attributes)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return false;
        try
        {
            FileSystemSecurity acl = (attributes & FileAttributes.Directory) != 0
                ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
            var owner = acl.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            // Windows Modules Installer (TrustedInstaller).
            if (owner?.Value == "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464") return true;
            using var identity = WindowsIdentity.GetCurrent();
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                bool applies = rule.IdentityReference == identity.User ||
                    identity.Groups?.Contains(rule.IdentityReference) == true;
                if (applies && rule.AccessControlType == AccessControlType.Deny &&
                    (rule.FileSystemRights & (FileSystemRights.WriteData | FileSystemRights.Delete)) != 0) return true;
            }
        }
        catch (UnauthorizedAccessException) { return true; }
        catch (Exception ex) when (ex is IOException or System.Security.SecurityException or IdentityNotMappedException) { }
        return false;
    }

    private static bool Under(string path, string root) => !string.IsNullOrWhiteSpace(root) &&
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

    private static WarningCauseFacts? Facts(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            return new(path, IsCloudOnly: true);
        try
        {
            string full = Path.GetFullPath(path);
            string ext = Path.GetExtension(full).ToLowerInvariant();
            FileAttributes attributes = 0;
            try { attributes = File.GetAttributes(full); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            bool windows = Under(full, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            bool yetta = Under(full, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Yetta")) ||
                Under(full, AppContext.BaseDirectory);
            bool config = ext == ".reg" || ((windows || yetta) && ext is ".ini" or ".cfg" or ".conf" or ".config" or ".xml" or ".json") ||
                full.Equals(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts"), StringComparison.OrdinalIgnoreCase);
            // Offline / recall-on-open / recall-on-data-access: inspect metadata without hydrating a placeholder.
            bool cloud = ((int)attributes & (0x1000 | 0x40000 | 0x400000)) != 0;
            bool protectedFile = windows || (attributes & (FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Encrypted)) != 0 || HasProtection(full, attributes);
            return new(full, (attributes & FileAttributes.Directory) != 0, cloud, protectedFile, config);
        }
        catch (UnauthorizedAccessException) { return new(path, IsProtected: true); }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        { return new(path); }
    }
}
