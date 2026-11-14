namespace Yetta.Core;

public sealed record ConsoleContext(string Theme, string Version = "0.1.0-preview");
public sealed record ConsoleResult(string Message, string? NavigateTo = null,
    string? SwitchThemeTo = null, bool ClearOutput = false);

/// <summary>
/// An intentionally allowlisted Yetta command router. It does NOT invoke
/// PowerShell, CMD, subprocesses, registry writes, or privileged APIs.
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
                "help             Список команд\n" +
                "version          Версия приложения\n" +
                "status           Статус компонентов\n" +
                "modules          Раздел модулей\n" +
                "access           Раздел Access Manager\n" +
                "dax              Раздел DAX\n" +
                "settings         Настройки Yetta\n" +
                "theme fluent     Стиль Windows 11\n" +
                "theme glass      Стеклянный стиль\n" +
                "clear            Очистить вывод"),
            "version" or "about" => new ConsoleResult("Yetta " + context.Version +
                "\nБезопасный интерфейсный прототип. Системные модули не активны."),
            "status" => new ConsoleResult(
                "Yetta Core UI: работает\n" +
                "Access Manager: не установлен\n" +
                "DAX / DAX Guard: не установлены\n" +
                "Привилегированные операции: заблокированы\n" +
                "Текущая тема: " + context.Theme),
            "modules" => new ConsoleResult("Открываю каталог модулей.", NavigateTo: "modules"),
            "access" => new ConsoleResult("Открываю Access Manager (демонстрация).", NavigateTo: "access"),
            "dax" => new ConsoleResult("Открываю DAX (демонстрация).", NavigateTo: "dax"),
            "settings" => new ConsoleResult("Открываю настройки Yetta.", NavigateTo: "settings"),
            "theme fluent" => new ConsoleResult("Включён стиль Windows 11.", SwitchThemeTo: "Fluent"),
            "theme glass" => new ConsoleResult("Включён стиль Glass.", SwitchThemeTo: "Glass"),
            "clear" => new ConsoleResult(string.Empty, ClearOutput: true),
            "theme" => new ConsoleResult("Использование: theme fluent | theme glass"),
            _ => new ConsoleResult("Неизвестная команда: " + command +
                "\nДоступны только команды Yetta. Введите help.")
        };
    }
}
