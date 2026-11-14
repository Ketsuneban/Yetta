using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Yetta.Desktop;

/// <summary>
/// Passive tracking of the Yetta-owned embedded-shell process and descendants.
/// Enumerates process metadata solely to determine parentage; persists only
/// events for the observed Yetta subtree. Does not scan arbitrary system apps.
/// </summary>
internal sealed class DaxProcessMonitor : IDisposable
{
    private const uint QueryLimitedInformation = 0x1000;
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicProcessInfo
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr ParentProcessId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inherit, uint id);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        IntPtr handle, int informationClass, out BasicProcessInfo information,
        int informationLength, out int returnLength);

    private readonly DispatcherTimer _timer;
    private readonly int _rootPid;
    private readonly HashSet<int> _known = [];
    private readonly Action<string> _report;
    private bool _disposed;

    internal DaxProcessMonitor(int rootPid, Action<string> report)
    {
        _rootPid = rootPid;
        _report = report;
        _known.Add(rootPid);
        DaxAudit.Write("process", "started", "PowerShell process in Yetta: PID " + rootPid);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => Scan();
        _timer.Start();
    }

    private static int? ParentOf(int pid)
    {
        IntPtr handle = OpenProcess(QueryLimitedInformation, false, (uint)pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            if (NtQueryInformationProcess(handle, 0, out var data,
                Marshal.SizeOf<BasicProcessInfo>(), out _) < 0)
                return null;
            return unchecked((int)data.ParentProcessId.ToInt64());
        }
        finally { _ = CloseHandle(handle); }
    }

    private void Scan()
    {
        if (_disposed) return;
        var snapshots = Process.GetProcesses();
        try
        {
            var parents = new Dictionary<int, int>();
            foreach (var process in snapshots)
            {
                if (process.Id == _rootPid) continue;
                int? parent = ParentOf(process.Id);
                if (parent is int id) parents[process.Id] = id;
            }

            var tree = new HashSet<int> { _rootPid };
            for (int step = 0; step < 10; step++)
            {
                bool advanced = false;
                foreach (var pair in parents)
                    if (tree.Contains(pair.Value) && tree.Add(pair.Key)) advanced = true;
                if (!advanced) break;
            }

            foreach (int pid in tree)
            {
                if (!_known.Add(pid) || pid == _rootPid) continue;
                var p = snapshots.FirstOrDefault(x => x.Id == pid);
                string name = p?.ProcessName ?? "process";
                DaxAudit.Write("process", "started", "Yetta child PID " + pid + ": " + name);
                _report("Запущен дочерний процесс " + name + " (PID " + pid + ")");
            }

            foreach (int pid in _known.ToList())
            {
                if (pid != _rootPid && !tree.Contains(pid))
                {
                    _known.Remove(pid);
                    DaxAudit.Write("process", "exited", "Yetta child PID " + pid);
                }
            }
        }
        catch (Win32Exception) { }
        catch (InvalidOperationException) { }
        finally
        {
            foreach (var p in snapshots) p.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        DaxAudit.Write("process", "stopped", "Stopped tracking Yetta PowerShell PID " + _rootPid);
    }
}
