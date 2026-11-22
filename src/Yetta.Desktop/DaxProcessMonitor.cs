using Yetta.Core;
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
    private readonly DateTime _rootStarted;
    private readonly HashSet<int> _known = [];
    private readonly Action<string> _report;
    private bool _checking;
    private bool _disposed;

    internal DaxProcessMonitor(int rootPid, Action<string> report)
    {
        _rootPid = rootPid;
        try
        {
            using var root = Process.GetProcessById(rootPid);
            _rootStarted = root.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            _rootStarted = DateTime.MinValue;
        }
        _report = report;
        _known.Add(rootPid);
        DaxAudit.Write("process", "started", "Yetta script process: PID " + rootPid);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => await ScanAsync();
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

    private async Task ScanAsync()
    {
        if (_disposed || _checking) return;
        _checking = true;
        try
        {
            var tree = await Task.Run(ReadTree);
            if (_disposed) return;
            foreach (var (pid, name) in tree)
            {
                if (!_known.Add(pid) || pid == _rootPid) continue;
                DaxAudit.Write("process", "started", "Yetta child PID " + pid + ": " + name);
                _report(TallowText.Get("dax.monitor.0001") + name + " (PID " + pid + ")");
            }
            foreach (int pid in _known.ToArray())
            {
                if (pid == _rootPid || tree.ContainsKey(pid)) continue;
                _known.Remove(pid);
                DaxAudit.Write("process", "unobserved", "Yetta child PID " + pid);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
        finally { _checking = false; }
    }

    private Dictionary<int, string> ReadTree()
    {
        // PID reuse must never enroll an unrelated process.
        try
        {
            using var root = Process.GetProcessById(_rootPid);
            if (root.StartTime.ToUniversalTime() != _rootStarted) return [];
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception) { return []; }
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

            var observed = new Dictionary<int, string>();
            foreach (var process in snapshots)
            {
                if (!tree.Contains(process.Id)) continue;
                try { observed[process.Id] = process.ProcessName; }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
            }
            return observed;
        }
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
        DaxAudit.Write("process", "stopped", "Stopped tracking Yetta script PID " + _rootPid);
    }
}
