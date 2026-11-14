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
Assert(WarningPolicy.RequiresTty(YettaWarningLevel.Critical,"User"),
    "critical operation requires additional TTY key");
Assert(!WarningPolicy.RequiresTty(YettaWarningLevel.Standard,"User"),
    "standard operation cannot require TTY key unexpectedly");
Assert(WarningPolicy.StatusIcon(YettaWarningLevel.Standard,false,"Guest")==35,
    "guest warning status uses restricted mode icon");
Assert(WarningPolicy.HeaderIcon(YettaWarningLevel.Dax,daxDenied:true)==33,
    "owner-supplied DAX blocked icon");
Assert(WarningPolicy.SubjectIcon(WarningSubjectType.PythonFile)==37,
    "owner-supplied Python object icon");
Assert(WarningPolicy.SubjectIcon(WarningSubjectType.Archive)==44,
    "owner-supplied archive object icon");
Assert(ConsoleEngine.Execute("process",c).NavigateTo=="process",
    "new DAX Process navigation command");
Assert(ConsoleEngine.Execute("permissions",c).Message.Contains("не повышает"),
    "Marvine permission command cannot elevate");

Console.WriteLine("All Yetta Core smoke tests passed.");
