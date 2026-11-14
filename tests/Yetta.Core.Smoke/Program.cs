using Yetta.Core;

static void Assert(bool condition, string description)
{
    if (!condition) throw new Exception("Smoke test failed: " + description);
    Console.WriteLine("PASS " + description);
}

var initial = new AppSettings();
Assert(initial.Theme == "Fluent", "default visual mode");
initial.Theme = "unexpected";
initial.Normalize();
Assert(initial.Theme == "Fluent", "invalid theme is rejected");

string location = Path.Combine(Path.GetTempPath(), "yetta-smoke-" +
    Guid.NewGuid().ToString("N"), "settings.json");
try
{
    var store = new SettingsStore(location);
    Assert(store.Load().Theme == "Fluent", "missing file defaults safely");
    store.Save(new AppSettings { Theme = "Glass" });
    Assert(store.Load().Theme == "Glass", "theme persists across instances");
    File.WriteAllText(location, "{corrupted");
    Assert(store.Load().Theme == "Fluent", "corrupted settings recover");
}
finally
{
    var directory = Path.GetDirectoryName(location);
    if (directory is not null && Directory.Exists(directory))
        Directory.Delete(directory, recursive: true);
}

var context = new ConsoleContext("Glass");
Assert(ConsoleEngine.Execute("  HELP ", context).Message.Contains("theme glass"),
    "help command is case-insensitive");
Assert(ConsoleEngine.Execute("theme fluent", context).SwitchThemeTo == "Fluent",
    "allowlisted theme change");
Assert(ConsoleEngine.Execute("access", context).NavigateTo == "access",
    "allowlisted page navigation");
Assert(ConsoleEngine.Execute("clear", context).ClearOutput,
    "clear command");
Assert(ConsoleEngine.Execute("del /s /q c:\\", context).Message.Contains("Неизвестная команда"),
    "arbitrary shell command is rejected");
Assert(ConsoleEngine.Execute("powershell -command whoami", context).Message.Contains("Неизвестная команда"),
    "PowerShell is not exposed");

Console.WriteLine("Yetta Core smoke tests passed.");
