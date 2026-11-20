namespace Yetta.Core;

public sealed record ConsoleContext(string Theme, string ActiveProfile = "Guest",
    string Version = "0.5.1-preview", bool SubsyEnabled = true, string Colorset = "EGA");
public sealed record ConsoleResult(string Message, string? NavigateTo = null,
    string? SwitchThemeTo = null, bool ClearOutput = false,
    string? Request = null, string? Argument = null, WindowsPreference? Preference = null, int? SettingValue = null);

/// <summary>
/// Marvine is an original, strictly allowlisted UI command router. No shell,
/// arbitrary process execution, registry modification or privileged actions.
/// </summary>
public static class ConsoleEngine
{
    public static ConsoleResult Execute(string? input, ConsoleContext context)
    {
        string raw = (input ?? string.Empty).Trim();
        if (raw.Length > 4096)
            return new ConsoleResult("[⚠] Marvine: команда слишком длинная (максимум 4096 символов).");
        raw = CommandAliases.Normalize(raw);
        string command = raw.ToLowerInvariant();
        if (CommandTokens.Read(raw, out _).FirstOrDefault()?.Value == "/win")
            return WindowsCommands.Parse(raw);
        if (command == "/script" || command == "/script help")
            return new("script run [\"путь к файлу.py\"] — выбрать и запустить файл Python\nscript list — список запусков\nscript stop <ID|путь> — остановить скрипт");
        if (command == "/script run") return new("", Request: "SCRIPT_RUN");
        if (command == "/script stop") return new("script stop <ID|путь> · Идентификаторы показывает script list.");
        if (command == "/test warn" || command.StartsWith("/test warn ", StringComparison.Ordinal))
            return WarningTestCommand.TryParse(raw, out _)
                ? new ConsoleResult("", Request: "TEST_WARN", Argument: raw)
                : new ConsoleResult(WarningTestCommand.Usage);
        if (command is "/help" or "/help 1" or "/help 2" or "/help 3" or "/help 4")
        {
            int page = command.Length > 6 && int.TryParse(command[6..], out int requested) ? requested : 1;
            return new ConsoleResult(HelpPage(page, context.ActiveProfile), Request: "HELP_PAGE", Argument: page.ToString());
        }
        if (command.StartsWith("/help ", StringComparison.Ordinal))
            return new ConsoleResult("[⚠] Marvine: страницы справки: /help 1, /help 2, /help 3, /help 4.");
        if (command is "/yetta version" or "/yetta update")
            return new ConsoleResult("", Request: command[7..].ToUpperInvariant());
        if (command == "/dax status")
            return new ConsoleResult("", Request: "DAX_STATUS");
        if (command == "/dax diagnostics")
            return new ConsoleResult("Открываю диагностику DAX.", NavigateTo: "recovery");
        if (command == "/subsy demo")
            return new ConsoleResult("", Request: "SUBSY_DEMO");
        if (command == "/subsy gallery")
            return new ConsoleResult("", Request: "SUBSY_GALLERY");
        if (command.StartsWith("/yetta download ", StringComparison.Ordinal))
            return new ConsoleResult("", Request: "DOWNLOAD", Argument: raw[16..].Trim());
        if (command is "/tty info" or "/tty update")
            return new ConsoleResult("", Request: command[5..].ToUpperInvariant() == "INFO" ? "TTY_INFO" : "TTY_UPDATE");
        if (command is "/marvine subsy enable" or "/marvine subsy disable")
            return new ConsoleResult("", Request: "SUBSY", Argument: command.EndsWith("enable", StringComparison.Ordinal) ? "enable" : "disable");
        if (command is "/marvine colorset ega" or "/marvine colorset no color" or "/marvine colorset dracula")
            return new ConsoleResult("", Request: "COLORSET", Argument: command.EndsWith("dracula", StringComparison.Ordinal) ? "dracula" : command.EndsWith("ega", StringComparison.Ordinal) ? "EGA" : "No Color");
        if (command is "/script list")
            return new ConsoleResult("", Request: "SCRIPT_LIST");
        if (command.StartsWith("/script run ", StringComparison.Ordinal))
            return new ConsoleResult("", Request: "SCRIPT_RUN", Argument: raw[12..].Trim());
        if (command.StartsWith("/script stop ", StringComparison.Ordinal))
            return new ConsoleResult("", Request: "SCRIPT_STOP", Argument: raw[13..].Trim());
        if (command is "/snob" or "/snob help")
            return new ConsoleResult("[ℹ] Marvine: /snob не повышает права. Профиль Администратор доступен в DAX Guard.");
        if (command.StartsWith("/snob ", StringComparison.Ordinal))
            return new ConsoleResult("[✖] Marvine: эта команда не выполняет действия с повышенными правами.");
        return command switch
        {
            "" => new ConsoleResult(TallowText.Get("marvine.console.0001")),
            "version" or "about" => new ConsoleResult("Yetta " + context.Version +
                TallowText.Get("marvine.console.0015")),
            "status" => new ConsoleResult(
                TallowText.Get("marvine.console.0016") +
                TallowText.Get("marvine.console.0017") + AccessProfiles.DisplayName(context.ActiveProfile) + "\n" +
                TallowText.Get("marvine.console.0018") +
                TallowText.Get("marvine.console.0019") +
                TallowText.Get("marvine.console.0020") + "Windows 11"),
            "access" => new ConsoleResult(AccessProfiles.Description(context.ActiveProfile)),
            "info" => new ConsoleResult(TallowText.Get("marvine.console.0021"), NavigateTo: "info"),
            "guard" => new ConsoleResult(TallowText.Get("marvine.console.0022"), NavigateTo: "guard"),
            "logs" => new ConsoleResult(TallowText.Get("marvine.console.0023"), NavigateTo: "logs"),
            "activity" or "notifications" => new ConsoleResult(TallowText.Get("marvine.console.0024"), NavigateTo: "activity"),
            "overview" or "home" => new ConsoleResult(TallowText.Get("marvine.console.0025"), NavigateTo: "overview"),
            "recovery" => new ConsoleResult(TallowText.Get("marvine.console.0027"), NavigateTo: "recovery"),
            "settings" => new ConsoleResult(TallowText.Get("marvine.console.0028"), NavigateTo: "settings"),
            "sources" => new ConsoleResult(TallowText.Get("marvine.console.0029"), NavigateTo: "sources"),
            "theme fluent" or "theme glass" => new ConsoleResult("Yetta использует оформление Windows 11 автоматически."),
            "theme" => new ConsoleResult("Yetta использует оформление Windows 11 автоматически."),
            "clear" => new ConsoleResult("", ClearOutput: true),
            _ => new ConsoleResult(TallowText.Get("marvine.console.0033") + command +
                TallowText.Get("marvine.console.0034"))
        };
    }

