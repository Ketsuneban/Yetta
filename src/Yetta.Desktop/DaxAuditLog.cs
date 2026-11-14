using System.Text.Json;

namespace Yetta.Desktop;

/// <summary>
/// Persistent, bounded-on-read audit of explicit Yetta actions ONLY. This is
/// not an antivirus, a Windows Security event log, or a process telemetry feed.
/// TTY values, passwords and command payloads must NEVER be recorded.
/// </summary>
internal static class DaxAuditLog
{
    private sealed record Entry(DateTimeOffset Time, string Component, string Action, string Result);
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Yetta", "dax-events.jsonl");

    internal static void Write(string component, string action, string result)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // Serialize one event per line; append atomically for this process.
            string line = JsonSerializer.Serialize(
                new Entry(DateTimeOffset.UtcNow, component, action, result));
            using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.WriteLine(line);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static IReadOnlyList<string> Recent(int count = 100)
    {
        if (!File.Exists(FilePath)) return [];
        try
        {
            return File.ReadLines(FilePath).TakeLast(count).Reverse().Select(line =>
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<Entry>(line);
                    if (entry is null) return "Повреждённая строка журнала";
                    return $"{entry.Time.ToLocalTime():yyyy-MM-dd HH:mm:ss} · " +
                           $"{entry.Component} · {entry.Action} · {entry.Result}";
                }
                catch (JsonException) { return "Повреждённая строка журнала"; }
            }).ToArray();
        }
        catch (IOException) { return ["Не удалось прочитать журнал."]; }
        catch (UnauthorizedAccessException) { return ["Нет доступа к журналу."]; }
    }

    internal static string PathOnDisk => FilePath;
}
