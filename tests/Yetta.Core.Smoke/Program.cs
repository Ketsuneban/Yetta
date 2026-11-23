using Yetta.Core;

static void Assert(bool condition, string description)
{
    if (!condition) throw new Exception("Smoke test failed: " + description);
    Console.WriteLine("PASS " + description);
}
var defaults = new AppSettings();
Assert(SubsyFrame.Parse("@subsy progress 42 Индексация") is { Current: 42, Total: 100, Label: "Индексация" },
    "Subsy displays an explicit progress update");
Assert(SubsyFrame.Parse("@subsy step 4/100 Проверка") is { Current: 4, Total: 100, IsStep: true },
    "Subsy distinguishes steps from percents");
Assert(SubsyFrame.Parse("@subsy done Всё готово") is { Finished: true, Label: "Всё готово" },
    "Subsy finishes the current task");
Assert(SubsyFrame.Parse("release 5/11") is null && SubsyFrame.Parse("@subsy progress 101") is null &&
    SubsyFrame.Parse("@subsy step 3/2") is null, "ordinary and invalid output stays text");
Assert(SubsyFrame.Parse("[####------] 40% Подготовка") is { Current: 40, Label: "Подготовка" },
    "legacy progress becomes a Subsy frame");
Assert(SubsyFrame.Parse("|") is { IsBusy: true } &&
    SubsyFrame.Parse("5/12 — Идёт загрузка") is { IsStep: true, Current: 5, Total: 12 },
    "spinner and labelled step formats work");
const string promptPrefix = SubsyPrompt.Prefix;
Assert(SubsyPrompt.Parse(promptPrefix + """{"id":"files","type":"multi","label":"Выбор","options":["a","b"],"limit":1}""")
    is { Kind: SubsyPromptKind.Multi, Limit: 1, Options.Length: 2 }, "Subsy accepts a bounded checkbox prompt");
Assert(SubsyPrompt.Parse(promptPrefix + """{"id":"x","type":"multi","label":"Выбор","options":["a"],"limit":2}""")
    is null, "Subsy rejects a limit above option count");
Assert(SubsyPrompt.Parse(promptPrefix + """{"id":"secret","type":"password","label":"Пароль"}""") is
    { Kind: SubsyPromptKind.Password, Reveal: false }, "Subsy masks passwords by default");
Assert(SubsyPrompt.Parse("[y/n/a/h]") is null &&
    SubsyPrompt.Parse(promptPrefix + """{"id":"x","type":"0","label":"Не тот тип"}""") is null,
    "ordinary terminal text and unknown prompt types cannot create an input");
var daxStatus = new DaxStatus(DaxState.Notice, "0.3.28", true, false);
Assert(DaxStatus.Parse(daxStatus.Serialize()) == daxStatus, "Tallow receives all DAX status fields");
Assert(DaxStatus.Parse("READY").State == DaxState.Incompatible &&
    DaxStatus.Parse("DAX/2|0|0.3.28|1|1").State == DaxState.Incompatible,
    "unrecognized or malformed DAX replies cannot become ready");
Assert(await DaxProtocol.ReadFrameAsync(new StringReader("TALLOW_STATUS_V2\r\n"), default) ==
    DaxProtocol.StatusRequest, "DAX reads a terminated frame");
Assert(await DaxProtocol.ReadFrameAsync(new StringReader("partial"), default) is null, "DAX rejects an incomplete frame");
try
{
    _ = await DaxProtocol.ReadFrameAsync(new StringReader(new string('x', 1000)), default);
    throw new Exception("Oversized DAX message was accepted.");
}
catch (InvalidDataException) { Console.WriteLine("PASS bounded DAX frame"); }
Assert(defaults.ActiveProfile == "Guest" && defaults.InstalledProfiles.SequenceEqual(["Guest"]),
    "Guest is the only initially installed profile");
Assert(!AccessProfiles.Install(defaults,"SafeUser"),"SafeUser is merged into Guest");
defaults.ActiveProfile = "SafeUser";
defaults.InstalledProfiles.Add("SafeUser");
defaults.Normalize();
Assert(defaults.ActiveProfile == "Guest" && !defaults.InstalledProfiles.Contains("SafeUser"),
    "old SafeUser preference migrates to Guest");
