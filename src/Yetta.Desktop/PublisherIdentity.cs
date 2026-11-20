using System.ComponentModel;
using Microsoft.Security.Extensions;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Yetta.Desktop;

internal sealed record PublisherIdentity(string Publisher, string Status, string Technical = "", bool Verified = false);

/// <summary>Read Windows Authenticode results, including catalog signatures. Never executes the subject file.</summary>
internal static class PublisherInspector
{
    private static readonly SemaphoreSlim Slots = new(2, 2);
    internal static PublisherIdentity Describe(SignatureState status, string signer, string details)
    {
        return status switch
        {
            SignatureState.SignedAndTrusted when signer.Length > 0 => new("Издатель: " + signer, "Цифровая подпись действительна", details, true),
            SignatureState.Unsigned => new("Издатель: не подтверждён", "Файл не подписан", details),
            SignatureState.Invalid => new("Издатель: не подтверждён", "Цифровая подпись недействительна", details),
            SignatureState.SignedAndNotTrusted => new("Издатель: не подтверждён", "Подпись не прошла проверку доверия", details + "\nЗаявленный подписант: " + signer),
            _ => new("Издатель: не подтверждён", "Не удалось подтвердить цифровую подпись", details)
        };
    }

    internal static async Task<PublisherIdentity> InspectAsync(string? path, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(path)) return new("Издатель: не применимо", "Конкретный файл не указан");
        if (!OperatingSystem.IsWindows()) return new("Издатель: не подтверждён", "Проверка доступна в Windows");
        await Slots.WaitAsync(token).ConfigureAwait(false);
        var lookup = Task.Run(() =>
        {
            try { return InspectCore(path); }
            finally { Slots.Release(); }
        });
        try { return await lookup.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false); }
        catch (TimeoutException) { return new("Издатель: не подтверждён", "Истекло время проверки подписи"); }
    }

    private static PublisherIdentity InspectCore(string path)
    {
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
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var signature = FileSignatureInfo.GetFromFileStream(file);
            using var signer = signature.SigningCertificate;
            using var timestamp = signature.TimestampCertificate;
            string details = $"Authenticode: {signature.Kind} · {signature.State}";
            if (signer is not null) details += "\nСертификат: " + signer.Thumbprint;
            return Describe(signature.State, signer?.GetNameInfo(X509NameType.SimpleName, false) ?? "", details);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or
            CryptographicException or InvalidOperationException or System.Security.SecurityException or
            IdentityNotMappedException or NotSupportedException or System.Runtime.InteropServices.ExternalException)
        { return new("Издатель: не подтверждён", "Не удалось прочитать цифровую подпись", ex.GetType().Name); }
    }
}
