using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Yetta.Desktop;

/// <summary>
/// On-demand checks only for a file the user explicitly selected for launching
/// through Yetta. There is no global antivirus service or automatic privilege
/// escalation. A scan exit code 0 may include successful remediation, not a
/// blanket "clean" verdict.
/// </summary>
internal sealed record DaxFileAssessment(
    string Path,
    string Sha256,
    string SignatureStatus,
    string DefenderStatus,
    bool ScanFailedOrThreat,
    bool DefenderUnavailable);

internal static class DaxFileScanner
{
    private static string? FindDefender()
    {
        var platform = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData),
            "Microsoft", "Windows Defender", "Platform");
        if (Directory.Exists(platform))
        {
            var executables = Directory.EnumerateDirectories(platform)
                .OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase);
            foreach (string directory in executables)
            {
                string candidate = Path.Combine(directory, "MpCmdRun.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        string fallback = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");
        return File.Exists(fallback) ? fallback : null;
    }

    private static async Task<(int? Exit, string Text)> RunAsync(
        string executable, IReadOnlyList<string> args, TimeSpan timeout,
        CancellationToken token)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        if (!process.Start()) return (null, "Не удалось запустить проверяющий процесс.");
        using var timeoutToken = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutToken.CancelAfter(timeout);
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeoutToken.Token);
            var error = process.StandardError.ReadToEndAsync(timeoutToken.Token);
            await process.WaitForExitAsync(timeoutToken.Token);
            string text = (await output) + Environment.NewLine + (await error);
            return (process.ExitCode, text.Length > 1200 ? text[..1200] : text);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            return (null, "Проверка прервана или превышено время ожидания.");
        }
    }

    internal static async Task<DaxFileAssessment> AssessAsync(
        string file, CancellationToken token = default)
    {
        file = Path.GetFullPath(file);
        if (!File.Exists(file) ||
            !string.Equals(Path.GetExtension(file), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Выберите существующий исполняемый .exe-файл.");

        byte[] hash;
        using (var stream = File.OpenRead(file))
            hash = await SHA256.HashDataAsync(stream, token);
        string hex = Convert.ToHexStringLower(hash);

        string status = "Не удалось проверить цифровую подпись";
        try
        {
            // Single-quoted PowerShell literal, with quotes doubled to avoid
            // interpreting selected filenames as PowerShell instructions.
            string quoted = "'" + file.Replace("'", "''") + "'";
            var result = await RunAsync("powershell.exe",
                ["-NoProfile", "-NonInteractive", "-Command",
                    "$s = Get-AuthenticodeSignature -LiteralPath " + quoted +
                    "; $s.Status.ToString()"], TimeSpan.FromSeconds(20), token);
            if (result.Exit == 0)
                status = result.Text.Trim().Split('\n',
                    StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? status;
        }
        catch (Exception e) when (e is IOException or
            System.ComponentModel.Win32Exception or UnauthorizedAccessException) { }

        string? defender = FindDefender();
        if (defender is null)
            return new DaxFileAssessment(file, hex, status,
                "Microsoft Defender недоступен: антивирусная проверка не выполнялась.",
                ScanFailedOrThreat: false, DefenderUnavailable: true);

        try
        {
            var scan = await RunAsync(defender,
                ["-Scan", "-ScanType", "3", "-File", file],
                TimeSpan.FromMinutes(3), token);
            // Defender exit 0 can mean no detections OR malware remediated;
            // exit 2 can mean malware found OR a scanning error.
            if (scan.Exit != 0)
                return new DaxFileAssessment(file, hex, status,
                    "Defender сообщил о проблеме при проверке (код " +
                    (scan.Exit?.ToString() ?? "timeout") +
                    "). Это может быть обнаруженная угроза или ошибка сканирования.",
                    ScanFailedOrThreat: true, DefenderUnavailable: false);
            if (!File.Exists(file))
                return new DaxFileAssessment(file, hex, status,
                    "Файл исчез после проверки Defender (возможно, карантин).",
                    ScanFailedOrThreat: true, DefenderUnavailable: false);
            byte[] after;
            using (var stream = File.OpenRead(file))
                after = await SHA256.HashDataAsync(stream, token);
            if (!CryptographicOperations.FixedTimeEquals(hash, after))
                return new DaxFileAssessment(file, hex, status,
                    "Файл изменился во время проверки Defender. Запуск заблокирован.",
                    ScanFailedOrThreat: true, DefenderUnavailable: false);
            return new DaxFileAssessment(file, hex, status,
                "Defender завершил проверку (код 0). Это не гарантия отсутствия вредоносного ПО.",
                ScanFailedOrThreat: false, DefenderUnavailable: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception)
        {
            return new DaxFileAssessment(file, hex, status,
                "Невозможно выполнить проверку Defender: " + e.Message,
                ScanFailedOrThreat: true, DefenderUnavailable: false);
        }
    }
}