Assert(AccessProfiles.Install(defaults,"User"),"User installs logical standard policy");
Assert(AccessProfiles.Apply(defaults,"User"),"User can be applied");
Assert(!AccessProfiles.Install(defaults,"Administrator"),"Administrator cannot be falsely installed");
var adminCache = new AppSettings { InstalledProfiles = ["Guest", "Administrator"], ActiveProfile = "Administrator" };
AccessProfiles.SyncAdministrator(adminCache, DaxAdministratorState.Unavailable);
Assert(adminCache.ActiveProfile == "Guest" && !adminCache.InstalledProfiles.Contains("Administrator"),
    "unverified Administrator preferences are ignored");
AccessProfiles.SyncAdministrator(adminCache, DaxAdministratorState.Inactive);
Assert(!AccessProfiles.Apply(adminCache, "Administrator"), "local preferences cannot activate the installed grant");
AccessProfiles.SyncAdministrator(adminCache, DaxAdministratorState.Active);
Assert(adminCache.ActiveProfile == "Administrator", "confirmed service state activates Administrator");
AccessProfiles.SyncAdministrator(adminCache, DaxAdministratorState.Revoked);
Assert(adminCache.ActiveProfile == "Guest", "revocation removes the active profile immediately");
Assert(!AccessProfiles.Install(defaults,"System"),"System cannot be falsely installed");
Assert(!AccessProfiles.Apply(defaults,"System"),"non-installed level cannot be applied");
defaults.ActiveProfile = "System";
defaults.InstalledProfiles.Add("System");
defaults.Normalize();
Assert(defaults.ActiveProfile=="Guest" && !defaults.InstalledProfiles.Contains("System"),
    "tampered local settings cannot grant SYSTEM");
defaults.Theme = "random";
defaults.Normalize();
Assert(defaults.Theme=="Fluent","invalid theme normalized");

string dir = Path.Combine(Path.GetTempPath(),"yetta-smoke-"+Guid.NewGuid().ToString("N"));
try
{
    var store = new SettingsStore(Path.Combine(dir,"settings.json"));
    Assert(store.Load().ActiveProfile=="Guest","safe defaults without config");
    store.Save(new AppSettings { Theme="Glass", InstalledProfiles=["Guest","User"], ActiveProfile="User"});
    Assert(store.Load().ActiveProfile=="User" && store.Load().Theme=="Fluent","single theme migration");
    File.WriteAllText(store.FilePath,"{corrupted");
    Assert(store.Load().ActiveProfile=="Guest","corrupted settings handled safely");
}
finally { if (Directory.Exists(dir)) Directory.Delete(dir,true); }

var c=new ConsoleContext("Glass","Guest");
Assert(ConsoleEngine.Execute(" HELP ",c).Message.Contains("/script run|list|stop"),"Marvine help");
Assert(ConsoleEngine.Execute("theme fluent",c).SwitchThemeTo is null,"theme switching disabled");
Assert(ConsoleEngine.Execute("guard",c).Message.Contains("Неизвестная команда"),"guard navigation");
Assert(ConsoleEngine.Execute("activity",c).NavigateTo=="activity","activity navigation");
Assert(ConsoleEngine.Execute("/snob", c).Message.Contains("install|uninstall|check"), "Snob commands are discoverable");
Assert(ConsoleEngine.Execute("snob uninstall", c) is { Request: "SNOB_UNINSTALL", Argument: null }, "catalog opens without a package ID");
Assert(ConsoleEngine.Execute("snob install Yetta.yetta", c) is { Request: "SNOB_INSTALL", Argument: "Yetta.yetta" }, "Yetta package ID");
Assert(ConsoleEngine.Execute("snob check \"Visual Studio Code\"", c) is { Request: "SNOB_CHECK", Argument: "Visual Studio Code" }, "quoted package name");
Assert(ConsoleEngine.Execute("snob yetta exit", c) is { Request: "SNOB_YETTA", Argument: "exit" }, "close and exit route separately");
Assert(ConsoleEngine.Execute("help", c).Request == "HELP" && ConsoleEngine.Execute("help", c with { SubsyEnabled = false }).Request == "HELP",
    "help keeps its rendering route in both console modes");
