using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32;
using Yetta.Core;

namespace Yetta.AdminBroker;

internal static class BrokerInstaller
{
    private const string RegistryPath = @"SOFTWARE\Yetta\AdminBroker";
    private const string FileName = "Yetta.AdminBroker.exe";

    private static string InstalledPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "Yetta", "AdminBroker", FileName);

    internal static async Task<int> Install(string sid)
    {
        VerifyElevated();
        var owner = new SecurityIdentifier(sid);
        if (!owner.IsAccountSid()) throw new ArgumentException("A Windows user SID is required.");

        using (var existing = Registry.LocalMachine.OpenSubKey(RegistryPath))
        {
            if (existing?.GetValue("OwnerSid") is string oldSid &&
                !string.Equals(oldSid, sid, StringComparison.Ordinal))
                throw new UnauthorizedAccessException(
                    "Another Windows account owns this broker. An administrator must remove it first.");
        }

        string source = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot locate the elevated broker binary.");
        string destination = InstalledPath;
        string parent = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(parent); // Program Files inherits protected ACL.

        if (File.Exists(destination))
        {
            // Privileged service EXEs must never be silently replaced by per-user
            // application updates. A distinct elevated broker upgrade is required.
            byte[] sourceHash = SHA256.HashData(File.ReadAllBytes(source));
            byte[] installedHash = SHA256.HashData(File.ReadAllBytes(destination));
            if (!CryptographicOperations.FixedTimeEquals(sourceHash, installedHash))
                throw new InvalidOperationException(
                    "Installed broker version differs. Explicit privileged upgrade is required.");
        }
        else
            File.Copy(source, destination, overwrite: false);

        using (var machine = Registry.LocalMachine.CreateSubKey(RegistryPath, writable: true)
            ?? throw new UnauthorizedAccessException("Cannot write broker owner policy."))
        {
            machine.SetValue("OwnerSid", sid, RegistryValueKind.String);
            machine.SetValue("Protocol", AdminBrokerProtocol.Version, RegistryValueKind.DWord);
        }

        string serviceImage = "\"" + destination + "\" --service";
        await RunSc("create", AdminBrokerProtocol.ServiceName,
            "binPath=", serviceImage,
            "start=", "auto",
            "obj=", "LocalSystem",
            "DisplayName=", "Yetta Administrator Broker");
        await RunSc("start", AdminBrokerProtocol.ServiceName);
        return 0;
    }

    internal static async Task<int> Remove()
    {
        VerifyElevated();
        try { await RunSc("stop", AdminBrokerProtocol.ServiceName); }
        catch (InvalidOperationException) { /* Service could already be stopped. */ }
        await RunSc("delete", AdminBrokerProtocol.ServiceName);

        // If Windows still has the service executable open, do not attempt to
        // forcibly delete it. Report its path for a later admin cleanup.
        string path = InstalledPath;
        try
        {
            if (File.Exists(path)) File.Delete(path);
            string parent = Path.GetDirectoryName(path)!;
            if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                Directory.Delete(parent);
        }
        catch (IOException)
        {
            Console.Error.WriteLine("Service removed; binary still in use: " + path);
        }
        Registry.LocalMachine.DeleteSubKeyTree(RegistryPath, throwOnMissingSubKey: false);
        return 0;
    }

    private static void VerifyElevated()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException();
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Windows Administrator elevation is required.");
    }

    private static async Task RunSc(params string[] arguments)
    {
        string systemSc = Path.Combine(Environment.SystemDirectory, "sc.exe");
        var start = new ProcessStartInfo(systemSc)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true
        };
        foreach (string arg in arguments) start.ArgumentList.Add(arg);
        using var child = Process.Start(start)
            ?? throw new InvalidOperationException("Windows Service Control Manager is unavailable.");
        string outputTask = await child.StandardOutput.ReadToEndAsync();
        string errors = await child.StandardError.ReadToEndAsync();
        await child.WaitForExitAsync();
        if (child.ExitCode != 0)
            throw new InvalidOperationException(
                "Service Manager refused the operation: " + outputTask + " " + errors);
    }
}
