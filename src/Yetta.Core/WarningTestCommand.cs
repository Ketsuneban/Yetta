namespace Yetta.Core;

public sealed record WarningTestCommand(string Kind, string Cause, bool Password, bool Tty, bool Check, string Text)
{
    public static readonly string[] Kinds = ["Standard", "Information", "Caution", "Dangerous", "Critical", "Fatal", "Agreement", "Dax"];
    public const string Usage = "/test warn <тип> [Yetta|DAX|Tallow|Windows|путь] [pass:true/false] [tty:true/false] [check:true/false] [\"текст\"]";
    public static bool TryParse(string input, out WarningTestCommand? result)
    {
        result = null;
        var args = CommandTokens.Read(input.Trim(), out bool closed);
        if (!closed || args.Count < 3 || !args[0].Value.Equals("/test", StringComparison.OrdinalIgnoreCase) ||
            !args[1].Value.Equals("warn", StringComparison.OrdinalIgnoreCase)) return false;
        string? kind = Kinds.FirstOrDefault(x => x.Equals(args[2].Value, StringComparison.OrdinalIgnoreCase));
        if (kind is null) return false;
        string cause = "Yetta"; int next = 3;
        if (next < args.Count && !IsFlag(args[next].Value)) cause = args[next++].Value;
        if (string.IsNullOrWhiteSpace(cause)) return false;
        var flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var description = new List<string>();
        for (; next < args.Count; next++)
        {
            var token = args[next];
            if (!token.Quoted && IsFlag(token.Value))
            {
                string[] parts = token.Value.Split(':', 2);
                if (!bool.TryParse(parts[1], out bool value) || !flags.TryAdd(parts[0], value)) return false;
            }
            else description.Add(token.Value);
        }
        result = new(kind, cause, flags.GetValueOrDefault("pass"), flags.GetValueOrDefault("tty"),
            flags.GetValueOrDefault("check"), description.Count == 0 ? "Это тестовое предупреждение." : string.Join(" ", description));
        return true;
    }
    private static bool IsFlag(string text) => new[] { "pass:", "tty:", "check:" }
        .Any(key => text.StartsWith(key, StringComparison.OrdinalIgnoreCase));
}
