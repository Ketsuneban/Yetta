using System.IO;
using System.Text.Json;
using Yetta.Core;

namespace Yetta.Desktop;

internal sealed record DaxAuditEntry(DateTimeOffset Time, string Category,
    string Subject, string Detail, string Result);

/// <summary>
/// Audit covers Yetta-triggered warnings and actions only, not global Windows
/// events. TTY keys and PowerShell command text are never written here.
/// </summary>
internal static class DaxAuditLog
{
    private static readonly object Gate = new();
    internal static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Yetta", "dax-logs.jsonl");

    internal static void Append(YettaWarningLevel category, string subject,
        string detail, string result)
    {
        try
        {
            lock (Gate)
            {
                var directory = Path.GetDirectoryName(FilePath)
                    ?? throw new IOException("Invalid audit directory");
                Directory.CreateDirectory(directory);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 5_000_000)
                {
                    var archive = FilePath + ".previous";
                    File.Move(FilePath, archive, overwrite: true);
                }
                var record = new DaxAuditEntry(
                    DateTimeOffset.Now, category.ToString(),
                    subject.Length > 150 ? subject[..150] : subject,
                    detail.Length > 500 ? detail[..500] : detail,
                    result);
                File.AppendAllText(FilePath, JsonSerializer.Serialize(record) +
                    Environment.NewLine);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Audit failure must not be interpreted as authorization; operations
            // are independently gated by the warning decision and access policy.
        }
    }

    internal static IReadOnlyList<DaxAuditEntry> Recent(int limit = 100)
    {
        lock (Gate)
        {
            if (!File.Exists(FilePath)) return [];
            try
            {
                // Bound in-memory parsing even when a user manually edits logs.
                return File.ReadLines(FilePath)
                    .TakeLast(Math.Clamp(limit, 1, 250))
                    .Select(line =>
                    {
                        try { return JsonSerializer.Deserialize<DaxAuditEntry>(line); }
                        catch (JsonException) { return null; }
                    })
                    .Where(entry => entry is not null)
                    .Cast<DaxAuditEntry>()
                    .Reverse()
                    .ToArray();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }
    }
}
