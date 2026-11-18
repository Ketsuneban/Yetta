using System.Text.Json;

namespace Yetta.Core;

public enum SubsyPromptKind { Confirm, Text, Number, File, Password, Select, Multi, Expandable, Autocomplete, Pages }

public sealed record SubsyPrompt(string Id, SubsyPromptKind Kind, string Label, string[] Options,
    int Limit, string? Help, bool Reveal)
{
    public const string Prefix = "@subsy prompt ";

    public static SubsyPrompt? Parse(string line)
    {
        if (line.Length > 4096 || !line.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        try
        {
            using var json = JsonDocument.Parse(line[Prefix.Length..], new JsonDocumentOptions { MaxDepth = 4 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            static string? String(JsonElement item, string key) =>
                item.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
            string? id = String(root, "id");
            string? label = String(root, "label");
            string? type = String(root, "type");
            if (id is null || id.Length is < 1 or > 32 ||
                id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_') ||
                string.IsNullOrWhiteSpace(label) || label.Length > 240 ||
                !Enum.TryParse(type, ignoreCase: true, out SubsyPromptKind kind) ||
                !string.Equals(type, kind.ToString(), StringComparison.OrdinalIgnoreCase))
                return null;

            string[] options = [];
            if (kind is SubsyPromptKind.Select or SubsyPromptKind.Multi or SubsyPromptKind.Autocomplete or SubsyPromptKind.Pages)
            {
                if (!root.TryGetProperty("options", out var array) || array.ValueKind != JsonValueKind.Array ||
                    array.GetArrayLength() is < 1 or > 20) return null;
                var values = new List<string>();
                foreach (var option in array.EnumerateArray())
                {
                    if (option.ValueKind != JsonValueKind.String || option.GetString() is not { } value ||
                        string.IsNullOrWhiteSpace(value) || value.Length > 240) return null;
                    values.Add(value);
                }
                if (values.Distinct(StringComparer.Ordinal).Count() != values.Count) return null;
                options = values.ToArray();
            }
            int limit = options.Length;
            if (kind == SubsyPromptKind.Multi && root.TryGetProperty("limit", out var max) &&
                (max.ValueKind != JsonValueKind.Number || !max.TryGetInt32(out limit) || limit < 1 || limit > options.Length))
                return null;
            string? help = String(root, "help");
            bool reveal = kind == SubsyPromptKind.Password && root.TryGetProperty("reveal", out var revealField) &&
                revealField.ValueKind == JsonValueKind.True;
            return new(id, kind, label, options, limit, help is { Length: <= 400 } ? help : null, reveal);
        }
        catch (JsonException) { return null; }
    }
}
