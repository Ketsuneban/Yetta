namespace Yetta.Core;

public sealed record CommandSuggestion(string Label, string Command);
public static class CommandCompletion
{
    private static readonly string[] Commands = ["/help", "/help 1", "/help 2", "/help 3", "/test warn",
        "/yetta version", "/yetta update", "/yetta download", "/dax status", "/dax diagnostics", "/subsy demo", "/subsy gallery",
        "/tty info", "/tty update", "/marvine subsy enable", "/marvine subsy disable", "/marvine colorset EGA",
        "/marvine colorset No Color", "/marvine colorset dracula", "/script list", "/script run", "/script stop", "/snob help", "clear", "status", "info", "settings", "guard", "logs"];

    public static IReadOnlyList<CommandSuggestion> Get(string input)
    {
        string raw = CommandAliases.Normalize(input.TrimStart());
        var tokens = CommandTokens.Read(raw, out _);
        bool space = raw.Length > 0 && char.IsWhiteSpace(raw[^1]);
        if (tokens.Count >= 2 && tokens[0].Value.Equals("/test", StringComparison.OrdinalIgnoreCase) &&
            tokens[1].Value.Equals("warn", StringComparison.OrdinalIgnoreCase))
        {
            int position = space ? tokens.Count : tokens.Count - 1;
            bool complete = tokens.Count == 2 || tokens.Count == 3 && WarningTestCommand.Kinds.Contains(tokens[2].Value, StringComparer.OrdinalIgnoreCase) ||
                tokens.Count == 4 && new[] { "Yetta", "DAX", "Tallow", "Windows" }.Contains(tokens[3].Value, StringComparer.OrdinalIgnoreCase);
            if (complete || tokens.Count > 3 && tokens[^1].Quoted ||
                tokens.Count > 4 && (tokens[^1].Value.EndsWith(":true", StringComparison.OrdinalIgnoreCase) || tokens[^1].Value.EndsWith(":false", StringComparison.OrdinalIgnoreCase))) position = tokens.Count;
            if (position == 2) return Options(WarningTestCommand.Kinds);
            if (position == 3) return Options(["Yetta", "DAX", "Tallow", "Windows"]);
            var choices = new List<string>();
            foreach (string flag in new[] { "pass", "tty", "check" })
            {
                if (tokens.Skip(4).Take(position - 4).Any(t => t.Value.StartsWith(flag + ":", StringComparison.OrdinalIgnoreCase))) continue;
                choices.Add(flag + ":true"); choices.Add(flag + ":false");
            }
            if (!tokens.Skip(4).Any(t => t.Quoted)) choices.Add("\"Тестовое предупреждение\"");
            return Options(choices);

            IReadOnlyList<CommandSuggestion> Options(IEnumerable<string> values)
            {
                bool replacing = position < tokens.Count;
                string prefix = replacing ? raw[..tokens[position].Start] : raw.TrimEnd() + " ";
                string typed = replacing ? tokens[position].Value : "";
                return values.Where(v => v.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                    .Select(v => new CommandSuggestion(v, prefix + v + " ")).ToArray();
            }
        }
        bool exact = Commands.Any(c => c.Equals(raw.TrimEnd(), StringComparison.OrdinalIgnoreCase));
        string basis = raw.TrimEnd();
        string prefixText = basis.Length == 0 ? "" : basis + " ";
        var next = Commands.Where(c => c.StartsWith(prefixText, StringComparison.OrdinalIgnoreCase)).ToArray();
        if ((exact || next.Length > 0) && basis.Length > 0)
            return next.Select(c => c[prefixText.Length..].Split(' ')[0]).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(v => new CommandSuggestion(v, prefixText + v + " ")).ToArray();
        int lastSpace = raw.LastIndexOf(' ');
        string before = lastSpace < 0 ? "" : raw[..(lastSpace + 1)];
        string fragment = raw[(lastSpace + 1)..];
        return Commands.Where(c => c.StartsWith(before, StringComparison.OrdinalIgnoreCase))
            .Select(c => c[before.Length..].Split(' ')[0]).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(v => v.StartsWith(fragment, StringComparison.OrdinalIgnoreCase))
            .Select(v => new CommandSuggestion(v, before + v + " ")).ToArray();
    }
}