Assert(WarningTestCommand.TryParse("test warn Fatal", out var fatalPreview) && fatalPreview is { Password: false, Tty: false, Check: false } &&
    !WarningTestCommand.TryParse("test warn Fatal Yetta check:true", out _) && CommandCompletion.Get("test warn Fatal ").Count == 0,
    "fatal warnings accept no extra arguments");
Assert(ConsoleEngine.Execute("clear",c).ClearOutput,"clear");
Assert(ConsoleEngine.Execute("powershell -command whoami",c).Message.Contains("Неизвестная команда"),
    "no arbitrary PowerShell");
Assert(ConsoleEngine.Execute("cmd /c del c:\\",c).Message.Contains("Неизвестная команда"),
    "no arbitrary CMD");
Assert(WarningPolicy.StatusIcon(YettaWarningLevel.Information,false)==27,"safe icon 27");
Assert(WarningPolicy.StatusIcon(YettaWarningLevel.Dangerous,false)==26,"danger icon 26");
Assert(WarningPolicy.StatusIcon(YettaWarningLevel.Dangerous,true)==35,"safe-mode icon 35");
Assert(WarningPolicy.RequiresAdditionalAcknowledgement(YettaWarningLevel.Consent),"consent checkbox required");

var clock = DateTimeOffset.Parse("2026-11-15T00:00:00Z");
var warnings = new TallowWarningQueue<string>(() => clock);
var admitted = new List<Guid>();
for (int i = 0; i < 5; i++)
{
    var admission = warnings.Submit("warning-" + i, "subject " + i);
    Assert(admission.Result == TallowWarningQueue<string>.Outcome.Show && admission.Item is not null,
        "first five warnings open separately");
    admitted.Add(admission.Item!.Id);
}
var queued = warnings.Submit("queued-1", "sixth warning");
Assert(queued.Result == TallowWarningQueue<string>.Outcome.Queued && warnings.PendingCount == 1,
    "sixth warning goes to bounded queue");
Assert(warnings.Submit("queued-1", "duplicate").Result == TallowWarningQueue<string>.Outcome.Suppressed,
    "duplicate queued warning suppressed");
var promoted = warnings.Close(admitted[0]);
Assert(promoted?.Id == queued.Item!.Id && warnings.VisibleCount == 5 && warnings.PendingCount == 0,
    "closing a warning promotes oldest queued item");
var later = warnings.Submit("queued-2", "new warning");
Assert(later.Result == TallowWarningQueue<string>.Outcome.Queued, "overflow remains queued");
Assert(warnings.RejectQueued(later.Item!.Id) && warnings.PendingCount == 0,
    "queue denial discards only selected pending request");
var fatal = warnings.Submit("fatal", "fatal warning", fatal: true);
Assert(fatal.Result == TallowWarningQueue<string>.Outcome.Fatal &&
    warnings.VisibleCount == 1 && warnings.PendingCount == 0 && warnings.FatalVisible,
    "fatal warning preempts all windows and queue");
Assert(warnings.Submit("blocked", "must not open").Result ==
    TallowWarningQueue<string>.Outcome.BlockedByFatal, "fatal window has exclusive priority");
warnings.Close(fatal.Item!.Id);
Assert(!warnings.FatalVisible && warnings.VisibleCount == 0, "closing fatal does not perform recovery");
clock = clock.AddSeconds(6);
Assert(warnings.Submit("warning-0", "retry").Result == TallowWarningQueue<string>.Outcome.Show,
    "explicit request may be repeated after cooling off");


