using System.Diagnostics;
using System.Management;
using System.ComponentModel;
using Yetta.Core;

namespace Yetta.Desktop;

/// <summary>
/// Best-effort observation restricted to process IDs in the tree started by
/// an explicit Yetta action. This does not install a driver, read other app
/// memory or claim to see every short-lived child process.
/// </summary>
internal static class DaxProcessTreeMonitor
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    internal static void Monitor(Process launched, Action<string> onEvent)
    {
        int rootPid;
        try { rootPid = launched.Id; }
        catch (InvalidOperationException) { return; }

        _ = Task.Run(async () =>
        {
            var tracked = new HashSet<int> { rootPid };
            int samples = 0;
            DaxAuditLog.Append(YettaWarningLevel.Dax, launched.StartInfo.FileName,
                "Yetta запустила процесс PID " + rootPid, "started");
            try
            {
                // At most 60 minutes, preventing a permanent background
                // monitor even if a child process lives indefinitely.
                while (tracked.Count > 0 && samples++ < 1800)
                {
                    var newChildren = new List<(int Pid, string Name)>();
                    foreach (int parent in tracked.ToArray())
                    {
                        try
                        {
                            using var query = new ManagementObjectSearcher(
                                "SELECT ProcessId, Name FROM Win32_Process WHERE ParentProcessId = " +
                                parent);
                            foreach (ManagementObject child in query.Get())
                            {
                                using (child)
                                {
                                    int pid = Convert.ToInt32(child["ProcessId"]);
                                    if (pid > 0 && tracked.Add(pid))
                                        newChildren.Add((pid, Convert.ToString(child["Name"]) ?? "Process"));
                                }
                            }
                        }
                        catch (ManagementException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                    foreach (var (pid, name) in newChildren)
                    {
                        DaxAuditLog.Append(YettaWarningLevel.Dax, name,
                            "Дочерний процесс PID " + pid + ", родитель из дерева Yetta", "observed");
                        onEvent("DAX Process: " + name + " (PID " + pid + ") запущен.");
                    }

                    foreach (int pid in tracked.ToArray())
                    {
                        bool running;
                        try
                        {
                            using var child = Process.GetProcessById(pid);
                            running = !child.HasExited;
                        }
                        catch (ArgumentException) { running = false; }
                        catch (InvalidOperationException) { running = false; }
                        catch (Win32Exception) { running = false; }
                        if (!running)
                        {
                            tracked.Remove(pid);
                            DaxAuditLog.Append(YettaWarningLevel.Dax, "PID " + pid,
                                "Процесс из отслеживаемого дерева завершён", "exited");
                        }
                    }
                    if (tracked.Count == 0) break;
                    await Task.Delay(Interval);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or
                ObjectDisposedException or OperationCanceledException)
            {
                DaxAuditLog.Append(YettaWarningLevel.Caution, "DAX Process",
                    "Наблюдение завершилось с ошибкой: " + e.Message, "unavailable");
            }
            finally
            {
                DaxAuditLog.Append(YettaWarningLevel.Dax, "DAX Process",
                    "Мониторинг пользовательского дерева завершён", "stopped");
                launched.Dispose();
            }
        });
    }
}
