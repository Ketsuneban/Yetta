using System.Globalization;
using System.Text.Json;

namespace Yetta.Core;

/// <summary>One user-facing text catalog shared by Yetta, Tallow, DAX and Marvine.
/// Runtime edits cannot change protocol names or security decisions.</summary>
public static class TallowText
{
    private const string ResourceName = "Yetta.Shared.local.json";
    public const int MaximumBytes = 2 * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, string> Embedded = ReadEmbedded();
    private static readonly object Gate = new();
    private static IReadOnlyDictionary<string, string> _current = ReadLocalOrEmbedded();

    public static event EventHandler<TextCatalogChangedEventArgs>? Changed;
    public static IReadOnlyDictionary<string, string> All => Volatile.Read(ref _current);
    public static string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return All.TryGetValue(key, out var value) ? value : "[missing text: " + key + "]";
    }

    public static string Format(string key, params object?[] arguments)
    {
        try { return string.Format(CultureInfo.CurrentCulture, Get(key), arguments); }
        catch (FormatException)
        {
            // Malformed community-edited placeholders must not crash a warning.
            return string.Format(CultureInfo.CurrentCulture, Embedded[key], arguments);
        }
    }

    public static bool TryUpdate(ReadOnlySpan<byte> utf8)
    {
        if (!TryParseValid(utf8, out var next)) return false;
        TextCatalogChangedEventArgs? change = null;
        lock (Gate)
        {
            var old = _current;
            if (old.Count == next.Count && old.All(x =>
                next.TryGetValue(x.Key, out var text) && string.Equals(x.Value, text, StringComparison.Ordinal)))
                return true;
            Volatile.Write(ref _current, next);
            change = new TextCatalogChangedEventArgs(old, next);
        }
        Changed?.Invoke(null, change!);
        return true;
    }

    public static bool ReloadFromDisk()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "local.json");
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > MaximumBytes) return false;
            return TryUpdate(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public static bool TryPersist(ReadOnlySpan<byte> bytes)
    {
        if (!TryParseValid(bytes, out _)) return false;
        string dest = Path.Combine(AppContext.BaseDirectory, "local.json");
        string temp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temp, bytes.ToArray());
            File.Move(temp, dest, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static IReadOnlyDictionary<string, string> ReadEmbedded()
    {
        using var stream = typeof(TallowText).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("Embedded local.json is missing");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException("Embedded local.json is invalid");
    }

    private static IReadOnlyDictionary<string, string> ReadLocalOrEmbedded()
    {
        try
        {
            var file = new FileInfo(Path.Combine(AppContext.BaseDirectory, "local.json"));
            if (file.Exists && file.Length <= MaximumBytes &&
                TryParseValid(File.ReadAllBytes(file.FullName), out var parsed)) return parsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return Embedded;
    }

    private static bool TryParseValid(ReadOnlySpan<byte> utf8,
        out IReadOnlyDictionary<string, string> catalog)
    {
        catalog = Embedded;
        if (utf8.Length is 0 or > MaximumBytes) return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(utf8);
            if (parsed is null || Embedded.Any(entry =>
                    !parsed.TryGetValue(entry.Key, out var value) ||
                    value is null || value.Length > 16000))
                return false;
            catalog = parsed;
            return true;
        }
        catch (JsonException) { return false; }
    }
}

public sealed class TextCatalogChangedEventArgs(
    IReadOnlyDictionary<string, string> previous,
    IReadOnlyDictionary<string, string> current) : EventArgs
{
    public IReadOnlyDictionary<string, string> Previous { get; } = previous;
    public IReadOnlyDictionary<string, string> Current { get; } = current;
}
