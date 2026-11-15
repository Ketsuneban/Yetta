using Yetta.Core;
using System.Diagnostics;
using System.Drawing;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace Yetta.DaxAgent;

/// <summary>
/// Lightweight, unprivileged tray companion. Checks service liveliness only.
/// Not an antivirus, Windows service, admin broker or malware scanner.
/// </summary>
internal static class Program
{
    private const string MutexName = "Local\\YettaDaxAgent_0_3";
    internal const string PipeName = "Yetta-DAX-Local-v1";
    [STAThread]
    private static void Main()
    {
        using var singleton = new Mutex(initiallyOwned: true, MutexName, out bool first);
        if (!first) return;
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayAgent());
    }
}

internal enum AgentState { Working = 1, Healthy = 2, Notice = 3, Threat = 4, Disabled = 5, Fatal = 6 }

internal sealed class TrayAgent : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly SynchronizationContext _ui;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<AgentState, Icon> _icons = new();
    private AgentState _state = AgentState.Healthy;
    private bool _shuttingDown;
    private string _lastReport = TallowText.Get("dax.tray.0001");

    internal TrayAgent()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        foreach (AgentState state in Enum.GetValues<AgentState>())
            _icons[state] = LoadIcon((int)state);
        var menu = new ContextMenuStrip();
        menu.Items.Add(TallowText.Get("dax.tray.0002"), null, async (_, _) => await RunCheckAsync());
        menu.Items.Add(TallowText.Get("dax.tray.0003"), null, async (_, _) => await RestartAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(TallowText.Get("dax.tray.0004"), null, (_, _) => Disable());
        menu.Items.Add(TallowText.Get("dax.tray.0005"), null, (_, _) => Enable());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(TallowText.Get("ui.warning.0008"), null, (_, _) => ExitAgent());
        _tray = new NotifyIcon
        {
            Text = TallowText.Get("dax.tray.0006"),
            Icon = _icons[_state],
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (_state is AgentState.Notice or AgentState.Threat or AgentState.Fatal)
                ShowReport();
            else OpenYetta();
        };
        UpdateTray();
        _ = ServeAsync(_stop.Token);
    }

    private static Icon LoadIcon(int number)
    {
        try
        {
            using Stream? png = Assembly.GetExecutingAssembly().GetManifestResourceStream("Yetta.DaxAgent.Assets." + number + ".png");
            if (png is null) return SystemIcons.Shield;
            using var bitmap = new Bitmap(png);
            IntPtr hIcon = bitmap.GetHicon();
            try { using var icon = Icon.FromHandle(hIcon); return (Icon)icon.Clone(); }
            finally { DestroyIcon(hIcon); }
        }
        catch { return SystemIcons.Shield; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    private void UpdateTray()
    {
        _tray.Icon = _icons[_state];
        _tray.Text = TallowText.Get("dax.tray.0007") + (_state switch
        {
            AgentState.Working => TallowText.Get("dax.tray.0008"),
            AgentState.Healthy => TallowText.Get("dax.tray.0009"),
            AgentState.Notice => TallowText.Get("dax.tray.0010"),
            AgentState.Threat => TallowText.Get("dax.tray.0011"),
            AgentState.Disabled => TallowText.Get("dax.tray.0012"),
            AgentState.Fatal => TallowText.Get("dax.tray.0013"),
            _ => TallowText.Get("dax.tray.0014")
        });
    }

    private void SetState(AgentState state) { _state = state; UpdateTray(); }

    private void ShowBalloon(string title, string body, ToolTipIcon type = ToolTipIcon.Info)
    {
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = body.Length > 220 ? body[..220] : body;
        _tray.BalloonTipIcon = type;
        _tray.ShowBalloonTip(5000);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr hwnd, int command);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    private sealed class ProcessSet(Process[] processes) : IDisposable
    {
        internal Process[] Processes { get; } = processes;
        public void Dispose() { foreach (var process in Processes) process.Dispose(); }
    }

    private static string AppPath => Path.Combine(AppContext.BaseDirectory, "Yetta.exe");

    private void OpenYetta()
    {
        try
        {
            using var owned = new ProcessSet(Process.GetProcessesByName("Yetta"));
            var active = owned.Processes.FirstOrDefault(p => !p.HasExited && p.MainWindowHandle != IntPtr.Zero);
            if (active is not null)
            {
                IntPtr handle = active.MainWindowHandle;
                _ = ShowWindowAsync(handle, 9); // SW_RESTORE; Windows may deny focus-stealing.
                if (!SetForegroundWindow(handle))
                    ShowBalloon(TallowText.Get("ui.main.0012"), TallowText.Get("dax.tray.0015"));
                return;
            }
            if (!File.Exists(AppPath))
            {
                _lastReport = TallowText.Get("dax.tray.0016");
                SetState(AgentState.Notice);
                ShowReport();
                return;
            }
            Process.Start(new ProcessStartInfo(AppPath) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            _lastReport = TallowText.Get("dax.tray.0017") + ex.GetType().Name + ").";
            SetState(AgentState.Notice);
            ShowReport();
        }
    }

    private string CheckComponents()
    {
        // Explicitly report only observed/known state; do not invent malware verdicts.
        var processes = Process.GetProcessesByName("Yetta");
        try
        {
            bool interfaceRunning = processes.Any(p => !p.HasExited);
            bool installed = File.Exists(AppPath);
            string report = TallowText.Get("dax.tray.0018") +
                TallowText.Get("dax.tray.0019") + (interfaceRunning ? TallowText.Get("dax.tray.0020") : installed ? TallowText.Get("dax.tray.0021") : TallowText.Get("dax.tray.0022")) + ".\\n" +
                TallowText.Get("dax.tray.0023") +
                TallowText.Get("dax.tray.0024") +
                TallowText.Get("dax.tray.0025") + processes.Length + ".\\n" +
                TallowText.Get("dax.tray.0026");
            return report;
        }
        finally { foreach (var p in processes) p.Dispose(); }
    }

    private async Task RunCheckAsync()
    {
        if (_state == AgentState.Disabled) return;
        SetState(AgentState.Working);
        await Task.Yield();
        try
        {
            _lastReport = CheckComponents();
            SetState(_lastReport.Contains(TallowText.Get("dax.tray.0022"), StringComparison.Ordinal) ? AgentState.Notice : AgentState.Healthy);
            ShowBalloon(TallowText.Get("dax.tray.0027"), _lastReport.Replace(TallowText.Get("dax.tray.0028"), " "));
        }
        catch (Exception ex)
        {
            _lastReport = TallowText.Get("dax.tray.0029") + ex.GetType().Name;
            SetState(AgentState.Notice);
            ShowBalloon(TallowText.Get("dax.tray.0030"), _lastReport, ToolTipIcon.Warning);
        }
    }

    private async Task RestartAsync()
    {
        SetState(AgentState.Working);
        await Task.Delay(350);
        // No privileged DAX services are installed yet; restart tray coordinator only.
        _lastReport = TallowText.Get("dax.tray.0031");
        SetState(AgentState.Healthy);
        ShowBalloon(TallowText.Get("ui.main.0030"), _lastReport);
    }

    private void Disable()
    {
        _lastReport = TallowText.Get("dax.tray.0032");
        SetState(AgentState.Disabled);
        ShowBalloon(TallowText.Get("dax.tray.0033"), TallowText.Get("dax.tray.0034"), ToolTipIcon.Warning);
    }
    private void Enable()
    {
        SetState(AgentState.Healthy);
        _lastReport = TallowText.Get("dax.tray.0035");
        ShowBalloon(TallowText.Get("dax.tray.0036"), _lastReport);
    }
    private void ShowReport() => MessageBox.Show(_lastReport, TallowText.Get("dax.tray.0037"), MessageBoxButtons.OK, MessageBoxIcon.Information);
    private void ExitAgent()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        _stop.Cancel();
        _tray.Visible = false;
        ExitThread();
    }

    private async Task ServeAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(Program.PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(token);
                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(token);
                var result = line switch
                {
                    "PING" => _state == AgentState.Disabled ? "DISABLED" : _state == AgentState.Working ? "WORKING" : "READY",
                    "CHECK" => "CHECKING",
                    "RESTART" => "RESTARTING",
                    "ENABLE" => "ENABLED",
                    "DISABLE" => "DISABLED",
                    "STATE" => _state.ToString().ToUpperInvariant(),
                    _ => "UNKNOWN"
                };
                await writer.WriteLineAsync(result);
                if (line == "CHECK") _ui.Post(async _ => await RunCheckAsync(), null);
                if (line == "RESTART") _ui.Post(async _ => await RestartAsync(), null);
                if (line == "ENABLE") _ui.Post(_ => Enable(), null);
                if (line == "DISABLE") _ui.Post(_ => Disable(), null);
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { await Task.Delay(250, token).ConfigureAwait(false); }
            catch (Exception ex)
            {
                Debug.WriteLine("DAX pipe: " + ex.GetType().Name);
                try { await Task.Delay(500, token); } catch (OperationCanceledException) { break; }
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _stop.Cancel();
            _tray.Dispose();
            foreach (var icon in _icons.Values) icon.Dispose();
            _stop.Dispose();
        }
        base.Dispose(disposing);
    }
}
