using System.Diagnostics;
using System.IO;

namespace Yetta.Desktop;

/// <summary>
/// A genuine PowerShell process with redirected text streams, rendered inside
/// Yetta's window. Not a Windows Terminal / ConPTY emulator: commands requiring
/// full-screen TTY, terminal escape sequences or interactive password input may
/// not work. Run only from a non-elevated process, after user opt-in.
/// </summary>
public sealed class EmbeddedShellSession : IDisposable
{
    private readonly Process _process;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Action<string> _onOutput;
    private bool _disposed;

    private EmbeddedShellSession(Process process, Action<string> onOutput)
    {
        _process = process;
        _onOutput = onOutput;
        _ = ReadOutputAsync(_process.StandardOutput);
        _ = ReadOutputAsync(_process.StandardError);
    }

    public static EmbeddedShellSession Start(Action<string> onOutput)
    {
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"),
            Arguments = "-NoLogo -NoProfile -Command -",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        // No shell elevation, UseShellExecute=false; this inherits the parent
        // user's real Windows token. The caller MUST reject elevated parents.
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!process.Start()) throw new InvalidOperationException("PowerShell did not start.");
        process.StandardInput.AutoFlush = true;
        DaxProcessTreeMonitor.Monitor(process,
            line => onOutput("[DAX] " + line), ownsProcess: false);
        return new EmbeddedShellSession(process, onOutput);
    }

    private async Task ReadOutputAsync(StreamReader reader)
    {
        try
        {
            while (!_cancel.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(_cancel.Token);
                if (line is null) break;
                if (!_disposed) _onOutput(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    public async Task SendAsync(string command)
    {
        if (_disposed || _process.HasExited)
            throw new InvalidOperationException("PowerShell session has stopped.");
        if (command.Length > 32768)
            throw new ArgumentOutOfRangeException(nameof(command), "Command is too long.");
        await _process.StandardInput.WriteLineAsync(command);
        await _process.StandardInput.FlushAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancel.Cancel();
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
        _process.Dispose();
        _cancel.Dispose();
    }
}
