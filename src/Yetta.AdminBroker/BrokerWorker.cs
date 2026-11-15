using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Yetta.Core;

namespace Yetta.AdminBroker;

/// <summary>
/// Installed in Program Files as a real Windows service. The process has the
/// LocalSystem service token, but exposes only four fixed, Yetta-owned commands.
/// No shell, file paths, executables, registry paths or PowerShell text are accepted.
/// </summary>
internal sealed class BrokerWorker(ILogger<BrokerWorker> log) : BackgroundService
{
    private const string RegistryPath = @"SOFTWARE\Yetta\AdminBroker";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows() || !WindowsIdentity.GetCurrent().IsSystem)
        {
            log.LogError("Refusing to run outside the LocalSystem service account.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using RegistryKey? machine = Registry.LocalMachine.OpenSubKey(RegistryPath, writable: false);
                string ownerSidString = machine?.GetValue("OwnerSid") as string ?? "";
                if (string.IsNullOrWhiteSpace(ownerSidString))
                {
                    log.LogCritical("Broker owner is not configured; refusing to listen.");
                    return;
                }

                var ownerSid = new SecurityIdentifier(ownerSidString);
                var acl = new PipeSecurity();
                acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                acl.AddAccessRule(new PipeAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    PipeAccessRights.FullControl, AccessControlType.Allow));
                acl.AddAccessRule(new PipeAccessRule(ownerSid,
                    PipeAccessRights.ReadWrite, AccessControlType.Allow));

                using var pipe = NamedPipeServerStreamAcl.Create(
                    AdminBrokerProtocol.PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    AdminBrokerProtocol.MaxMessageBytes,
                    AdminBrokerProtocol.MaxMessageBytes,
                    acl,
                    HandleInheritability.None,
                    0);

                await pipe.WaitForConnectionAsync(stoppingToken);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                byte[] requestBytes = await ReadFrame(pipe, timeout.Token);

                // Reading first is required for named-pipe client impersonation.
                // Fail closed if impersonation cannot establish the true Windows SID.
                string? actualCallerSid = null;
                pipe.RunAsClient(() =>
                    actualCallerSid = WindowsIdentity.GetCurrent(ifImpersonating: true)?.User?.Value);
                if (!string.Equals(actualCallerSid, ownerSidString, StringComparison.Ordinal))
                {
                    log.LogWarning("Rejected named-pipe request: caller SID mismatch.");
                    continue;
                }

                AdminBrokerReply reply;
                try
                {
                    var request = JsonSerializer.Deserialize<AdminBrokerRequest>(requestBytes);
                    reply = request is null
                        ? new(false, "Недопустимый запрос.")
                        : ExecuteAllowlisted(request);
                }
                catch (JsonException)
                {
                    reply = new(false, "Недопустимый формат запроса.");
                }
                await WriteFrame(pipe, reply, timeout.Token);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (e is IOException or OperationCanceledException
                or UnauthorizedAccessException or InvalidOperationException)
            {
                log.LogWarning(e, "Rejected or timed-out broker connection.");
            }
        }
    }

    private static AdminBrokerReply ExecuteAllowlisted(AdminBrokerRequest request)
    {
        if (request.Version != AdminBrokerProtocol.Version)
            return new(false, "Версия протокола брокера не совпадает.");

        using var key = Registry.LocalMachine.OpenSubKey(RegistryPath, writable: true)
            ?? throw new UnauthorizedAccessException("Machine policy is unavailable.");
        return request.Operation switch
        {
            AdminBrokerProtocol.Ping =>
                new(true, "Служба DAX Administrator активна.", "Операции ограничены белым списком Yetta."),
            AdminBrokerProtocol.ReadMachineProof =>
                new(true, "Состояние защищённой настройки Yetta.",
                    key.GetValue("LastProofUtc") as string ?? "Проверка ещё не выполнялась."),
            AdminBrokerProtocol.WriteMachineProof =>
                WriteProof(key),
            AdminBrokerProtocol.ClearMachineProof =>
                ClearProof(key),
            _ => new(false, "Операция запрещена политикой DAX.")
        };
    }

    private static AdminBrokerReply WriteProof(RegistryKey key)
    {
        string stamp = DateTimeOffset.UtcNow.ToString("O");
        key.SetValue("LastProofUtc", stamp, RegistryValueKind.String);
        RecordAudit(key, AdminBrokerProtocol.WriteMachineProof);
        return new(true, "Административная запись HKLM\\SOFTWARE\\Yetta\\AdminBroker выполнена.", stamp);
    }

    private static AdminBrokerReply ClearProof(RegistryKey key)
    {
        key.DeleteValue("LastProofUtc", throwOnMissingValue: false);
        RecordAudit(key, AdminBrokerProtocol.ClearMachineProof);
        return new(true, "Административная проверочная запись удалена.");
    }

    private static void RecordAudit(RegistryKey root, string operation)
    {
        using var audit = root.CreateSubKey("Audit", writable: true)
            ?? throw new UnauthorizedAccessException("Protected audit store cannot be opened.");
        string key = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N");
        audit.SetValue(key, operation, RegistryValueKind.String);
    }

    private static async Task<byte[]> ReadFrame(PipeStream pipe, CancellationToken token)
    {
        byte[] lenBytes = new byte[4];
        await pipe.ReadExactlyAsync(lenBytes, token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(lenBytes);
        if (length is < 1 or > AdminBrokerProtocol.MaxMessageBytes)
            throw new InvalidDataException("Broker frame length is invalid.");
        byte[] body = new byte[length];
        await pipe.ReadExactlyAsync(body, token);
        return body;
    }

    private static async Task WriteFrame(PipeStream pipe, AdminBrokerReply data,
        CancellationToken token)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(data);
        if (body.Length > AdminBrokerProtocol.MaxMessageBytes)
            throw new InvalidDataException("Broker reply is too large.");
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, body.Length);
        await pipe.WriteAsync(length, token);
        await pipe.WriteAsync(body, token);
        await pipe.FlushAsync(token);
    }
}
