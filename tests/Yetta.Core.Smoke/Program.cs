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
Assert(WarningPolicy.HeaderIcon(YettaWarningLevel.Information)==22, "standard warning icon");
Assert(WarningPolicy.HeaderIcon(YettaWarningLevel.Consent)==2, "agreement icon");
Assert(WarningPolicy.HeaderIcon(YettaWarningLevel.Caution)==23, "security caution icon");
Assert(WarningPolicy.HeaderIcon(YettaWarningLevel.Dangerous)==24, "dangerous files and Windows icon");
Assert(WarningPolicy.HeaderIcon(YettaWarningLevel.Critical)==25, "critical system warning icon");
Assert(WarningPolicy.HeaderIcon(YettaWarningLevel.Fatal)==48, "fatal warning icon");
Assert(WarningPolicy.HeaderIcon(YettaWarningLevel.Dax)==33, "DAX notice icon");
Assert(WarningPolicy.StatusIcon(YettaWarningLevel.Fatal,false)==0,"fatal has no status badge");
Assert(WarningPolicy.StatusIcon(YettaWarningLevel.Information,false,"Guest")==35,
    "Guest warning always shows restricted status");
Assert(WarningPolicy.StatusIcon(YettaWarningLevel.Dax,false,"User")==35,
    "DAX notification status is restricted");
Assert(WarningPolicy.RequiresTty(YettaWarningLevel.Critical,"Guest"),"critical always requires TTY");
Assert(WarningPolicy.RequiresTty(YettaWarningLevel.Information,"System"),"SYSTEM requires TTY");
Assert(!WarningPolicy.RequiresTty(YettaWarningLevel.Information,"User"),"USER does not require TTY");
Assert(!WarningPolicy.CanApprove(false,true,true,false),"unchecked agreement never approves");
Assert(!WarningPolicy.CanApprove(true,false,true,true),"invalid TTY never approves");
Assert(WarningPolicy.CanApprove(true,true,true,true),"checked agreement and valid TTY approve UI");
Assert(WarningPolicy.ObjectIcon(WarningObjectKind.PythonScript)==37,"Python icon");
Assert(WarningPolicy.ObjectIcon(WarningObjectKind.Folder)==43,"folder icon");
Assert(WarningPolicy.ObjectIcon(WarningObjectKind.Unknown)==46,"unknown icon");
Console.WriteLine("All Yetta Core smoke tests passed.");
