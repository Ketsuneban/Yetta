namespace Yetta.Core;

/// <summary>Normalize command words only; quoted paths, flags and free text retain their bytes.</summary>
public static class CommandAliases
{
    private static readonly string[] Groups = ["help", "test", "yetta", "subsy", "tty", "marvine", "script", "snob"];
    private static readonly Dictionary<string, string> Root = new(StringComparer.OrdinalIgnoreCase)
        { ["marv"] = "marvine", ["scr"] = "script", ["cls"] = "clear", ["?"] = "help" };
    public static string Normalize(string input)
    {
        var tokens = CommandTokens.Read(input.TrimStart(), out _);
        string raw = input.TrimStart();
        if (tokens.Count == 0) return raw;
        string root = tokens[0].Value.TrimStart('/').ToLowerInvariant();
        if (Root.TryGetValue(root, out var alias)) root = alias;
        string[] roots = [.. Groups, "clear", "activity", "overview", "home", "recovery", "sources", "version", "about", "access", "theme", "notifications"];
        root = Expand(root, roots);
        string first = Groups.Contains(root) ? "/" + root : root;
        var replacements = new List<(int Start, int End, string Value)> { (tokens[0].Start, tokens[0].End, first) };
        if (tokens.Count > 1 && !tokens[1].Quoted)
        {
            string word = tokens[1].Value;
            string[] options = root switch
            {
                "yetta" => ["update", "version", "download"], "dax" => ["diagnostics"],
                "subsy" => ["demo", "gallery"], "snob" => ["install", "uninstall", "check", "yetta", "dax", "help"], "tty" => ["info", "update"],
                "marvine" => ["subsy", "colorset", "open"], "script" => ["list", "run", "stop"], "test" => ["warn"], _ => []
            };
            word = options.Length == 0 ? word : word.ToLowerInvariant() switch { "upd" => "update", "ver" => "version", "dl" => "download", "diag" => "diagnostics", "colors" => "colorset", _ => word };
            word = Expand(word, options);
            replacements.Add((tokens[0].End, tokens[1].End, " " + word));
            if (tokens.Count > 2) replacements.Add((tokens[1].End, tokens[2].Start, " "));
            if (root == "marvine" && word.Equals("subsy", StringComparison.OrdinalIgnoreCase) && tokens.Count > 2 && !tokens[2].Quoted)
                replacements.Add((tokens[2].Start, tokens[2].End, Expand(tokens[2].Value, ["enable", "disable"])));
        }
        foreach (var part in replacements.OrderByDescending(p => p.Start)) raw = raw[..part.Start] + part.Value + raw[part.End..];
        return raw;
    }
    private static string Expand(string word, string[] choices)
    {
        if (choices.Contains(word, StringComparer.OrdinalIgnoreCase)) return choices.First(c => c.Equals(word, StringComparison.OrdinalIgnoreCase));
        if (word.Length < 2) return word;
        var matches = choices.Where(c => c.StartsWith(word, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? matches[0] : word;
    }
}
