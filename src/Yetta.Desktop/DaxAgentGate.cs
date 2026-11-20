using Yetta.Core;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace Yetta.Desktop;

/// <summary>
/// Tallow-side health monitor for the separate unprivileged DAX tray companion.
/// The protocol is intentionally NOT an authentication or security boundary.
/// </summary>
internal sealed class DaxAgentGate : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Action<DaxStatus> _report;
    private bool _checking;
    private bool _disposed;
    internal DaxStatus Status { get; private set; } = new(DaxState.Unavailable, "", false, false);

    internal DaxAgentGate(Window owner, Action<DaxStatus> report)
    {
        _report = report;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += async (_, _) => await CheckAsync();
        owner.Loaded += async (_, _) =>
        {
            try
            {
                if (await GetStatusAsync() is { State: DaxState.Unavailable })
                {
                    await TryStopDaxAsync(DaxProtocol.LegacyPipeName);
                    if (_disposed) return;
                    TryLaunch();
                }
                if (_disposed) return;
                _timer.Start();
                await CheckAsync();
            }
            catch (OperationCanceledException) { }
        };
    }

    private string AgentFile => Path.Combine(AppContext.BaseDirectory, "Yetta.DAX.exe");

    internal void TryLaunch()
    {
        if (!File.Exists(AgentFile)) return;
        try
        {
            var start = new ProcessStartInfo(AgentFile) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
            start.Environment["YETTA_DESKTOP_PATH"] = Environment.ProcessPath;
            using var agent = Process.Start(start);
        }
        catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException or IOException)
        {
            DaxAudit.Write("connection", "launch_failed", ex.GetType().Name);
        }
    }

    internal static Task<string> RequestAsync(string command, CancellationToken token = default) =>
        RequestAsync(command, DaxProtocol.PipeName, token);

    private static async Task<string> RequestAsync(string command, string pipeName, CancellationToken token = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await client.ConnectAsync(800, deadline.Token);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            await writer.WriteLineAsync(command.AsMemory(), deadline.Token);
            return await DaxProtocol.ReadFrameAsync(reader, deadline.Token) ?? "OFFLINE";
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("DAX response timed out.");
        }
    }

    internal static async Task<DaxStatus> GetStatusAsync(CancellationToken token = default)
    {
        try
        {
            string frame = await RequestAsync(DaxProtocol.StatusRequest, token);
            return frame == "OFFLINE" ? new(DaxState.Unavailable, "", false, false) : DaxStatus.Parse(frame);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return new(DaxState.Unavailable, "", false, false);
        }
        catch (InvalidDataException)
        {
            return new(DaxState.Incompatible, "", false, false);
        }
    }

    internal static async Task TryStopDaxAsync(string pipeName = DaxProtocol.PipeName)
    {
        try { await RequestAsync("STOP_FOR_UPDATE", pipeName); }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        { DaxAudit.Write("connection", "stop_unavailable", ex.GetType().Name); }
    }

    internal async Task<DaxStatus> WaitForScriptAgentAsync(CancellationToken token)
    {
        var status = await GetStatusAsync(token);
        if (status.State == DaxState.Unavailable) TryLaunch();
        for (int retry = 0; retry < 3 && status.State is DaxState.Unavailable or DaxState.Working; retry++)
        {
            await Task.Delay(300, token);
            status = await GetStatusAsync(token);
        }
        return status;
    }

    internal async Task CheckAsync()
    {
        if (_disposed || _checking) return;
        _checking = true;
        try
        {
            var status = await GetStatusAsync();
            if (_disposed || status == Status) return;
            Status = status;
            _report(status);
        }
        finally { _checking = false; }
    }

    public void Dispose() { _disposed = true; _timer.Stop(); }
}
