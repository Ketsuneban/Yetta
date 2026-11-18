using System.IO;

namespace Yetta.Core;

public enum DaxState { Ready, Working, Notice, Disabled, Threat, Fatal, Unavailable, Incompatible }

public sealed record DaxStatus(DaxState State, string AgentVersion, bool DesktopPresent, bool CatalogPresent)
{
    public string Serialize() =>
        $"DAX/2|{State}|{AgentVersion}|{(DesktopPresent ? 1 : 0)}|{(CatalogPresent ? 1 : 0)}";

    public static DaxStatus Parse(string? frame)
    {
        var fields = frame?.Split('|');
        if (fields is { Length: 5 } && fields[0] == "DAX/2" &&
            Enum.TryParse(fields[1], out DaxState state) && state is >= DaxState.Ready and <= DaxState.Fatal &&
            fields[1] == state.ToString() && Version.TryParse(fields[2], out var version) &&
            fields[3] is "0" or "1" && fields[4] is "0" or "1")
            return new(state, version.ToString(), fields[3] == "1", fields[4] == "1");
        return new(DaxState.Incompatible, "", false, false);
    }
}

public static class DaxProtocol
{
    public const string PipeName = "Yetta-DAX-Local-v1";
    public const string StatusRequest = "TALLOW_STATUS_V2";
    public const int MaxFrameLength = 256;

    // Read a small, newline-terminated message even when the peer never sends a newline.
    public static async Task<string?> ReadFrameAsync(TextReader reader, CancellationToken token)
    {
        char[] frame = new char[MaxFrameLength];
        char[] next = new char[1];
        for (int i = 0; i < frame.Length; i++)
        {
            if (await reader.ReadAsync(next.AsMemory(), token) == 0) return null;
            if (next[0] == '\n') return new string(frame, 0, i).TrimEnd('\r');
            frame[i] = next[0];
        }
        throw new InvalidDataException("DAX message exceeds the allowed length.");
    }
}
