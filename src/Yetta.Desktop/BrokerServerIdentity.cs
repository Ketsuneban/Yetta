using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Yetta.Core;

namespace Yetta.Desktop;

/// <summary>
/// Verify the pipe's peer PID against the REAL Service Control Manager process
/// before trusting any status or response. A local fake pipe cannot activate
/// the Administrator UI profile.
/// </summary>
internal static class BrokerServerIdentity
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const uint ServiceRunning = 0x00000004;

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
        public uint ProcessId;
        public uint ServiceFlags;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(
        SafePipeHandle pipe, out uint serverProcessId);

    [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(
        string? machineName, string? databaseName, uint access);

    [DllImport("advapi32.dll", EntryPoint = "OpenServiceW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(
        IntPtr manager, string serviceName, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatusEx(
        IntPtr service, int infoLevel,
        out ServiceStatusProcess status, int bufferSize, out int requiredSize);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    internal static bool IsInstalledServicePipe(SafePipeHandle pipe)
    {
        if (!OperatingSystem.IsWindows() ||
            !GetNamedPipeServerProcessId(pipe, out uint peerPid) || peerPid == 0)
            return false;

        IntPtr manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero) return false;
        try
        {
            IntPtr service = OpenService(manager, AdminBrokerProtocol.ServiceName, ServiceQueryStatus);
            if (service == IntPtr.Zero) return false;
            try
            {
                int size = Marshal.SizeOf<ServiceStatusProcess>();
                return QueryServiceStatusEx(service, 0, out var status, size, out _) &&
                    status.CurrentState == ServiceRunning &&
                    status.ProcessId != 0 &&
                    status.ProcessId == peerPid;
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(manager); }
    }
}
