using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Yetta.Core;
using static Yetta.Core.DaxAdministratorProtocol;

namespace Yetta.Desktop;

internal static class DaxAdministratorClient
{
    private const uint QueryStatus = 4;
    private const int ServiceRunning = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint Type, State, Controls, ExitCode, ServiceExitCode, Checkpoint, WaitHint, ProcessId, Flags;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr manager, string service, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(IntPtr service, int level, out ServiceStatus status, int size, out int needed);

    [DllImport("advapi32.dll")]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    internal static async Task<DaxAdministratorState> RequestAsync(DaxAdministratorOperation operation, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var (state, processId) = ReadService();
            if (processId == 0) return state;
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeAccessRights.ReadWrite,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Identification, HandleInheritability.None);
            await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
            // The kernel identifies the pipe peer. SCM independently identifies the real service.
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint actual) || actual != processId ||
                ReadService().ProcessId != actual)
                return DaxAdministratorState.Unavailable;
            await pipe.WriteAsync(new byte[] { WireVersion, (byte)operation }, deadline.Token).ConfigureAwait(false);
            var response = new byte[2];
            await pipe.ReadExactlyAsync(response, deadline.Token).ConfigureAwait(false);
            return response[0] == WireVersion && Enum.IsDefined((DaxAdministratorState)response[1])
                ? (DaxAdministratorState)response[1] : DaxAdministratorState.Unavailable;
        }
        catch (UnauthorizedAccessException) { return DaxAdministratorState.NotGranted; }
        catch (Exception ex) when (ex is IOException or TimeoutException or Win32Exception or
            OperationCanceledException or System.Security.SecurityException)
        {
            if (token.IsCancellationRequested) throw new OperationCanceledException(token);
            return DaxAdministratorState.Unavailable;
        }
    }

    private static (DaxAdministratorState State, uint ProcessId) ReadService()
    {
        IntPtr manager = OpenSCManager(null, null, 1);
        if (manager == IntPtr.Zero) return (DaxAdministratorState.Unavailable, 0);
        try
        {
            IntPtr service = OpenService(manager, ServiceName, QueryStatus);
            if (service == IntPtr.Zero)
                return (Marshal.GetLastWin32Error() == 1060 ? DaxAdministratorState.NotInstalled : DaxAdministratorState.Unavailable, 0);
            try
            {
                if (QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<ServiceStatus>(), out _) &&
                    status.State == ServiceRunning)
                    return (DaxAdministratorState.Unavailable, status.ProcessId);
                return (DaxAdministratorState.Unavailable, 0);
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }
}
