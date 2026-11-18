using System.Globalization;

namespace Yetta.Core;

public sealed record SubsyFrame(string Label, int Current, int Total, bool Finished = false, bool IsStep = false)
{
    // Explicit stdout format: @subsy progress 0..100 text, step n/m text, or done text.
    public static SubsyFrame? Parse(string line)
    {
        if (line.Length > 512 || !line.StartsWith("@subsy ", StringComparison.Ordinal)) return null;
        string[] fields = line.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length >= 2 && fields[1] == "done")
            return new(fields.Length > 2 ? string.Join(' ', fields.Skip(2)) : "Готово", 100, 100, true);
        if (fields.Length < 3) return null;
        string label = fields.Length == 4 ? fields[3] : "";
        if (fields[1] == "progress" && ParseNumber(fields[2], out int percent) && percent <= 100)
            return new(label, percent, 100);
        if (fields[1] == "step")
        {
            string[] step = fields[2].Split('/');
            if (step.Length == 2 && ParseNumber(step[0], out int current) &&
                ParseNumber(step[1], out int total) && total is > 0 and <= 10000 && current <= total)
                return new(label, current, total, IsStep: true);
        }
        return null;
    }

    private static bool ParseNumber(string value, out int result) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
}
