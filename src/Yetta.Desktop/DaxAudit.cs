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
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    internal sealed record Entry(DateTimeOffset Utc, string Category, string Result, string Summary, string? Actor);
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
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length >= 4 * 1024 * 1024)
                    File.Move(FilePath, FilePath + ".previous", overwrite: true);
                var row = new { utc = DateTimeOffset.UtcNow, actor = "Tallow", category, result, summary };
                File.AppendAllText(FilePath, JsonSerializer.Serialize(row) + Environment.NewLine);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static IReadOnlyList<Entry> Recent(int limit)
    {
        if (!File.Exists(FilePath)) return [];
        var entries = new Queue<Entry>();
        foreach (string line in File.ReadLines(FilePath))
        {
            if (line.Length > 16_384) continue;
            try
            {
                if (JsonSerializer.Deserialize<Entry>(line, ReadOptions) is not
                    { Category: not null, Result: not null, Summary: not null } entry) continue;
                if (entries.Count == limit) entries.Dequeue();
                entries.Enqueue(entry);
            }
            catch (JsonException) { }
        }
        return entries.Reverse().ToArray();
    }
}
