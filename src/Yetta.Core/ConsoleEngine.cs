namespace Yetta.Core;

public sealed record ConsoleContext(string Theme, string ActiveProfile = "Guest",
    string Version = "0.7.0", bool SubsyEnabled = true, string Colorset = "EGA");
public sealed record ConsoleResult(string Message, string? NavigateTo = null, string? SwitchThemeTo = null,
    bool ClearOutput = false, string? Request = null, string? Argument = null);

public static class ConsoleEngine
{
    internal static readonly Dictionary<string, string> Syntax = new(StringComparer.OrdinalIgnoreCase)
    {
        ["help"] = "/help", ["script"] = "/script [действие] [путь]", ["test"] = WarningTestCommand.Usage,
        ["marvine"] = "/marvine open <путь> · /marvine colorset <палитра> · /marvine subsy enable|disable", ["yetta"] = "/yetta [действие]",
        ["tty"] = "/tty [Info | Update]", ["subsy"] = "/subsy [demo | gallery]", ["clear"] = "/clear",
        ["snob"] = "/snob install|uninstall|check <программа> · /snob yetta close|exit|uninstall · /snob dax status", ["version"] = "/version", ["access"] = "/access", ["activity"] = "/activity",
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
            case "help" when tokens.Count == 1:
                var help = HelpPage(1, context.ActiveProfile);
                return new(help, Request: "HELP", Argument: help);
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
                if (action == "open" && tail.Length > 0)
                    return new("", Request: "MARVINE_OPEN", Argument: tail);
                if (action == "subsy" && tokens.Count == 3 && tail.ToLowerInvariant() is "enable" or "disable")
                    return new("", Request: "SUBSY", Argument: tail.ToLowerInvariant());
                if (action == "colorset" && new[] { "EGA", "No Color", "dracula" }.FirstOrDefault(c => c.Equals(tail, StringComparison.OrdinalIgnoreCase)) is { } color)
                    return new("", Request: "COLORSET", Argument: color);
                return Invalid(tail.Length > 0 ? tail : action);
            case "yetta":
                if (action is "update" or "version" && tokens.Count == 2) return new("", Request: action.ToUpperInvariant());
                if (action == "download" && tokens.Count == 3) return new("", Request: "DOWNLOAD", Argument: tokens[2].Value);
                return Invalid(tail.Length > 0 ? tail : action, "/yetta update|version · /yetta download <ID или тег>");
            case "tty" when tokens.Count == 2 && action is "info" or "update": return new("", Request: "TTY_" + action.ToUpperInvariant());
            case "subsy" when tokens.Count == 2 && action is "demo" or "gallery": return new("", Request: "SUBSY_" + action.ToUpperInvariant());
            case "snob":
                if (action is "" or "help") return new(syntax + "\nКоманды выполняются с текущими правами Windows.");
                if (action == "uninstall" && tokens.Count == 2) return new("", Request: "SNOB_UNINSTALL");
                if (action is "install" or "uninstall" or "check" && tokens.Count > 2)
                    return new("", Request: "SNOB_" + action.ToUpperInvariant(), Argument: string.Join(" ", tokens.Skip(2).Select(t => t.Value)));
                if (action == "yetta" && tokens.Count == 3 && tokens[2].Value.ToLowerInvariant() is "close" or "exit" or "uninstall")
                    return new("", Request: "SNOB_YETTA", Argument: tokens[2].Value.ToLowerInvariant());
                if (action == "dax" && (tokens.Count == 2 || tokens.Count == 3 && tokens[2].Value.Equals("status", StringComparison.OrdinalIgnoreCase)))
                    return new("", Request: "SNOB_DAX");
                return Invalid(tail.Length > 0 ? tail : action);
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
        "/script run|list|stop [путь или ID] — открыть Python в Subsy, показать сеансы или остановить запуск",
        "/marvine open <путь> — сменить рабочую папку сеанса",
        "/marvine colorset <палитра> — выбрать оформление консоли",
        "/marvine subsy enable|disable — настроить оформление вывода в Marvine",
        Syntax["test"] + " — показать тестовое предупреждение",
        "/yetta version|update|download [релиз] — проверить версию, обновиться или выбрать релиз",
        Syntax["tty"] + " — показать или обновить ключ",
        Syntax["subsy"] + " — открыть примеры элементов Subsy",
        "/help · /clear — показать справку или очистить вывод",
        "/snob install|uninstall|check <программа> — управлять приложениями Windows",
        "/snob yetta close|exit|uninstall — закрыть, завершить или удалить Yetta",
        "/snob dax status — посмотреть состояние DAX",
        "/version · /access · /theme — посмотреть сведения о сеансе",
        "/overview · /activity · /sources · /recovery — открыть раздел Yetta"
    });
    public static IReadOnlyList<string> Suggestions(string? prefix) => CommandCompletion.Get(prefix ?? "").Select(x => x.Command).ToArray();
}
