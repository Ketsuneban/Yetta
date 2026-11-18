using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Yetta.Desktop;

internal sealed record ScriptOutput(Guid Run, string Id, string Text, bool IsError = false, bool Exited = false);

/// <summary>Tracks Python processes started explicitly by this Yetta session.</summary>
internal sealed class MarvineScriptManager : IDisposable
{
    private sealed record Script(Guid Run, string Id, string Path, Process Process);
    private readonly Dictionary<string, Script> _scripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    internal IReadOnlyList<(string Id, string Path, bool Running)> List()
    {
        lock (_sync)
            return _scripts.Values.Select(item => (item.Id, item.Path, !item.Process.HasExited)).ToArray();
    }

    internal void Start(string id, string path, string interpreterPath, Action<ScriptOutput> onOutput)
    {
        if (!Regex.IsMatch(id, @"\A[A-Za-z0-9]{1,16}\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("ID: от 1 до 16 латинских букв или цифр.", nameof(id));
        path = Path.GetFullPath(path.Trim('"'));
        if (!path.EndsWith(".py", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new FileNotFoundException("Нужен существующий файл Python (.py).", path);
        lock (_sync)
        {
            if (_scripts.TryGetValue(id, out var previous))
            {
                if (!previous.Process.HasExited) throw new InvalidOperationException("Этот ID уже занят.");
                previous.Process.Dispose();
                _scripts.Remove(id);
            }
            if (_scripts.Count >= 100)
            {
                foreach (var finished in _scripts.Values.Where(item => item.Process.HasExited).ToArray())
                {
                    finished.Process.Dispose();
                    _scripts.Remove(finished.Id);
                }
                if (_scripts.Count >= 100)
                    throw new InvalidOperationException("Достигнут предел одновременно запущенных скриптов (100).");
            }
            var process = StartPython(path, interpreterPath);
            var run = Guid.NewGuid();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutput(new(run, id, e.Data)); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onOutput(new(run, id, e.Data, IsError: true)); };
            _scripts.Add(id, new Script(run, id, path, process));
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _ = ReportExitAsync(process, run, id, onOutput);
        }
    }

    private static async Task ReportExitAsync(Process process, Guid run, string id, Action<ScriptOutput> report)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            int code = process.ExitCode;
            report(new(run, id, "Скрипт завершён. Код выхода: " + code, IsError: code != 0, Exited: true));
        }
        catch (InvalidOperationException) { }
    }

    private static Process StartPython(string path, string interpreterPath)
    {
        interpreterPath = Path.GetFullPath(interpreterPath);
        string name = Path.GetFileName(interpreterPath);
        if ((!name.Equals("py.exe", StringComparison.OrdinalIgnoreCase) &&
             !name.Equals("python.exe", StringComparison.OrdinalIgnoreCase)) || !File.Exists(interpreterPath))
            throw new FileNotFoundException("Выбери установленный Python 3 (py.exe или python.exe).", interpreterPath);
        var start = new ProcessStartInfo(interpreterPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(path)!
        };
        if (name.Equals("py.exe", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add("-3");
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.Environment["PYTHONUTF8"] = "1";
        start.ArgumentList.Add("-u");
        start.ArgumentList.Add(path);
        var process = Process.Start(start) ?? throw new InvalidOperationException("Python не запустился.");
        process.EnableRaisingEvents = true;
        return process;
    }

    internal async Task AnswerAsync(Guid run, string promptId, object? value, CancellationToken token)
    {
        StreamWriter input;
        lock (_sync)
        {
            var script = _scripts.Values.FirstOrDefault(item => item.Run == run);
            if (script is null || script.Process.HasExited)
                throw new InvalidOperationException("Скрипт уже завершился.");
            input = script.Process.StandardInput;
        }
        // Serialize exactly one reply to this run's private stdin. Never echo it into Marvine.
        string line = JsonSerializer.Serialize(new { id = promptId, value });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await input.WriteLineAsync(line.AsMemory(), timeout.Token);
        await input.FlushAsync(timeout.Token);
    }

    internal bool Stop(string idOrPath)
    {
        lock (_sync)
        {
            Script? script = _scripts.Values.FirstOrDefault(item =>
                item.Id.Equals(idOrPath, StringComparison.OrdinalIgnoreCase) ||
                item.Path.Equals(idOrPath.Trim('"'), StringComparison.OrdinalIgnoreCase));
            if (script is null || script.Process.HasExited) return false;
            script.Process.Kill(entireProcessTree: true);
            return true;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (var script in _scripts.Values)
            {
                try { if (!script.Process.HasExited) script.Process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
                script.Process.Dispose();
            }
            _scripts.Clear();
        }
    }
}
