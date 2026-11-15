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
    private string _lastReport = "DAX работает. Глубокий анализ угроз ещё не реализован.";

    internal TrayAgent()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        foreach (AgentState state in Enum.GetValues<AgentState>())
            _icons[state] = LoadIcon((int)state);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Проверить", null, async (_, _) => await RunCheckAsync());
        menu.Items.Add("Перезапустить", null, async (_, _) => await RestartAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Отключить", null, (_, _) => Disable());
        menu.Items.Add("Включить", null, (_, _) => Enable());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Закрыть", null, (_, _) => ExitAgent());
        _tray = new NotifyIcon
        {
            Text = "DAX · Yetta — запуск",
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
        _tray.Text = "DAX · Yetta — " + (_state switch
        {
            AgentState.Working => "Проверка",
            AgentState.Healthy => "Работает",
            AgentState.Notice => "Сообщение",
            AgentState.Threat => "Угроза",
            AgentState.Disabled => "Отключён",
            AgentState.Fatal => "Критический сбой",
            _ => "Неизвестно"
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

    private static string AppPath => Path.Combine(AppContext.BaseDirectory, "Yetta.exe");

    private void OpenYetta()
    {
        try
        {
            if (Process.GetProcessesByName("Yetta").Length > 0)
            {
                ShowBalloon("Yetta", "Интерфейс уже запущен. Перейдите к окну Yetta.");
                return;
            }
            if (!File.Exists(AppPath))
            {
                _lastReport = "Не найден Yetta.exe рядом с DAX.";
                SetState(AgentState.Notice);
                ShowReport();
                return;
            }
            Process.Start(new ProcessStartInfo(AppPath) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            _lastReport = "Yetta: запуск не удался (" + ex.GetType().Name + ").";
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
            string report = "DAX tray: работает.\\n" +
                "Yetta интерфейс: " + (interfaceRunning ? "запущен" : installed ? "не запущен" : "файл отсутствует") + ".\\n" +
                "Tallow: встроен в Yetta; отдельный сервис не создан.\\n" +
                "Marvine: компонент Yetta; независимый контроль недоступен.\\n" +
                "Процессы Yetta: " + processes.Length + ".\\n" +
                "DAX анализ угроз / Defender: пока не подключён.";
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
            SetState(_lastReport.Contains("файл отсутствует", StringComparison.Ordinal) ? AgentState.Notice : AgentState.Healthy);
            ShowBalloon("DAX · проверка завершена", _lastReport.Replace("\\n", " "));
        }
        catch (Exception ex)
        {
            _lastReport = "Проверка прервана: " + ex.GetType().Name;
            SetState(AgentState.Notice);
            ShowBalloon("DAX · ошибка проверки", _lastReport, ToolTipIcon.Warning);
        }
    }

    private async Task RestartAsync()
    {
        SetState(AgentState.Working);
        await Task.Delay(350);
        // No privileged DAX services are installed yet; restart tray coordinator only.
        _lastReport = "Компонент DAX tray перезапущен. Системные службы DAX пока отсутствуют.";
        SetState(AgentState.Healthy);
        ShowBalloon("DAX", _lastReport);
    }

    private void Disable()
    {
        _lastReport = "DAX отключён пользователем. Операции Yetta должны быть заблокированы.";
        SetState(AgentState.Disabled);
        ShowBalloon("DAX отключён", "Значок остаётся в трее; включите DAX для работы с Yetta.", ToolTipIcon.Warning);
    }
    private void Enable()
    {
        SetState(AgentState.Healthy);
        _lastReport = "DAX tray снова работает; анализ угроз ещё не реализован.";
        ShowBalloon("DAX включён", _lastReport);
    }
    private void ShowReport() => MessageBox.Show(_lastReport, "DAX · состояние", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
