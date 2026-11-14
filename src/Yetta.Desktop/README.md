# Yetta Desktop — 0.1.0-preview

A working, unprivileged Windows 11 UI prototype built with C# and .NET 10 (WPF). WinUI 3 remains the preferred future UI target; WPF provides an initial buildable version without external UI packages.

## What works

- Desktop window: Home, Modules, Console, Access Manager, DAX, Settings, About.
- Two UI palettes (Fluent-inspired and Glass-inspired) without restarting.
- Persistent local preferences and recovery from invalid settings JSON.
- Read-only system information: OS, architecture, runtime, account, computer, CPU count, uptime.
- User-confirmed shortcuts to Windows Settings, Explorer and Windows Security.
- Allowlisted Yetta Console commands: help, version, status, modules, access, dax, settings, theme fluent, theme glass, clear.
- Read-only Access Manager and DAX information screens. These are NOT operational security components.
- GitHub Actions builds a self-contained Windows x64 ZIP for download.

## Not implemented

No registry edits, service changes, drivers, process monitoring, Windows configuration changes, elevation, UAC impersonation, TTY, Windows Defender bypass, privileged helper, or DAX Guard. The Access Manager screen shows Guest independently of the actual process token. Windows admin status is shown for diagnostics only.

## Build and run

Requirements: Windows 11 x64, .NET 10 SDK for development.

Run from source:

    dotnet run --project src/Yetta.Desktop/Yetta.Desktop.csproj

Build:

    dotnet build src/Yetta.Desktop/Yetta.Desktop.csproj -c Release

Core smoke checks:

    dotnet run --project tests/Yetta.Core.Smoke/Yetta.Core.Smoke.csproj -c Release

Publish Windows x64 (self-contained):

    dotnet publish src/Yetta.Desktop/Yetta.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/Yetta-win-x64

The .github/workflows/desktop-build.yml workflow builds the preview and uploads Yetta-0.1.0-preview-win-x64.zip to each successful workflow run's Artifacts section. Extract the archive before starting Yetta.exe.

This preview is UNSIGNED. Do not disable Windows Security to launch it.

## Local data

Preferences: %LOCALAPPDATA%\Yetta\settings.json. No cloud service is used. Console history is kept in memory and discarded on exit. User and machine names are displayed locally only and are not transmitted by the app.

## Security limits

The console is NOT a shell; it only supports known internal commands and cannot invoke arbitrary processes. Starting a Windows system app requires an explicit user action and, by default, a confirmation. No elevation or privileged service is installed.

Future Access Manager / DAX development requires an independent security review.

## Current limitations

- WPF rather than final WinUI 3.
- Glass mode is visual styling, not system Acrylic/Mica.
- No installer, signed binary or production-grade icon packaging yet.
- CI verifies compilation and core logic, but interactive usability still requires a Windows 11 test.
