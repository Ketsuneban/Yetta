using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;
using Yetta.Core;
using static Yetta.Core.DaxAdministratorProtocol;

namespace Yetta.DaxAdministrator;

internal sealed class AdministratorService : ServiceBase
{
    private CancellationTokenSource? _stop;

    internal AdministratorService()
    {
        ServiceName = DaxAdministratorProtocol.ServiceName;
        CanStop = true;
        AutoLog = false;
    }

    protected override void OnStart(string[] args)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User is not { } user || !Program.ServiceAccount.Equals(user) || !new WindowsPrincipal(identity).IsInRole(Program.ServiceSid))
            throw new InvalidOperationException("DAX Administrator must run as LocalService.");
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var grant = machine.OpenSubKey(RegistryPath);
        var sid = new SecurityIdentifier((string?)grant?.GetValue(OwnerValue) ??
            throw new InvalidOperationException("Administrator is not configured."));
        var access = new PipeSecurity();
        access.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        access.AddAccessRule(new PipeAccessRule(Program.ServiceSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        access.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));

        var pipe = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 256, 256, access);
        _stop = new CancellationTokenSource();
        _ = ListenAsync(pipe, sid, _stop.Token);
    }

    protected override void OnStop()
    {
        _stop?.Cancel();
        _stop?.Dispose();
        _stop = null;
    }

    private static async Task ListenAsync(NamedPipeServerStream pipe,
        SecurityIdentifier authorized, CancellationToken stop)
    {
        using (pipe)
        {
            while (!stop.IsCancellationRequested)
            {
                bool connected = false;
                try
                {
                    await pipe.WaitForConnectionAsync(stop);
                    connected = true;
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
                    deadline.CancelAfter(TimeSpan.FromSeconds(2));
                    var request = new byte[2];
                    await pipe.ReadExactlyAsync(request, deadline.Token);

                    SecurityIdentifier? caller = null;
                    pipe.RunAsClient(() =>
                    {
                        using var identity = WindowsIdentity.GetCurrent(ifImpersonating: true);
                        caller = identity?.User;
                    });

                    var result = request[0] == WireVersion && caller is not null && authorized.Equals(caller)
                        ? Decide(authorized, (DaxAdministratorOperation)request[1])
                        : DaxAdministratorState.NotGranted;
                    await pipe.WriteAsync(new byte[] { WireVersion, (byte)result }, deadline.Token);
                }
                catch (OperationCanceledException) when (!stop.IsCancellationRequested) { }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
                {
                    Audit("Ошибка запроса DAX: " + ex.GetType().Name, EventLogEntryType.Warning);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
                finally
                {
                    if (connected) pipe.Disconnect();
                }
            }
        }
    }

    private static DaxAdministratorState Decide(SecurityIdentifier caller, DaxAdministratorOperation operation)
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var grant = machine.OpenSubKey(RegistryPath);
        if (!string.Equals(grant?.GetValue(OwnerValue) as string, caller.Value, StringComparison.Ordinal))
            return DaxAdministratorState.NotGranted;

        using var state = grant!.OpenSubKey(StateKey, RegistryKeyPermissionCheck.ReadWriteSubTree,
            RegistryRights.ReadKey | RegistryRights.SetValue);
        if (state is null || state.GetValue(StateValue) is not int stored)
            return DaxAdministratorState.Unavailable;
        var previous = (DaxAdministratorState)stored;
        if (previous == DaxAdministratorState.Revoked) return previous;
        if (previous is not (DaxAdministratorState.Active or DaxAdministratorState.Inactive))
            return DaxAdministratorState.Unavailable;

        var next = operation switch
        {
            DaxAdministratorOperation.Status => previous,
            DaxAdministratorOperation.Enable => DaxAdministratorState.Active,
            DaxAdministratorOperation.Disable => DaxAdministratorState.Inactive,
            DaxAdministratorOperation.Revoke => DaxAdministratorState.Revoked,
            _ => DaxAdministratorState.Unavailable
        };
        if (next != previous && next != DaxAdministratorState.Unavailable)
        {
            state.SetValue(StateValue, (int)next);
            Audit($"{caller.Value}: {previous} -> {next}", EventLogEntryType.Information);
        }
        return next;
    }

    private static void Audit(string text, EventLogEntryType type)
    {
        try { EventLog.WriteEntry(Program.AuditSource, text, type, 100); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.Security.SecurityException) { }
    }
}