string activityDirectory = Path.Combine(Path.GetTempPath(), "yetta-activity-test-" + Guid.NewGuid().ToString("N"));
try
{
    string path = Path.Combine(activityDirectory, "activity.jsonl");
    var now = DateTimeOffset.Parse("2026-11-15T16:00:00Z");
    var history = new TallowNotificationStore(path, () => now);
    history.Add("Tallow", "First", 24);
    history.Add("DAX", "Second", 24);
    Assert(history.Load().Count == 2, "activity persists across reads");
    File.AppendAllText(path, "{ invalid JSON" + Environment.NewLine);
    Assert(history.Load().Count == 2, "corrupt activity entry is skipped");
    now = now.AddHours(25);
    Assert(history.Load().Count == 0, "activity expires after default 24 hours");
    Assert(!File.ReadAllText(path).Contains("First", StringComparison.Ordinal),
        "expired activity is removed from persistence");
}
finally { if (Directory.Exists(activityDirectory)) Directory.Delete(activityDirectory, true); }

Assert(TallowText.All.Count >= 300, "shared text catalog loads migrated UI strings");
Assert(TallowText.All.Values.Contains("Разрешить"), "Russian warning action available");
Assert(TallowText.All.Values.Contains("Windows, on your terms."), "existing English UI text available");
Assert(TallowText.All.Values.Contains("Проверить"), "DAX tray text available");

// Hot catalog updates: the JSON changes without recompiling or restarting.
var catalogOriginal = new Dictionary<string, string>(TallowText.All);
var textKey = catalogOriginal.First(x => x.Value == "Разрешить").Key;
var updated = new Dictionary<string, string>(catalogOriginal)
{
    [textKey] = "Тест: разрешить"
};
int events = 0;
void OnTextsChanged(object? sender, TextCatalogChangedEventArgs args)
{
    events++;
    Assert(args.Current[textKey] == TallowText.Get(textKey), "catalog notification reflects active text");
}
TallowText.Changed += OnTextsChanged;
try
{
    Assert(TallowText.TryUpdate(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(updated)),
        "valid catalog is applied without process restart");
    Assert(TallowText.Get(textKey) == "Тест: разрешить", "changed label becomes visible immediately");
    Assert(!TallowText.TryUpdate(System.Text.Encoding.UTF8.GetBytes("{}")),
        "incomplete catalog is rejected");
    Assert(!TallowText.TryUpdate(System.Text.Encoding.UTF8.GetBytes("{bad")),
        "broken JSON is rejected");
    Assert(TallowText.Get(textKey) == "Тест: разрешить",
        "invalid catalogs preserve last valid copy");
    Assert(TallowText.TryUpdate(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(catalogOriginal)),
        "original catalog restored");
    Assert(events == 2, "only actual catalog changes notify listeners");
}
finally
{
    TallowText.Changed -= OnTextsChanged;
    TallowText.TryUpdate(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(catalogOriginal));
}

Console.WriteLine("All Yetta Core smoke tests passed.");

// Owner supplied 18-icon taxonomy, independent of severity.
var causeCases = new (string Path, int Icon)[] {
    ("unknown.xyz",1), ("README",1), ("image.PNG",4), ("song.flac",5), ("movie.mkv",6),
    ("note.txt",7), ("run.bat",8), ("run.PY",8), ("run.pyw",8), ("app.exe",8), ("backup.7z",14), ("settings.reg",15)
};
foreach (var test in causeCases)
    Assert(WarningCause.Icon(WarningCauseType.File, new(test.Path)) == test.Icon, "cause: " + test.Path);
Assert(WarningCause.Icon(WarningCauseType.File, new("note.txt", IsImportant:true)) == 7, "importance no longer overrides file type");
Assert(WarningCause.Icon(WarningCauseType.File, new("note.txt", IsProtected:true)) == 9, "protected file");
Assert(WarningCause.Icon(WarningCauseType.File, new("settings.json", IsProtected:true, IsWindowsConfiguration:true)) == 15, "configuration before protection");
Assert(WarningCause.Icon(WarningCauseType.File, new("run.py", IsCloudOnly:true)) == 10, "cloud before extension");
Assert(WarningCause.Icon(WarningCauseType.File, new("folder.zip", IsDirectory:true)) == 2, "folder before extension");
Assert(WarningCause.Icon(WarningCauseType.Application, new("unknown.exe")) == 3, "application fallback");
Assert(WarningCause.Icon(WarningCauseType.Application, new(@"C:\Windows\System32\cmd.exe")) == 12, "terminal application");
Assert(WarningCause.Icon(WarningCauseType.Terminal) == 12, "terminal cause");
Assert(WarningCause.Icon(WarningCauseType.Windows, windowsBuild:22000) == 11 &&
    WarningCause.Icon(WarningCauseType.Windows, windowsBuild:19045) == 13, "Windows 11 and legacy icons");
