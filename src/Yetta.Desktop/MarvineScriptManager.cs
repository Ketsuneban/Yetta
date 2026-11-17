using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Yetta.Desktop;

/// <summary>Tracks Python processes started explicitly by this Yetta session.</summary>
internal sealed class MarvineScriptManager : IDisposable
{
    private sealed record Script(string Id, string Path, Process Process);
    private readonly Dictionary<string, Script> _scripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    internal IReadOnlyList<(string Id, string Path, bool Running)> List()
    {
        lock (_sync)
            return _scripts.Values.Select(item => (item.Id, item.Path, !item.Process.HasExited)).ToArray();
    }

    internal void Start(string id, string path, string interpreterPath, Action<string> onOutput)
    {
        if (!Regex.IsMatch(id, @"\A[A-Za-z0-9]{1,16}\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("ID: от 1 до 16 латинских букв или цифр.", nameof(id));
        path = Path.GetFullPath(path.Trim('"'));
        if (!path.EndsWith(".py", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new FileNotFoundException("Нужен существующий файл Python (.py).", path);
        lock (_sync)
        {
            if (_scripts.ContainsKey(id)) throw new InvalidOperationException("Этот ID уже занят.");
            var process = StartPython(path, interpreterPath);
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutput("[▪] " + id + ": " + e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onOutput("[⚠] " + id + ": " + e.Data); };
            process.Exited += (_, _) =>
            {
                try { onOutput("[ℹ] " + id + ": скрипт завершён, код " + process.ExitCode + "."); }
                catch (InvalidOperationException) { }
            };
            _scripts.Add(id, new Script(id, path, process));
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
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
            RedirectStandardInput = false,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
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
