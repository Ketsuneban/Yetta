using System.ComponentModel;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;
using Yetta.Core;
using static Yetta.Core.DaxAdministratorProtocol;

namespace Yetta.DaxAdministrator;

internal static class Program
{
    private const string Account = @"NT AUTHORITY\LocalService";
    internal const string AuditSource = "Yetta DAX Administrator";
    internal static readonly SecurityIdentifier ServiceAccount = new(WellKnownSidType.LocalServiceSid, null);
    internal static SecurityIdentifier ServiceSid => (SecurityIdentifier)new NTAccount("NT SERVICE", ServiceName)
        .Translate(typeof(SecurityIdentifier));
    private static string Executable => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Yetta DAX", "Yetta.DAX.Administrator.exe");
    private static string ImagePath => '"' + Executable + '"';
    private static string ServiceKey => @"SYSTEM\CurrentControlSet\Services\" + ServiceName;

    public static int Main(string[] args)
    {
        if (args.Length == 0 && !Environment.UserInteractive)
        {
            using var service = new AdministratorService();
            ServiceBase.Run(service);
            return 0;
        }

        using var identity = WindowsIdentity.GetCurrent();
        if (args.Length != 1 || !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)
            || !string.Equals(Environment.ProcessPath, Executable, StringComparison.OrdinalIgnoreCase))
            return 1;

        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            bool installed = IsManagedService(machine);
            switch (args[0])
            {
                case "--stop":
                    if (installed) StopService();
                    return 0;
                case "--configure":
                    Configure(machine, identity.User!, installed);
                    return 0;
                case "--remove":
                    if (installed)
                    {
                        StopService();
                        RunSc("delete", ServiceName);
                    }
                    machine.DeleteSubKeyTree(RegistryPath, throwOnMissingSubKey: false);
                    if (EventLog.SourceExists(AuditSource)) EventLog.DeleteEventSource(AuditSource);
                    return 0;
                default:
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static void Configure(RegistryKey machine, SecurityIdentifier owner, bool installed)
    {
        if (installed) StopService();
        if (!EventLog.SourceExists(AuditSource))
            EventLog.CreateEventSource(AuditSource, "Application");

        // Mark the grant revoked before changing the owner. A partial install fails closed.
        if (!installed)
            RunSc("create", ServiceName, "binPath=", ImagePath, "obj=", Account,
                "start=", "auto", "DisplayName=", AuditSource);
        RunSc("sidtype", ServiceName, "unrestricted");
        using var grant = machine.CreateSubKey(RegistryPath);
        grant.SetAccessControl(GrantAcl(RegistryRights.ReadKey));
        using var state = grant.CreateSubKey(StateKey);
        state.SetAccessControl(GrantAcl(RegistryRights.ReadKey | RegistryRights.SetValue));
        state.SetValue(StateValue, (int)DaxAdministratorState.Revoked);
        grant.SetValue(OwnerValue, owner.Value);
        try
        {
            state.SetValue(StateValue, (int)DaxAdministratorState.Active);
            using var service = new ServiceController(ServiceName);
            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(12));
        }
        catch
        {
            state.SetValue(StateValue, (int)DaxAdministratorState.Revoked);
            throw;
        }
    }

    private static RegistrySecurity GrantAcl(RegistryRights serviceRights)
    {
        var security = new RegistrySecurity();
        security.SetAccessRuleProtection(true, false);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.SetOwner(admins);
        security.AddAccessRule(new RegistryAccessRule(admins, RegistryRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new RegistryAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), RegistryRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new RegistryAccessRule(ServiceSid, serviceRights, AccessControlType.Allow));
        return security;
    }

    private static bool IsManagedService(RegistryKey machine)
    {
        using var service = machine.OpenSubKey(ServiceKey);
        if (service is null) return false;
        if (!string.Equals(service.GetValue("ImagePath") as string, ImagePath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(service.GetValue("ObjectName") as string, Account, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Another service is already using the DAX Administrator name.");
        return true;
    }

    private static void StopService()
    {
        using var service = new ServiceController(ServiceName);
        if (service.Status == ServiceControllerStatus.Stopped) return;
        if (service.Status != ServiceControllerStatus.StopPending) service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(12));
    }

    private static void RunSc(params string[] args)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start sc.exe.");
        if (!process.WaitForExit(10000))
        {
            process.Kill();
            throw new System.TimeoutException("Service configuration timed out.");
        }
        if (process.ExitCode != 0) throw new Win32Exception(process.ExitCode);
    }
}
