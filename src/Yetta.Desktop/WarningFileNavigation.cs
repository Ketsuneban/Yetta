using System.ComponentModel;
using System.Diagnostics;
using System.IO;
namespace Yetta.Desktop;
internal static class WarningFileNavigation
{
    internal static void Reveal(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile) return;
            string full = Path.GetFullPath(path);
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            if (File.Exists(full) || Directory.Exists(full)) start.Arguments = "/select,\"" + full.TrimEnd('\\') + "\"";
            else
            {
                string? parent = Path.GetDirectoryName(full);
                while (parent is not null && !Directory.Exists(parent)) parent = Path.GetDirectoryName(parent);
                if (parent is null) return;
                start.ArgumentList.Add(parent);
            }
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        { DaxAudit.Write("warning", "reveal_failed", ex.GetType().Name); }
    }
}
