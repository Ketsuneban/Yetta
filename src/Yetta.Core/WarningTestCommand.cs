using System.Text;

namespace Yetta.Core;

public sealed record WarningTestCommand(string Kind, string Cause, bool Password, bool Tty, bool Check, string Text)
{
    public const string Usage = "/test warn <Standard|Information|Caution|Dangerous|Critical|Fatal|Agreement|Dax> <Yetta|DAX|Tallow|Windows|путь> pass:true/false tty:true/false check:true/false \"текст\"";

    public static bool TryParse(string input, out WarningTestCommand? result)
    {
        result = null;
        // Quotes delimit paths/text; backslashes remain literal Windows path characters.
        var args = new List<string>();
        var part = new StringBuilder();
        bool quoted = false, started = false;
        foreach (char c in input.Trim())
        {
            if (c == '"') { quoted = !quoted; started = true; }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (started) { args.Add(part.ToString()); part.Clear(); started = false; }
            }
            else { part.Append(c); started = true; }
        }
        if (quoted) return false;
        if (started) args.Add(part.ToString());
        if (args.Count != 8 || !args[0].Equals("/test", StringComparison.OrdinalIgnoreCase) ||
            !args[1].Equals("warn", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(args[3]) ||
            string.IsNullOrWhiteSpace(args[7])) return false;
        string[] kinds = ["Standard", "Information", "Caution", "Dangerous", "Critical", "Fatal", "Agreement", "Dax"];
        string? kind = kinds.FirstOrDefault(x => x.Equals(args[2], StringComparison.OrdinalIgnoreCase));
        if (kind is null || !Flag(args[4], "pass", out bool password) ||
            !Flag(args[5], "tty", out bool tty) || !Flag(args[6], "check", out bool check)) return false;
        result = new(kind, args[3], password, tty, check, args[7]);
        return true;
    }

    private static bool Flag(string value, string key, out bool result)
    {
        result = false;
        return value.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase) &&
            bool.TryParse(value[(key.Length + 1)..], out result);
    }
}
