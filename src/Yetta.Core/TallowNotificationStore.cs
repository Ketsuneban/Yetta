using System.Text;
using System.Text.Json;

namespace Yetta.Core;

/// <summary>
/// Local, non-security activity notifications. These are distinct from the
/// append-oriented DAX security audit: retention settings never delete audit logs.
/// </summary>
public sealed class TallowNotificationStore
{
    public sealed record Entry(DateTimeOffset Time, string Source, string Message);

    private readonly string _path;
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _now;
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public TallowNotificationStore(string? path = null, Func<DateTimeOffset>? now = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Yetta", "Activity", "notifications.jsonl");
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<Entry> Load(int retentionHours = 24)
    {
        lock (_gate)
        {
            var items = Read(retentionHours);
            SaveAtomic(items);
            return items;
        }
    }

    public void Add(string source, string message, int retentionHours = 24)
    {
        lock (_gate)
        {
            var items = Read(retentionHours);
            items.Add(new Entry(_now(), Bound(source, 120), Bound(message, 1600)));
            if (items.Count > 1000) items.RemoveRange(0, items.Count - 1000);
            SaveAtomic(items);
        }
    }

    private List<Entry> Read(int hours)
    {
        if (hours is < 1 or > 720) hours = 24;
        var cutoff = _now().AddHours(-hours);
        var items = new List<Entry>();
        if (!File.Exists(_path)) return items;
        try
        {
            foreach (var line in File.ReadLines(_path).TakeLast(2000))
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<Entry>(line, Options);
                    if (entry is not null && entry.Time >= cutoff && entry.Time <= _now().AddMinutes(5))
                        items.Add(entry);
                }
                catch (JsonException) { /* Skip corrupted individual lines, not the full history. */ }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        if (items.Count > 1000) items.RemoveRange(0, items.Count - 1000);
        return items;
    }

    private void SaveAtomic(List<Entry> items)
    {
        string? folder = Path.GetDirectoryName(_path);
        if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException("Invalid activity path.");
        Directory.CreateDirectory(folder);
        string temp = _path + ".tmp";
        try
        {
            using (var writer = new StreamWriter(temp, append: false, new UTF8Encoding(false)))
                foreach (var item in items)
                    writer.WriteLine(JsonSerializer.Serialize(item, Options));
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static string Bound(string value, int max)
    {
        value ??= string.Empty;
        return value.Length <= max ? value : value[..max];
    }
}
