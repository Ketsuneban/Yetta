namespace Yetta.Core;

public sealed record ConsoleContext(string Theme, string ActiveProfile = "Guest",
    string Version = "0.3.15-preview");
public sealed record ConsoleResult(string Message, string? NavigateTo = null,
    string? SwitchThemeTo = null, bool ClearOutput = false);

/// <summary>
/// Marvine is an original, strictly allowlisted UI command router. No shell,
/// arbitrary process execution, registry modification or privileged actions.
/// </summary>
public static class ConsoleEngine
{
    public static ConsoleResult Execute(string? input, ConsoleContext context)
    {
        string command = (input ?? string.Empty).Trim().ToLowerInvariant();
        return command switch
        {
            "" => new ConsoleResult(TallowText.Get("marvine.console.0001")),
            "help" => new ConsoleResult(
                TallowText.Get("marvine.console.0002") +
                TallowText.Get("marvine.console.0003") +
                TallowText.Get("marvine.console.0004") +
                TallowText.Get("marvine.console.0005") +
                TallowText.Get("marvine.console.0006") +
                "guard            DAX Guard\n" +
                "logs             DAX Logs\n" +
                TallowText.Get("marvine.console.0007") +
                TallowText.Get("marvine.console.0008") +
                TallowText.Get("marvine.console.0009") +
                "recovery         DAX Recovery\n" +
                TallowText.Get("marvine.console.0010") +
                TallowText.Get("marvine.console.0011") +
                TallowText.Get("marvine.console.0012") +
                TallowText.Get("marvine.console.0013") +
                TallowText.Get("marvine.console.0014")),
            "version" or "about" => new ConsoleResult("Yetta " + context.Version +
                TallowText.Get("marvine.console.0015")),
            "status" => new ConsoleResult(
                TallowText.Get("marvine.console.0016") +
                TallowText.Get("marvine.console.0017") + AccessProfiles.DisplayName(context.ActiveProfile) + "\n" +
                TallowText.Get("marvine.console.0018") +
                TallowText.Get("marvine.console.0019") +
                TallowText.Get("marvine.console.0020") + context.Theme),
            "access" => new ConsoleResult(AccessProfiles.Description(context.ActiveProfile)),
            "info" => new ConsoleResult(TallowText.Get("marvine.console.0021"), NavigateTo: "info"),
            "guard" => new ConsoleResult(TallowText.Get("marvine.console.0022"), NavigateTo: "guard"),
            "logs" => new ConsoleResult(TallowText.Get("marvine.console.0023"), NavigateTo: "logs"),
            "activity" or "notifications" => new ConsoleResult(TallowText.Get("marvine.console.0024"), NavigateTo: "activity"),
            "overview" or "home" => new ConsoleResult(TallowText.Get("marvine.console.0025"), NavigateTo: "overview"),
            "/snob" or "/snob help" => new ConsoleResult(TallowText.Get("marvine.console.0026")),
            "recovery" => new ConsoleResult(TallowText.Get("marvine.console.0027"), NavigateTo: "recovery"),
            "settings" => new ConsoleResult(TallowText.Get("marvine.console.0028"), NavigateTo: "settings"),
            "sources" => new ConsoleResult(TallowText.Get("marvine.console.0029"), NavigateTo: "sources"),
            "theme fluent" => new ConsoleResult(TallowText.Get("marvine.console.0030"), SwitchThemeTo: "Fluent"),
            "theme glass" => new ConsoleResult(TallowText.Get("marvine.console.0031"), SwitchThemeTo: "Glass"),
            "theme" => new ConsoleResult(TallowText.Get("marvine.console.0032")),
            "clear" => new ConsoleResult("", ClearOutput: true),
            _ => new ConsoleResult(TallowText.Get("marvine.console.0033") + command +
                TallowText.Get("marvine.console.0034"))
        };
    }
}
