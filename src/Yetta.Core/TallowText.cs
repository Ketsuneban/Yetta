using System.Globalization;
using System.Text.Json;

namespace Yetta.Core;

/// <summary>
/// Unified static text catalog for Yetta, Tallow, DAX and Marvine.
/// Both executables use the same local.json from their installation folder.
/// The embedded catalog is the fallback if that file is missing or damaged.
/// User-editable text never defines permission policy or protocol tokens.
/// </summary>
public static class TallowText
{
    private const string ResourceName = "Yetta.Shared.local.json";
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Catalog =
        new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyDictionary<string, string> All => Catalog.Value;

    public static string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return All.TryGetValue(key, out var value) ? value : "[missing text: " + key + "]";
    }

    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    private static Dictionary<string, string> Parse(Stream stream) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
        ?? throw new InvalidDataException("local.json must contain a JSON object of strings.");

    private static IReadOnlyDictionary<string, string> Load()
    {
        using Stream embedded = typeof(TallowText).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("Embedded local.json is missing.");
        var fallback = Parse(embedded);
        string path = Path.Combine(AppContext.BaseDirectory, "local.json");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 2 * 1024 * 1024)
                return fallback;
            using var stream = File.OpenRead(path);
            var external = Parse(stream);
            // Never accept a half-written or incomplete catalog.
            if (fallback.Keys.Any(key => !external.TryGetValue(key, out var value) || value is null || value.Length > 16000))
                return fallback;
            return external;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return fallback;
        }
    }
}
