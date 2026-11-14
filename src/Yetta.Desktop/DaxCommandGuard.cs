using System.Text.RegularExpressions;

namespace Yetta.Desktop;

/// <summary>
/// Heuristic preflight for commands *submitted through Yetta's embedded
/// PowerShell only*. This is not malware detection, AV, or process monitoring.
/// </summary>
internal static class DaxCommandGuard
{
    private static readonly Regex Critical = new(
        @"(?i)\b(?:format-volume|clear-disk|bcdedit|vssadmin\s+delete|diskpart|disable-computerrestore|remove-item\b[^\r\n]{0,250}-recurse|set-mppreference\s+-disablerealtimemonitoring)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Caution = new(
        @"(?i)\b(?:invoke-expression|iex|invoke-webrequest|downloadstring|start-process|new-service|set-executionpolicy|reg\s+delete|stop-process)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    internal static WarningKind? Classify(string command)
    {
        if (command.Length > 32768) return WarningKind.Critical;
        try
        {
            if (Critical.IsMatch(command)) return WarningKind.Critical;
            if (Caution.IsMatch(command)) return WarningKind.Caution;
        }
        catch (RegexMatchTimeoutException) { return WarningKind.Critical; }
        return null;
    }
}
