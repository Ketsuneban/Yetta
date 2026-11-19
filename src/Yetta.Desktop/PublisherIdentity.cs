using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Yetta.Desktop;

internal sealed record PublisherIdentity(string Publisher, string Status, string Technical = "", bool Verified = false);

/// <summary>Read Windows Authenticode results, including catalog signatures. Never executes the subject file.</summary>
internal static class PublisherInspector
{
    private static readonly SemaphoreSlim Slots = new(2, 2);
    private const string Query = "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); $ErrorActionPreference='Stop'; " +
        "$s=Microsoft.PowerShell.Security\\Get-AuthenticodeSignature -LiteralPath $env:YETTA_SIGNATURE_PATH; " +
        "$n='';$thumb='';if($null -ne $s.SignerCertificate){$n=$s.SignerCertificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName,$false);$thumb=$s.SignerCertificate.Thumbprint};" +
        "[pscustomobject]@{Status=$s.Status.ToString();Signer=$n;Thumbprint=$thumb;Message=$s.StatusMessage}|ConvertTo-Json -Compress";

    internal static PublisherIdentity Parse(string json)
    {
        using var document = JsonDocument.Parse(json.Trim().TrimStart('\uFEFF'));
        var root = document.RootElement;
        string status = root.GetProperty("Status").GetString() ?? "UnknownError";
        string signer = root.GetProperty("Signer").GetString() ?? "";
        string details = "Authenticode: " + status;
        if (root.TryGetProperty("Thumbprint", out var thumb)) details += "\nСертификат: " + thumb.GetString();
        if (root.TryGetProperty("Message", out var message)) details += "\n" + message.GetString();
        return status switch
        {
            "Valid" when signer.Length > 0 => new("Издатель: " + signer, "Цифровая подпись действительна", details, true),
            "NotSigned" => new("Издатель: не подтверждён", "Файл не подписан", details),
            "HashMismatch" => new("Издатель: не подтверждён", "Подпись повреждена: содержимое файла изменено", details),
            "NotTrusted" => new("Издатель: не подтверждён", "Подпись не прошла проверку доверия", details + "\nЗаявленный подписант: " + signer),
            _ => new("Издатель: не подтверждён", "Не удалось подтвердить цифровую подпись", details)
        };
    }

    internal static async Task<PublisherIdentity> InspectAsync(string? path, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(path)) return new("Издатель: не применимо", "Конкретный файл не указан");
        if (!OperatingSystem.IsWindows()) return new("Издатель: не подтверждён", "Проверка доступна в Windows");
        await Slots.WaitAsync(token).ConfigureAwait(false);
        try { return await InspectCoreAsync(path, token).ConfigureAwait(false); }
        finally { Slots.Release(); }
    }

    private static async Task<PublisherIdentity> InspectCoreAsync(string path, CancellationToken token)
    {
        Process? process = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            // Metadata-only checks avoid hydrating cloud placeholders or reading an entire remote file.
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
                return new("Издатель: не подтверждён", "Облачный файл недоступен для локальной проверки");
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                var owner = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(NTAccount));
                return new("Владелец: " + (owner?.Value ?? "не определён"), "У папок нет подписи Authenticode");
            }
            if (((int)attributes & (0x1000 | 0x40000 | 0x400000)) != 0)
                return new("Издатель: не подтверждён", "Файл не загружен на устройство");
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                RedirectStandardError = true, StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false)
            };
            foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", Query }) start.ArgumentList.Add(argument);
            // The path is data in the child environment, never interpolated into PowerShell code.
            start.Environment["YETTA_SIGNATURE_PATH"] = Path.GetFullPath(path);
            start.Environment["PSModulePath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "Modules");
            process = Process.Start(start) ?? throw new IOException("Could not query Windows signatures.");
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            string json = await output.ConfigureAwait(false);
            _ = await error.ConfigureAwait(false);
            return process.ExitCode == 0 && json.Length <= 32768 ? Parse(json) : new("Издатель: не подтверждён", "Проверка подписи недоступна");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return new("Издатель: не подтверждён", "Истекло время проверки подписи"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or JsonException or InvalidOperationException or System.Security.SecurityException or IdentityNotMappedException)
        { return new("Издатель: не подтверждён", "Не удалось прочитать цифровую подпись", ex.GetType().Name); }
        finally
        {
            if (process is not null)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
                process.Dispose();
            }
        }
    }
}