    public static IReadOnlyList<string> Suggestions(string? prefix)
    {
        return CommandCompletion.Get(prefix ?? "").Select(s => s.Command).ToArray();
    }

    public static string HelpPage(int page, string profile)
    {
        if (page is < 1 or > 4) return "[⚠] Marvine: доступные страницы справки: 1–4.";
        return page switch
        {
            1 => "[⌘] Marvine: команды · страница 1/4\n" +
                "[Зелёный] /help [1–4] — справка\n" +
                "[Зелёный] /yetta version — установленная и последняя версии\n" +
                "[Зелёный] /yetta update — установить последний релиз\n" +
                "[Зелёный] /yetta download <ID> — установить релиз по ID\n" +
                "[Зелёный] /tty info|update — показать или сменить ключ\n" +
                "Напиши /help 2 для следующей страницы.",
            2 => "[⌘] Marvine: команды · страница 2/4\n" +
                "[Зелёный] /marvine subsy enable|disable — подсистема интерфейса\n" +
                "[Зелёный] /subsy demo|gallery — прогресс и все виды форм\n" +
                "[Зелёный] /marvine colorset EGA|No Color|dracula — цвета сообщений\n" +
                "[Зелёный] /script list — список Python-скриптов\n" +
                (profile == "Administrator" ? "[Жёлтый]" : "[Красный]") +
                " /script run [путь] — профиль Administrator и DAX\n" +
                (profile == "Guest" ? "[Красный]" : "[Зелёный]") + " /script stop <ID|путь> — остановить скрипт\n" +
                "Напиши /help 3 для следующей страницы.",
            4 => WindowsCommands.Help,
            _ => "[⌘] Marvine: команды · страница 3/4\n" +
                "[Серый] /snob — зарезервированная команда\n" +
                "[Зелёный] /dax status — состояние локального DAX\n" +
                "[Зелёный] /dax diagnostics — экран диагностики\n" +
                "[Зелёный] " + WarningTestCommand.Usage + "\n" +
                "[Зелёный] clear, status, info, settings, guard, logs\n" +
                "Настройка Windows — /help 4 или win."
        };
    }
}
