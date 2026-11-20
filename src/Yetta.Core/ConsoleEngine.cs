namespace Yetta.Core;

public sealed record ConsoleContext(string Theme, string ActiveProfile = "Guest",
    string Version = "0.6.0", bool SubsyEnabled = true, string Colorset = "EGA");
public sealed record ConsoleResult(string Message, string? NavigateTo = null, string? SwitchThemeTo = null,
    bool ClearOutput = false, string? Request = null, string? Argument = null);

public static class ConsoleEngine
{
    internal static readonly Dictionary<string, string> Syntax = new(StringComparer.OrdinalIgnoreCase)
    {
        ["help"] = "/help", ["script"] = "/script [действие] [путь]", ["test"] = WarningTestCommand.Usage,
        ["marvine"] = "/marvine [Colorset | Subsy] [дополнительные аргументы]", ["yetta"] = "/yetta [действие]",
        ["tty"] = "/tty [Info | Update]", ["subsy"] = "/subsy [demo | gallery]", ["clear"] = "/clear",
        ["snob"] = "/snob", ["version"] = "/version", ["access"] = "/access", ["activity"] = "/activity",
        ["overview"] = "/overview", ["sources"] = "/sources", ["recovery"] = "/recovery", ["theme"] = "/theme"
    };

    public static ConsoleResult Execute(string? input, ConsoleContext context)
    {
        string raw = (input ?? "").Trim();
        if (raw.Length == 0) return new("");
        if (raw.Length > 4096) return new("[✖] Команда длиннее 4096 символов.");
        raw = CommandAliases.Normalize(raw);
        var tokens = CommandTokens.Read(raw, out bool closed);
        if (tokens.Count == 0) return new("");
        string root = tokens[0].Value.TrimStart('/').ToLowerInvariant();
        string action = tokens.Count > 1 ? tokens[1].Value.ToLowerInvariant() : "";
        string canonical = root switch { "home" => "overview", "about" => "version", "notifications" => "activity", _ => root };
        if (!Syntax.TryGetValue(canonical, out var syntax)) return new($"[✖] Неизвестная команда: «{root}»");
        ConsoleResult Invalid(string value, string? form = null) => new($"[✖] Неверный аргумент: «{value}»\nПравильный вид: {form ?? syntax}");
        if (!closed) return Invalid("незакрытые кавычки");
        string tail = tokens.Count > 2 ? raw[tokens[2].Start..] : "";
        switch (canonical)
        {
            case "help" when tokens.Count == 1: return new(HelpPage(1, context.ActiveProfile));
            case "script":
                if (action is "" or "help" && tokens.Count <= 2) return new(syntax + "\nrun — выбрать или запустить .py\nlist — список запусков\nstop — остановить по ID или пути");
                if (action == "list" && tokens.Count == 2) return new("", Request: "SCRIPT_LIST");
                if (action == "run") return new("", Request: "SCRIPT_RUN", Argument: tail.Length == 0 ? null : tail);
                if (action == "stop" && tail.Length > 0) return new("", Request: "SCRIPT_STOP", Argument: tail);
                return Invalid(action, "/script run [путь] · /script list · /script stop <ID или путь>");
            case "test":
                if (action != "warn") return Invalid(action.Length > 0 ? action : "действие");
                return WarningTestCommand.TryParse(raw, out _, out var invalid)
                    ? new("", Request: "TEST_WARN", Argument: raw) : Invalid(invalid);
            case "marvine":
                if (action == "subsy" && tokens.Count == 3 && tail.ToLowerInvariant() is "enable" or "disable")
                    return new("", Request: "SUBSY", Argument: tail.ToLowerInvariant());
                if (action == "colorset" && new[] { "EGA", "No Color", "dracula" }.FirstOrDefault(c => c.Equals(tail, StringComparison.OrdinalIgnoreCase)) is { } color)
                    return new("", Request: "COLORSET", Argument: color);
                return Invalid(tail.Length > 0 ? tail : action, "/marvine colorset EGA|dracula|No Color · /marvine subsy enable|disable");
            case "yetta":
                if (action is "update" or "version" && tokens.Count == 2) return new("", Request: action.ToUpperInvariant());
                if (action == "download" && tokens.Count == 3) return new("", Request: "DOWNLOAD", Argument: tokens[2].Value);
                return Invalid(tail.Length > 0 ? tail : action, "/yetta update|version · /yetta download <ID или тег>");
            case "tty" when tokens.Count == 2 && action is "info" or "update": return new("", Request: "TTY_" + action.ToUpperInvariant());
            case "subsy" when tokens.Count == 2 && action is "demo" or "gallery": return new("", Request: "SUBSY_" + action.ToUpperInvariant());
            case "snob" when tokens.Count == 1 || action == "help": return new("/snob не повышает права. Профили доступны в DAX Guard.");
            case "theme" when tokens.Count <= 2: return new("Yetta использует оформление Windows.");
        }
        if (tokens.Count > 1) return Invalid(tokens[^1].Value);
        return canonical switch
        {
            "clear" => new("", ClearOutput: true), "version" => new("Yetta " + context.Version),
            "access" => new(AccessProfiles.Description(context.ActiveProfile)),
            "activity" or "overview" or "sources" or "recovery" => new("", NavigateTo: canonical),
            _ => new(syntax)
        };
    }

    public static string HelpPage(int page, string profile) => string.Join("\n", new[]
    {
        Syntax["script"] + " — run, list, stop", Syntax["test"], Syntax["marvine"],
        Syntax["yetta"] + " — version, update, download", Syntax["tty"], Syntax["subsy"],
        "/help · /clear — справка и очистка вывода", "/snob — информация о доступе", "/version · /access · /theme", "/overview · /activity · /sources · /recovery"
    });
    public static IReadOnlyList<string> Suggestions(string? prefix) => CommandCompletion.Get(prefix ?? "").Select(x => x.Command).ToArray();
}
