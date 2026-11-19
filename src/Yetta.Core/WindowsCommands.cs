using System.Globalization;

namespace Yetta.Core;

public sealed record WindowsPreference(string Key, string Label, int Minimum, int Maximum, bool Toggle = false)
{
    public string Values => Toggle ? "on|off" : $"{Minimum}–{Maximum}";
    public string Format(int value) => Toggle ? (value == 0 ? "off" : "on") : value.ToString(CultureInfo.InvariantCulture);
}

public static class WindowsCommands
{
    public static readonly WindowsPreference[] Preferences =
    [
        new("mouse-speed", "Скорость указателя", 1, 20),
        new("key-speed", "Скорость повтора клавиши", 0, 31),
        new("key-delay", "Задержка повтора клавиши", 0, 3),
        new("animations", "Анимации элементов Windows", 0, 1, true),
        new("drag-content", "Содержимое окна при перетаскивании", 0, 1, true)
    ];

    public static readonly IReadOnlyDictionary<string, string> Pages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["background"] = "personalization-background", ["colors"] = "personalization-colors", ["themes"] = "themes",
        ["taskbar"] = "taskbar", ["start"] = "personalization-start", ["lockscreen"] = "lockscreen",
        ["fonts"] = "fonts", ["display"] = "display", ["sound"] = "sound", ["mouse"] = "mousetouchpad",
        ["notifications"] = "notifications", ["night-light"] = "nightlight"
    };

    public static string Help => "win <параметр> — текущее значение. Добавь значение, чтобы изменить его.\n" +
        string.Join("\n", Preferences.Select(p => $"win {p.Key} [{p.Values}] — {p.Label}")) +
        "\nwin wallpaper [\"путь\"] — установить изображение на все мониторы" +
        "\nwin open <раздел> — открыть параметры Windows\n" + string.Join(", ", Pages.Keys);

    public static ConsoleResult Parse(string raw)
    {
        var args = CommandTokens.Read(raw, out bool closed);
        if (!closed) return new("[⚠] Закрой кавычки вокруг пути.");
        if (args.Count == 1 || args.Count == 2 && args[1].Value.Equals("help", StringComparison.OrdinalIgnoreCase))
            return new(Help);
        string name = args[1].Value.ToLowerInvariant();
        if (name == "open")
            return args.Count == 3 && Pages.TryGetValue(args[2].Value, out string? page)
                ? new("", Request: "WIN_OPEN", Argument: page)
                : new("win open <раздел>\n" + string.Join(", ", Pages.Keys));
        if (name == "wallpaper")
            return args.Count <= 3 ? new("", Request: "WIN_WALLPAPER", Argument: args.Count == 3 ? args[2].Value : null)
                : new("win wallpaper [\"путь к изображению\"]");
        var pref = Preferences.FirstOrDefault(p => p.Key == name);
        if (pref is null) return new(Help);
        if (args.Count == 2) return new("", Preference: pref);
        var value = args[2].Value.ToLowerInvariant();
        int number = 0;
        bool valid = pref.Toggle ? value is "on" or "off" :
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number >= pref.Minimum && number <= pref.Maximum;
        return args.Count == 3 && valid
            ? new("", Preference: pref, SettingValue: pref.Toggle ? (value == "on" ? 1 : 0) : number)
            : new($"[⚠] win {pref.Key} [{pref.Values}]");
    }
}
