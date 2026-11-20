using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;
using Windows.Management.Deployment;

namespace Yetta.Desktop;

internal static class WindowsPackages
{
    private const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    internal sealed record Package(string Name, string Id, string Version, string Publisher, string Location,
        string Scope, bool System, bool Hidden, string Selector, string? UninstallPath = null,
        string? PackageFullName = null, string? RemovalRestriction = null)
    {
        public string Label => $"{Name}  ·  {Version}  ·  {Id}";
        internal string Details => $"{Name}\nID: {Id}\nВерсия: {Version}\nИздатель: {Publisher}\n" +
            $"Расположение: {Location}\nОбласть: {Scope}\nСистемная: {(System ? "да" : "нет")}\nСкрытая: {(Hidden ? "да" : "нет")}" +
            (RemovalRestriction is null ? "" : $"\nУдаление: {RemovalRestriction}");
    }

    internal static IReadOnlyList<Package> List()
    {
        var packages = new List<Package>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(Uninstall);
            if (uninstall is null) continue;
            foreach (var id in uninstall.GetSubKeyNames())
            {
                using var key = uninstall.OpenSubKey(id);
                if (key is null) continue;
                string Read(string property) => key.GetValue(property) as string ?? "";
                string name = Read("DisplayName");
                string uninstallCommand = Read("UninstallString");
                if (name.Length == 0 && uninstallCommand.Length == 0) continue;
                bool hidden = key.GetValue("SystemComponent") is 1 || name.Length == 0;
                bool update = Read("ReleaseType").Length > 0;
                bool noRemove = key.GetValue("NoRemove") is 1;
                string? restriction = noRemove ? "В регистрации приложения указано ограничение на удаление." :
                    update ? "Эта запись относится к обслуживанию Windows." :
                    hidden ? "Это скрытый компонент приложения." : null;
                string location = Read("InstallLocation");
                packages.Add(new(name.Length == 0 ? id : name, id, Read("DisplayVersion"), Read("Publisher"), location,
                    hive == RegistryHive.CurrentUser ? "Текущий пользователь" : "Все пользователи", restriction is not null, hidden,
                    Guid.TryParse(id, out _) ? "--product-code" : "--name", InnoUninstaller(uninstallCommand, location),
                    RemovalRestriction: restriction));
            }
        }
        try
        {
            foreach (var package in new PackageManager().FindPackagesForUser(""))
            {
                var id = package.Id;
                var version = id.Version;
                bool hidden = package.IsFramework || package.IsResourcePackage;
                bool system = hidden || package.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System;
                string name = id.Name;
                try
                {
                    string displayName = package.DisplayName;
                    if (!string.IsNullOrWhiteSpace(displayName)) name = displayName;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
                string location;
                try { location = package.InstalledLocation.Path; }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { location = ""; }
                packages.Add(new(name, id.FamilyName, $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}",
                    id.Publisher, location, "MSIX · текущий пользователь", system, hidden, "--id", PackageFullName: id.FullName,
                    RemovalRestriction: hidden ? "Это пакет ресурсов или общий компонент приложений." : null));
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
        return packages.DistinctBy(p => $"{p.Id}|{p.Scope}|{p.Version}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static string? InnoUninstaller(string command, string location)
    {
        if (command.Length == 0 || location.Length == 0) return null;
        string[] parts = command.StartsWith('"') ? command.Split('"', 3) : [ "", command ];
        string path = parts[1];
        string filename = Path.GetFileName(path);
        return Path.IsPathFullyQualified(path) && Path.GetDirectoryName(path)?.Equals(
                Path.TrimEndingDirectorySeparator(location), StringComparison.OrdinalIgnoreCase) == true &&
            filename.Length == 12 && filename.StartsWith("unins", StringComparison.OrdinalIgnoreCase) &&
            filename.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
    }

    internal static bool Matches(Package package, string query) =>
        package.Id.Equals(query, StringComparison.OrdinalIgnoreCase) ||
        package.Name.Equals(query, StringComparison.CurrentCultureIgnoreCase) ||
        string.Equals(package.PackageFullName, query, StringComparison.OrdinalIgnoreCase) ||
        (package.PackageFullName is not null && package.Id.StartsWith(query + "_", StringComparison.OrdinalIgnoreCase)) ||
        ((query.Equals("Copilot", StringComparison.OrdinalIgnoreCase) ||
          query.Equals("Microsoft Copilot", StringComparison.OrdinalIgnoreCase)) &&
            (package.Name.Equals("Copilot", StringComparison.OrdinalIgnoreCase) ||
             package.Name.Equals("Microsoft Copilot", StringComparison.OrdinalIgnoreCase) ||
             package.Id.StartsWith("Microsoft.Copilot_", StringComparison.OrdinalIgnoreCase)));

    internal static async Task<(int Code, string Output)> RemoveMsixAsync(string packageFullName)
    {
        try
        {
            var result = await new PackageManager().RemovePackageAsync(packageFullName);
            return (result.ExtendedErrorCode?.HResult ?? 0, result.ErrorText);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            return (ex.HResult, ex.Message);
        }
    }

    internal static async Task<(int Code, string Output)> WinGetAsync(
        string verb, string selector, string target, CancellationToken token)
    {
        string winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");
        var start = new ProcessStartInfo(winget)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { verb, selector, target, "--exact", "--disable-interactivity" })
            start.ArgumentList.Add(argument);
        Process? process;
        try { process = Process.Start(start); }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        { return (-1, "WinGet недоступен. Проверь «Установщик приложений» в Windows."); }
        using (process)
        {
            if (process is null) return (-1, "WinGet не запустился.");
            var output = process.StandardOutput.ReadToEndAsync(token);
            var error = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            string combined = (await output + "\n" + await error).Trim();
            return (process.ExitCode, combined.Length > 14000 ? combined[^14000..] : combined);
        }
    }
}
