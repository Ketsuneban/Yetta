using Yetta.Core;

static void Assert(bool condition, string description)
{
    if (!condition) throw new Exception("Smoke test failed: " + description);
    Console.WriteLine("PASS " + description);
}
var defaults = new AppSettings();
Assert(defaults.ActiveProfile == "Guest" && defaults.InstalledProfiles.SequenceEqual(["Guest"]),
    "Guest is the only initially installed profile");
Assert(!AccessProfiles.CanLaunchUnprivilegedTerminal(defaults), "Guest blocks external terminal");
Assert(AccessProfiles.Install(defaults,"SafeUser"),"SafeUser installs logical read-only policy");
Assert(AccessProfiles.Apply(defaults,"SafeUser"),"SafeUser can be selected");
Assert(!AccessProfiles.CanLaunchUnprivilegedTerminal(defaults),"SafeUser blocks external terminal");
Assert(AccessProfiles.Install(defaults,"User"),"User installs logical standard policy");
Assert(AccessProfiles.Apply(defaults,"User"),"User can be applied");
Assert(AccessProfiles.CanLaunchUnprivilegedTerminal(defaults),"User permits non-elevated Terminal");
Assert(!AccessProfiles.Install(defaults,"Administrator"),"Administrator cannot be falsely installed");
Assert(!AccessProfiles.Install(defaults,"System"),"System cannot be falsely installed");
Assert(!AccessProfiles.CanLaunchElevatedShell(defaults),"elevated shell not enabled");
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
    Assert(store.Load().ActiveProfile=="User" && store.Load().Theme=="Glass","settings persistence");
    File.WriteAllText(store.FilePath,"{corrupted");
    Assert(store.Load().ActiveProfile=="Guest","corrupted settings handled safely");
}
finally { if (Directory.Exists(dir)) Directory.Delete(dir,true); }

var c=new ConsoleContext("Glass","Guest");
Assert(ConsoleEngine.Execute(" HELP ",c).Message.Contains("recovery"),"Marvine help");
Assert(ConsoleEngine.Execute("theme fluent",c).SwitchThemeTo=="Fluent","theme command");
Assert(ConsoleEngine.Execute("guard",c).NavigateTo=="guard","guard navigation");
Assert(ConsoleEngine.Execute("activity",c).NavigateTo=="activity","activity navigation");
Assert(ConsoleEngine.Execute("/snob",c).Message.Contains("не повышает права"),"reserved command cannot elevate");
Assert(ConsoleEngine.Execute("clear",c).ClearOutput,"clear");
Assert(ConsoleEngine.Execute("powershell -command whoami",c).Message.Contains("не поддерживается"),
    "no arbitrary PowerShell");
Assert(ConsoleEngine.Execute("cmd /c del c:\\",c).Message.Contains("не поддерживается"),
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

Console.WriteLine("All Yetta Core smoke tests passed.");