Assert(WarningCause.Icon(WarningCauseType.Yetta) == 16 && WarningCause.Icon(WarningCauseType.Dax) == 17 &&
    WarningCause.Icon(WarningCauseType.Tallow) == 18, "component icons");
const string preview = "/test warn Critical \"C:\\My Files\\demo.py\" \"Тест с пробелами\" pass:true tty:false check:true";
Assert(WarningTestCommand.TryParse(preview, out var parsed) && parsed is { Password: true, Tty: false, Check: true } &&
    parsed.Cause == @"C:\My Files\demo.py", "warning parser preserves quoted Windows paths and flags");
Assert(!WarningTestCommand.TryParse(preview.Replace("pass:true", "pass:maybe"), out _) &&
    !WarningTestCommand.TryParse(preview + " pass:false", out _) &&
    !WarningTestCommand.TryParse(preview.Replace("Critical", "99"), out _), "invalid previews cannot open windows");
Assert(ConsoleEngine.Execute(preview, c).Request == "TEST_WARN", "Marvine dispatches warning preview");

Assert(WarningTestCommand.TryParse("/test warn Standard Yetta", out var simple) &&
    simple is { Password: false, Tty: false, Check: false } && simple.Text.Length > 0, "minimal preview has defaults");
Assert(WarningTestCommand.TryParse("/test warn Dax", out var noCause) && noCause?.Cause == "Yetta", "cause can default to Yetta");
Assert(WarningTestCommand.TryParse("/test warn Caution Tallow \"Описание с пробелами\" check:true pass:true", out var flexible) &&
    flexible is { Check: true, Password: true, Tty: false, Text: "Описание с пробелами" }, "optional flags work in any order");
Assert(CommandCompletion.Get("/test").Select(x => x.Label).SequenceEqual(["warn"]), "test has only warn subcommand");
Assert(CommandCompletion.Get("/test warn").Count == WarningTestCommand.Kinds.Length, "warning kinds are offered at the next step");
Assert(CommandCompletion.Get("/test warn C").All(x => x.Label.StartsWith("C")), "partial warning type filters suggestions");
Assert(CommandCompletion.Get("/test warn Caution").Any(x => x.Label == "Tallow"), "cause choices follow a complete type");
Assert(CommandCompletion.Get("/test warn Caution Yetta check:true ").All(x => !x.Label.StartsWith("check:")), "used flags are not offered twice");
Assert(CommandCompletion.Get("/test warn Caution Yetta tty:").Select(x => x.Label).SequenceEqual(["tty:true", "tty:false"]), "boolean flag gets exact alternatives");

Assert(ConsoleEngine.Execute("yetta upd", c).Request == "UPDATE" && ConsoleEngine.Execute("/yetta update", c).Request == "UPDATE", "slash optional and update alias");
Assert(ConsoleEngine.Execute("yetta ver", c).Request == "VERSION" && ConsoleEngine.Execute("yetta dl v0.4.2-preview", c).Argument == "v0.4.2-preview", "version and download aliases");
Assert(ConsoleEngine.Execute("marv colors dracula", c) is { Request: "COLORSET", Argument: "dracula" }, "Dracula colorset command and aliases");
Assert(ConsoleEngine.Execute("/clear", c).ClearOutput && ConsoleEngine.Execute("clear", c).ClearOutput, "plain commands also accept slash");
Assert(WarningTestCommand.TryParse("test warn Standard \"C:\\My Files\\Tallow.py\" \"yetta upd must remain text\"", out var aliasTest) &&
    aliasTest?.Text == "yetta upd must remain text" && aliasTest.Cause == @"C:\My Files\Tallow.py", "aliases never rewrite paths or warning text");
