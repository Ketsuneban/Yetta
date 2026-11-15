using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using Yetta.Core;

namespace Yetta.Desktop;

/// <summary>
/// Client is always unelevated. Privilege is held by the protected, opt-in
/// Windows service after real UAC approval, never by the Yetta UI or a profile
/// value from user-editable settings.json.
/// </summary>
internal static class AdminBrokerClient
{
    private const string BrokerExe = "Yetta.AdminBroker.exe";
    private static string LocalBrokerPath => Path.Combine(AppContext.BaseDirectory, BrokerExe);

    internal static async Task<AdminBrokerReply> CallAsync(string operation,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return new(false, "Брокер доступен только в Windows.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var pipe = new NamedPipeClientStream(".", AdminBrokerProtocol.PipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous,
                TokenImpersonationLevel.Impersonation);
            await pipe.ConnectAsync(1000, timeout.Token);
            if (!BrokerServerIdentity.IsInstalledServicePipe(pipe.SafePipeHandle))
                return new(false, "Ответ не принадлежит зарегистрированной службе Windows YettaAdminBroker.");

            byte[] request = JsonSerializer.SerializeToUtf8Bytes(
                new AdminBrokerRequest(AdminBrokerProtocol.Version, operation));
            await WriteFrame(pipe, request, timeout.Token);
            byte[] reply = await ReadFrame(pipe, timeout.Token);
            return JsonSerializer.Deserialize<AdminBrokerReply>(reply)
                ?? new(false, "Пустой ответ защищённой службы.");
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or
            UnauthorizedAccessException or OperationCanceledException or JsonException)
        {
            return new(false, "Защищённая служба недоступна: " + ex.Message);
        }
    }

    internal static async Task<bool> IsAvailableAsync() =>
        (await CallAsync(AdminBrokerProtocol.Ping)).Success;

    internal static Task<AdminBrokerReply> TestMachineWriteAsync() =>
        CallAsync(AdminBrokerProtocol.WriteMachineProof);

    internal static Task<AdminBrokerReply> ReadMachineStateAsync() =>
        CallAsync(AdminBrokerProtocol.ReadMachineProof);

    internal static Task<AdminBrokerReply> ClearMachineWriteAsync() =>
        CallAsync(AdminBrokerProtocol.ClearMachineProof);

    internal static async Task<bool> InstallAsync()
    {
        if (!File.Exists(LocalBrokerPath))
            throw new FileNotFoundException(
                "Для установки прав ADMIN необходим YettaSetup.exe с компонентом AdminBroker.",
                LocalBrokerPath);
        using var identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value
            ?? throw new UnauthorizedAccessException("Windows не предоставила SID пользователя.");

        var start = new ProcessStartInfo(LocalBrokerPath)
        {
            UseShellExecute = true,
            Verb = "runas" // REAL Windows UAC, never suppressed or simulated.
        };
        start.ArgumentList.Add("--install");
        start.ArgumentList.Add(sid);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Windows отказала в запуске установщика службы.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                "Не удалось установить защищённую службу; Windows-код: " + process.ExitCode);

        // Service creation/start is asynchronous; readiness is verified by
        // authenticated IPC before the UI profile can be applied.
        for (int i = 0; i < 10; i++)
        {
            if (await IsAvailableAsync()) return true;
            await Task.Delay(300);
        }
        return false;
    }

    internal static async Task<bool> RemoveAsync()
    {
        if (!File.Exists(LocalBrokerPath))
            throw new FileNotFoundException(
                "Для удаления службы необходим полный установщик Yetta.",
                LocalBrokerPath);
        var start = new ProcessStartInfo(LocalBrokerPath)
        {
            UseShellExecute = true, Verb = "runas"
        };
        start.ArgumentList.Add("--remove");
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Windows отказала в запуске удаления службы.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) return false;
        for (int i = 0; i < 10; i++)
        {
            if (!await IsAvailableAsync()) return true;
            await Task.Delay(300);
        }
        return false;
    }

    private static async Task WriteFrame(PipeStream pipe, byte[] body,
        CancellationToken token)
    {
        if (body.Length is < 1 or > AdminBrokerProtocol.MaxMessageBytes)
            throw new InvalidDataException("Недопустимый размер команды брокера.");
        byte[] len = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(len, body.Length);
        await pipe.WriteAsync(len, token);
        await pipe.WriteAsync(body, token);
        await pipe.FlushAsync(token);
    }

    private static async Task<byte[]> ReadFrame(PipeStream pipe, CancellationToken token)
    {
        byte[] prefix = new byte[4];
        await pipe.ReadExactlyAsync(prefix, token);
        int size = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (size is < 1 or > AdminBrokerProtocol.MaxMessageBytes)
            throw new InvalidDataException("Недопустимый размер ответа брокера.");
        byte[] content = new byte[size];
        await pipe.ReadExactlyAsync(content, token);
        return content;
    }
}
