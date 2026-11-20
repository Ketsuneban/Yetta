using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Yetta.Desktop;

internal sealed record ScriptOutput(Guid Run, string Id, string Text, bool IsError = false, bool Exited = false, bool Partial = false);

/// <summary>Tracks Python processes started explicitly by this Yetta session.</summary>
internal sealed class MarvineScriptManager : IDisposable
{
    private sealed record Script(Guid Run, string Id, string Path, Process Process)
    {
        internal SemaphoreSlim InputLock { get; } = new(1, 1);
        internal Task? Completion { get; set; }
    }
    private readonly Dictionary<string, Script> _scripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    internal IReadOnlyList<(string Id, string Path, bool Running)> List()
    {
        lock (_sync)
            return _scripts.Values.Select(item => (item.Id, item.Path, !item.Process.HasExited)).ToArray();
    }

    internal Guid Start(string id, string path, string interpreterPath, Action<ScriptOutput> onOutput, string? workingDirectory = null)
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
                if (previous.Completion is { IsCompleted: false } || !previous.Process.HasExited) throw new InvalidOperationException("Этот ID уже занят.");
                previous.Process.Dispose();
                _scripts.Remove(id);
            }
            if (_scripts.Count >= 100)
            {
                foreach (var finished in _scripts.Values.Where(item => item.Process.HasExited && item.Completion is { IsCompleted: true }).ToArray())
                {
                    finished.Process.Dispose();
                    _scripts.Remove(finished.Id);
                }
                if (_scripts.Count >= 100)
                    throw new InvalidOperationException("Достигнут предел одновременно запущенных скриптов (100).");
            }
            var process = StartPython(path, interpreterPath, workingDirectory ?? Path.GetDirectoryName(path)!);
            var run = Guid.NewGuid();
            var script = new Script(run, id, path, process);
            _scripts.Add(id, script);
            script.Completion = Task.Run(() => ReportExitAsync(process, run, id, onOutput));
            return run;
        }
    }

    private static async Task ReadOutputAsync(StreamReader reader, Guid run, string id, bool error, Action<ScriptOutput> report)
    {
        var buffer = new char[2048];
        var line = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] == '\n')
                {
                    report(new(run, id, line.ToString().TrimEnd('\r'), IsError: error));
                    line.Clear();
                }
                else if (line.Length < 4096) line.Append(buffer[i]);
            }
            if (!error && line.Length > 0)
                report(new(run, id, line.ToString().TrimEnd('\r'), Partial: true));
        }
        if (line.Length > 0) report(new(run, id, line.ToString().TrimEnd('\r'), IsError: error));
    }

    private static async Task ReportExitAsync(Process process, Guid run, string id, Action<ScriptOutput> report)
    {
        try
        {
            await Task.WhenAll(
                ReadOutputAsync(process.StandardOutput, run, id, false, report),
                ReadOutputAsync(process.StandardError, run, id, true, report),
                process.WaitForExitAsync()).ConfigureAwait(false);
            int code = process.ExitCode;
            report(new(run, id, "Скрипт завершён. Код выхода: " + code, IsError: code != 0, Exited: true));
        }
        catch (IOException ex) { report(new(run, id, "Ошибка чтения вывода: " + ex.Message, IsError: true, Exited: true)); }
        catch (InvalidOperationException) { }
    }

    private static Process StartPython(string path, string interpreterPath, string workingDirectory)
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
            WorkingDirectory = workingDirectory
        };
        if (name.Equals("py.exe", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add("-3");
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.Environment["PYTHONUTF8"] = "1";
        start.ArgumentList.Add("-X");
        start.ArgumentList.Add("utf8");
        start.ArgumentList.Add("-u");
        if (File.Exists(Path.Combine(Path.GetDirectoryName(interpreterPath)!, "python314._pth")))
        {
            // The embeddable runtime isolates sys.path. Add only the selected script's directory
            // and run it as __main__, preserving argv and imports from neighbouring files.
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("import os, runpy, sys; del sys.argv[0]; sys.path.insert(0, os.path.dirname(sys.argv[0])); runpy.run_path(sys.argv[0], run_name='__main__')");
        }
        start.ArgumentList.Add(path);
        var process = Process.Start(start) ?? throw new InvalidOperationException("Python не запустился.");
        process.EnableRaisingEvents = true;
        return process;
    }

    internal Task AnswerAsync(Guid run, string promptId, object? value, CancellationToken token) =>
        SendAsync(run, JsonSerializer.Serialize(new { id = promptId, value }), token);

    internal async Task SendAsync(Guid run, string line, CancellationToken token)
    {
        Script script;
        lock (_sync)
        {
            script = _scripts.Values.FirstOrDefault(item => item.Run == run)
                ?? throw new InvalidOperationException("Скрипт уже завершился.");
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await script.InputLock.WaitAsync(timeout.Token);
        try
        {
            StreamWriter input;
            lock (_sync)
            {
                if (!_scripts.TryGetValue(script.Id, out var current) || !ReferenceEquals(current, script) || script.Process.HasExited)
                    throw new InvalidOperationException("Скрипт уже завершился.");
                input = script.Process.StandardInput;
            }
            await input.WriteLineAsync(line.AsMemory(), timeout.Token);
            await input.FlushAsync(timeout.Token);
        }
        finally { script.InputLock.Release(); }
    }

    internal bool Stop(string idOrPath, string? id = null)
    {
        lock (_sync)
        {
            if (!_scripts.TryGetValue(id ?? idOrPath, out var script))
            {
                string path = Path.GetFullPath(idOrPath.Trim('"'));
                script = _scripts.Values.LastOrDefault(item =>
                    item.Path.Equals(path, StringComparison.OrdinalIgnoreCase) && !item.Process.HasExited);
            }
            return StopProcess(script);
        }
    }

    internal bool StopRun(Guid run)
    {
        lock (_sync)
            return StopProcess(_scripts.Values.FirstOrDefault(item => item.Run == run));
    }

    private static bool StopProcess(Script? script)
    {
        if (script is null || script.Process.HasExited) return false;
        try { script.Process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) when (script.Process.HasExited) { return false; }
        return true;
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
