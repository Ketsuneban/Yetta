# DAX — Defense & Access eXecutive

Russian name: **Исполнительный модуль защиты и доступа**.

DAX is intended to be the unified security and access subsystem for Yetta. Internally it is divided into independent workers and components; visually and conceptually it remains one system.

## Scope

- Observe only programs and commands launched through Yetta, plus their descendants.
- Correlate attributable file, registry, service, scheduled-task, and driver changes where Windows APIs and telemetry make this feasible.
- Do not monitor unrelated applications or act as a machine-wide surveillance agent.
- Enforce DAX Guard before every Yetta-initiated operation.
- Record decisions and operation results in an auditable log.

## Planned internal workers

- **DAX Gate** — policy and authorization decision point.
- **DAX Process** — process launch and descendant tracking.
- **DAX File** — file reputation and integrity checks.
- **DAX System** — attribution of relevant registry/service/task/driver changes.
- **DAX Threat** — integration with Microsoft Defender and optional reputation sources.
- **DAX Audit** — append-oriented security event records.
- **DAX Update** — validation of signed DAX and application updates.

These are logical components, not a commitment to one thread per component. The implementation must use bounded concurrency and explicit ownership of state.

## Warnings

1. **Ordinary access request** — allow or deny access to a resource.
2. **Caution warning** — unknown publisher, unsigned component, suspicious source, or uncertain reputation; show more details before the user proceeds.
3. **System-damage warning** — the requested action may disrupt Windows; explain the risk and require an explicit decision.
4. **Critical warning** — highest system access requires the future TTY (Trusted Token Yield) authorization flow.

The visual design for these warnings will be supplied by the project owner later. Yetta warnings must not impersonate genuine Windows UAC, SmartScreen, or Defender UI.

## Threat intelligence

- Prefer local Microsoft Defender status and local file hashes.
- VirusTotal integration, if added, should perform hash lookups first and must not upload user files automatically. File uploads require separate informed consent and must explain that the file may become available to third-party security researchers.
- A lack of detection is not proof that a file is safe.

## Malware override policy

The project owner wants an Administrator or System profile to be able to explicitly continue after selected DAX 02/03 warnings, accepting responsibility. Lower profiles must not override DAX Guard. Defender-confirmed malware should remain blocked by default; any future exception requires a separately designed, highly visible, auditable policy and must not silently disable Defender or bypass quarantine.

## Privileged service security

The service must not expose arbitrary command execution. Use authenticated local IPC, caller identity validation, strict request schemas, per-operation capabilities, replay protection, rate limits, signed binaries, secure update validation, least-privilege subcomponents where possible, and fail-closed behavior. A compromised Windows kernel or host administrator can still defeat user-mode protections; no absolute security guarantee is possible.
