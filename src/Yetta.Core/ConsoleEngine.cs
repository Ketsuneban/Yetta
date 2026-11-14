namespace Yetta.Core;

public sealed record ConsoleContext(string Theme, string ActiveProfile = "Guest",
    string Version = "0.3.0-preview");
public sealed record ConsoleResult(string Message, string? NavigateTo = null,
    string? SwitchThemeTo = null, bool ClearOutput = false);

/// <summary>
/// Mavrine is an original, strictly allowlisted UI command router. No shell,
/// arbitrary process execution, registry modification or privileged actions.
/// </summary>
public static class ConsoleEngine
{
    public static ConsoleResult Execute(string? input, ConsoleContext context)
    {
        string command = (input ?? string.Empty).Trim().ToLowerInvariant();
        return command switch
        {
            "" => new ConsoleResult("Введите help для списка команд."),
            "help" => new ConsoleResult(
                "help             Справка по Mavrine\n" +
                "version          Версия Yetta\n" +
                "status           Реальное состояние компонентов\n" +
                "access           Текущий логический уровень\n" +
                "info             Системная информация\n" +
                "guard            DAX Guard\n" +
                "logs             DAX Logs\n" +
                "recovery         DAX Recovery\n" +
                "settings         Настройки\n" +
                "sources          Источники\n" +
                "theme fluent     Оформление Fluent\n" +
                "theme glass      Оформление Glass\n" +
                "clear            Очистить консоль"),
            "version" or "about" => new ConsoleResult("Yetta " + context.Version +
                "\nMavrine — внутренняя консоль. SYSTEM, DAX и UAC-брокер отсутствуют."),
            "status" => new ConsoleResult(
                "Yetta UI: работает\n" +
                "Профиль Yetta: " + AccessProfiles.DisplayName(context.ActiveProfile) + "\n" +
                "DAX Guard: ограничение команд внутри приложения\n" +
                "DAX-анализ, восстановление, системный сервис: НЕ РЕАЛИЗОВАНЫ\n" +
                "Тема: " + context.Theme),
            "access" => new ConsoleResult(AccessProfiles.Description(context.ActiveProfile)),
            "info" => new ConsoleResult("Открываю раздел «Информация».", NavigateTo: "info"),
            "guard" => new ConsoleResult("Открываю DAX Guard.", NavigateTo: "guard"),
            "logs" => new ConsoleResult("Открываю DAX Logs.", NavigateTo: "logs"),
            "recovery" => new ConsoleResult("Открываю DAX Recovery.", NavigateTo: "recovery"),
            "settings" => new ConsoleResult("Открываю настройки.", NavigateTo: "settings"),
            "sources" => new ConsoleResult("Открываю источники.", NavigateTo: "sources"),
            "theme fluent" => new ConsoleResult("Включён Fluent.", SwitchThemeTo: "Fluent"),
            "theme glass" => new ConsoleResult("Включён Glass.", SwitchThemeTo: "Glass"),
            "theme" => new ConsoleResult("Использование: theme fluent | theme glass"),
            "clear" => new ConsoleResult("", ClearOutput: true),
            _ => new ConsoleResult("Команда не поддерживается: " + command +
                "\nMavrine не выполняет системные команды. Введите help.")
        };
    }
}
