using System.Text;
namespace Yetta.Core;

public sealed record CommandToken(string Value, int Start, int End, bool Quoted);
public static class CommandTokens
{
    public static IReadOnlyList<CommandToken> Read(string input, out bool closed)
    {
        var tokens = new List<CommandToken>();
        var text = new StringBuilder();
        bool quote = false, quoted = false; int start = -1;
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '"') { if (start < 0) start = i; quote = !quote; quoted = true; }
            else if (char.IsWhiteSpace(c) && !quote)
            {
                if (start >= 0) { tokens.Add(new(text.ToString(), start, i, quoted)); text.Clear(); start = -1; quoted = false; }
            }
            else { if (start < 0) start = i; text.Append(c); }
        }
        if (start >= 0) tokens.Add(new(text.ToString(), start, input.Length, quoted));
        closed = !quote;
        return tokens;
    }
}
