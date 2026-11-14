# Yetta Security Policy

## Current implementation

Yetta 0.2.0-preview is an **unprivileged UI prototype**, not an antivirus, endpoint protection agent or full Windows administration tool. It implements local UI preference storage, logical profile policy checks, a fixed set of internal Mavrine commands, and launching genuine Windows Terminal without elevation only when its logical User profile is active.

**Not implemented:** a privileged DAX service, verified Administrator or SYSTEM profile, process monitoring, Windows registry/service changes, Defender integration, system restore points, anti-tamper protection, and trusted updating. Do not use it to protect critical devices or as a replacement for Microsoft Defender.

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
