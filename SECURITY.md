# Yetta Security Policy

## Current implementation

The published Yetta 0.3.9-preview is a non-elevated WPF application. Its
Administrator profile does not yet provide OS privileges.

**In the non-published feature branch** there is a dedicated Windows
Administrator broker prototype. It is not tested, signed or ready for use.
Before shipping, require a signed trusted installer, Windows 10/11 testing,
and an independent security review, especially of named-pipe authentication,
service configuration and uninstall behavior.

The draft broker executes ONLY built-in actions scoped to
`HKLM\\SOFTWARE\\Yetta\\AdminBroker`. It explicitly disallows
arbitrary shell commands, other program launches, UAC bypass and SYSTEM
operations requested by untrusted code. It records protected machine-level
audit entries. Read [the detailed threat model](docs/security/administrator-broker.md).

## Reporting vulnerabilities

Do not publish proof-of-concept exploits, private credentials or sensitive user data in public GitHub issues. Use GitHub private vulnerability reporting if enabled or contact the maintainer privately at invzum@gmail.com.

Please include the affected release/tag, reproduction steps and relevant environment information without exposing private data. Antivirus detections must be investigated rather than treated as automatic false positives.

## Design principles for future privileged components

- Least privilege; strict authentication and per-operation authorization.
- Never expose arbitrary SYSTEM shell execution to untrusted local clients.
- Validate signed releases and updates; verify file hashes for integrity.
- Separate UI, policy engine and privileged service.
- Make permission decisions fail closed on unknown or invalid authorization.
- Protect logs and secrets using supported Windows facilities.
- Preserve normal Windows uninstallation and user control.

No user-mode software can guarantee protection from a compromised kernel or full local administrator.
