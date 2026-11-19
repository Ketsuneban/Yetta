using Microsoft.Win32;
using Yetta.Core;
using Yetta.Desktop;
using static Yetta.Core.DaxAdministratorProtocol;

static async Task Expect(DaxAdministratorOperation command, DaxAdministratorState expected)
{
    var actual = await DaxAdministratorClient.RequestAsync(command, CancellationToken.None);
    if (actual != expected) throw new InvalidOperationException($"{command}: expected {expected}, got {actual}.");
    Console.WriteLine($"PASS {command}: {actual}");
}

if (args is ["--removed"])
{
    await Expect(DaxAdministratorOperation.Status, DaxAdministratorState.NotInstalled);
    using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
    using var grant = machine.OpenSubKey(RegistryPath);
    if (grant is not null) throw new InvalidOperationException("Uninstall left the grant behind.");
}
else if (args is ["--revoked"])
{
    await Expect(DaxAdministratorOperation.Enable, DaxAdministratorState.Revoked);
}
else
{
    await Expect(DaxAdministratorOperation.Status, DaxAdministratorState.Active);
    await Expect(DaxAdministratorOperation.Disable, DaxAdministratorState.Inactive);
    await Expect(DaxAdministratorOperation.Status, DaxAdministratorState.Inactive);
    await Expect(DaxAdministratorOperation.Enable, DaxAdministratorState.Active);

    using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
    using (var grant = machine.OpenSubKey(RegistryPath, writable: true)!)
    {
        var owner = (string)grant.GetValue(OwnerValue)!;
        try
        {
            grant.SetValue(OwnerValue, "S-1-5-18");
            await Expect(DaxAdministratorOperation.Enable, DaxAdministratorState.NotGranted);
        }
        finally { grant.SetValue(OwnerValue, owner); }
    }

    await Expect(DaxAdministratorOperation.Revoke, DaxAdministratorState.Revoked);
    await Expect(DaxAdministratorOperation.Enable, DaxAdministratorState.Revoked);
}
