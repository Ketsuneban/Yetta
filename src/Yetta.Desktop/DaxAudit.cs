using System.IO;
using System.Text.Json;

namespace Yetta.Desktop;

/// <summary>
/// Audits only operations initiated by Yetta. Not a Windows security or
/// Defender event log. Never records TTY keys, passwords or raw commands.
/// </summary>
internal static class DaxAudit
{
    private static readonly object Gate = new();
    internal static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Yetta", "DAX", "audit.jsonl");

    internal static void Write(string category, string result, string summary)
    {
        // Bound input and remove line-breaks before writing local JSONL.
        summary = summary.Replace('\r', ' ').Replace('\n', ' ');
        if (summary.Length > 400) summary = summary[..400];
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var row = new { utc = DateTimeOffset.UtcNow, category, result, summary };
                File.AppendAllText(FilePath, JsonSerializer.Serialize(row) + Environment.NewLine);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
