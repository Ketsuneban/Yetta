# Changelog

All notable project changes are documented here.

## Unreleased

- Add the first Windows 11 desktop preview (0.1.0-preview) using C# / .NET 10 WPF.
- Add navigation, two visual palettes, persisted preferences, read-only system information and allowlisted console commands.
- Add Access Manager / DAX informational screens that explicitly state the features are unavailable.
- Add Windows GitHub Actions build and Core smoke tests.
- Add the encrypted assistant continuity archive (WinZip AES-256) and remove the unencrypted assistant handoff JSON from the public repository.
- Fix the Windows artifact layout so downloading the GitHub Actions artifact yields the application files directly, without a ZIP nested inside another ZIP.
- Keep all privileged system operations and DAX enforcement disabled.

- Initialize the Yetta public repository and project documentation.
- Publish the first Yetta project website and security/architecture specifications.
- Connect the public GitHub repository to Cloudflare Pages at `https://yetta.pages.dev/`.
- Add the DAX, Access Manager, security, and GitHub hardening specifications.

## 0.1.0 — Infrastructure baseline

- Initial public project website and documentation baseline.
- No Windows application binaries or privileged components were included in the infrastructure-only baseline.