Assert(CommandCompletion.Get("test").Select(x => x.Label).SequenceEqual(["warn"]), "slashless completion follows same grammar");
var dracula = new AppSettings { MarvineColorset = "dracula" }; dracula.Normalize();
Assert(dracula.MarvineColorset == "dracula", "Dracula persists in settings");
Assert(WarningStatePolicy.Resolve(true, false, true) == WarningState.SafeMode && WarningStatePolicy.Resolve(true, true, true) == WarningState.SafeMode,
    "Windows safe mode takes priority");
Assert(WarningStatePolicy.Resolve(true, true, false) == WarningState.Danger && WarningStatePolicy.Resolve(true, false, false) == WarningState.Safe,
    "decision states distinguish danger from safety");
Assert(WarningStatePolicy.Resolve(false, true, true) == WarningState.None, "informational events have no state");

Assert(ConsoleEngine.Execute("scr   run", c).Request == "SCRIPT_RUN", "script picker through spaced alias");
Assert(ConsoleEngine.Execute(@"script   run   ""C:\My  Scripts\A.py""", c).Argument == @"""C:\My  Scripts\A.py""", "preserve quoted filename bytes");
foreach (var removed in new[] { "win", "dax", "settings", "guard", "info", "status", "logs", "powershell" })
    Assert(ConsoleEngine.Execute(removed, c).Message.Contains("Неизвестная команда"), "removed " + removed);
Assert(CommandCompletion.Get("").Count == 0 && CommandCompletion.Get("/").Count == 0, "quiet initial prompt");
Assert(ConsoleEngine.Execute("help", c).Message.Split("/script").Length == 2, "one script help entry");
Assert(!WarningTestCommand.TryParse("test warn Standard Yetta check:true late", out _), "description precedes switches");
Assert(ConsoleEngine.Execute("test warn Unknown Yetta sample", c).Message.Contains("«Unknown»"), "report the offending token");
Assert(CommandCompletion.Get("script ru").Select(s => s.Label).SequenceEqual(["run"]), "finish the action before offering a path");

Assert(ConsoleEngine.Execute(@"marvine open ""C:\Work\My Folder""", c) is
    { Request: "MARVINE_OPEN", Argument: @"""C:\Work\My Folder""" }, "Marvine preserves a quoted workspace path");
Assert(ConsoleEngine.Execute("marvine open ..", c) is { Request: "MARVINE_OPEN", Argument: ".." } &&
    CommandCompletion.Get("marvine op").Any(item => item.Label == "open"), "relative workspaces and completion");

foreach (var profile in new[] { "Guest", "User", "Administrator", "System", "unknown" })
{
    Assert(DaxPolicy.Decide(DaxOperation.StopScript, profile, DaxAdministratorState.Revoked, DaxState.Unavailable).Allowed,
        "owned script stop remains available: " + profile);
    Assert(!DaxPolicy.Decide(DaxOperation.RunScript, profile, DaxAdministratorState.Revoked, DaxState.Ready).Allowed,
        "revoked grant denies script: " + profile);
}
Assert(!DaxPolicy.Decide(DaxOperation.ManagePackages, "Guest", DaxAdministratorState.Active, DaxState.Ready).Allowed, "Guest denies package changes");
Assert(DaxPolicy.Decide(DaxOperation.ManagePackages, "User", DaxAdministratorState.Unavailable, DaxState.Unavailable).Allowed, "User package policy");
Assert(DaxPolicy.Decide(DaxOperation.RunScript, "Administrator", DaxAdministratorState.Active, DaxState.Ready).Allowed, "verified script policy");
Assert(!DaxPolicy.Decide(DaxOperation.RunScript, "Administrator", DaxAdministratorState.Active, DaxState.Unavailable).Allowed, "offline agent denies script");
