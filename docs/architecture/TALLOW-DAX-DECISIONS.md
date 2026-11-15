# Architecture decision: Tallow / DAX / Yetta / Marvine

Status: approved product specification, **not a claim of completed implementation**. Owner decisions: 2026-11-15.

## Responsibilities

| Component | Role | Visibility |
|---|---|---|
| **Tallow (TALLOW, TAL)** | Application core: coordinates modules and lifecycle, warning routing, queuing, recovery orchestration, healthy application restart, diagnostics, updates, notifications. | Internal, not presented as an independent end-user application. |
| **DAX** | Separate security companion/service: security policy, actual OS permission checks, encryption, TTY custody and rotation, protection of Tallow, Defender signals, process protection, integrity, audit and backup protection. | Visible through Yetta's DAX surfaces. |
| **Yetta** | User-facing UI, including warnings, update progress, settings, and notification center. | Visible. |
| **Marvine** | Internal console with explicit allowlisted commands. | Visible inside Yetta. |

Operations initiated by Yetta should have the **logical product actor** `Tallow` / `TALLOW` / `TAL` in application logs and UI where appropriate. Do not falsify the actual OS process identity, token, Windows Event Log principal or Defender attribution. A PowerShell command named `/snob` requires a genuine PowerShell wrapper/function or command shim; its application logs may carry the Tallow actor but its operating-system identity remains truthful. It must not bypass PowerShell execution policy, UAC, Defender or Windows access controls.

## Warning mediation

Tallow receives events and routes policy decisions from DAX; DAX remains the security authority. A module must not downgrade a DAX risk decision.

- At most **five individual live warnings**, with overflow represented in a **sixth queue window**. The queue can be browsed; deciding an item removes it from the queue. Pending requests must not be accepted merely because they are queued or a window is closed.
- A fatal event dismisses other warnings and leaves a single top-priority fatal window. Dismissal may close the fatal window without performing recovery. Escape is **not** a dismissal shortcut; Alt+F4 and visible close are permitted under the same denial/cancel semantics as the cross.
- Deduplicate/rate-limit repeated equivalent requests to avoid permission spam while preserving distinct requested operations.
- Archive notification records into grouped folders in Activity Center; default cleanup after 24 hours, configurable. Protected security/audit records must have a separately documented retention and integrity policy: user-visible notification cleanup must not silently erase forensic evidence.
- Warning UI: Windows 11 UAC-inspired geometry without Liquid Glass/blur; use owner assets and appropriate severity palettes. Application-specific icon extraction first, fallback to the owner atlas. Scale with DPI; follow Windows 11 animation and critical-error auditory feedback for fatal warning, respecting accessibility/system mute.
- Keep interface customization in Settings > Interface; do not introduce a separate appearance section under Warning settings.

## DAX authorization and process policy

- TTY: 64 characters, validate after full length. Copy/paste permitted. DAX monitors relevant security events, warns on credible compromise signals and can rotate key with notice. Never claim all RAM/clipboard interception is technically detectable or preventable. Avoid unnecessary plaintext persistence, minimize clipboard lifetime, never log tokens.
- TTY is **an additional Yetta authorization factor**, never an OS token or elevation substitute. DAX must verify actual Windows rights before any privileged action.
- Administrator: explicit, audited, informed override for DAX risk warnings when OS policy permits. System: maximum Yetta product-control privilege with explicit consent and immutable audit; **not** an ability to bypass kernel-enforced security, Defender quarantine or Windows permissions. Only trusted SYSTEM-scoped management can modify Tallow/DAX policy, subject to appropriately scoped and authenticated service controls. Windows administrators can still control many user-mode services; never assert impossible tamper-proofing.
- DAX monitors Yetta-launched processes and descendants by default; user may explicitly enroll external applications. Do not monitor all unrelated apps by default.
- For suspicious behavior, request safe process suspension where supported, present a decision and terminate only after user authorization. If suspension cannot be performed, communicate the gap and apply fail-closed policy to Yetta-originated operations.
- Use Windows Defender as a contributing verdict signal; no claim of guaranteed malware detection and no stealth disabling/tampering with Defender.

## Recovery and backup design (planned)

- DAX is a companion service/program; every 20 minutes it should asynchronously create a consistent, encrypted, authenticated snapshot/image of its installable state and required configuration in access-controlled storage. Validate before promotion; retain at least one **last-known-good** backup until the next backup is verified. Never delete the only good image before replacement. Avoid UI-thread I/O and large stop-the-world operations; rate-limit bandwidth/disk and report failures.
- On DAX-signaled Yetta failure, Tallow attempts non-destructive diagnosis and bounded, verified repair, then controlled restart; next a verified application backup; finally a signed, provenance-checked GitHub release with rollback plan. No silent execution of downloaded unverified binaries or unbounded restart loops. Notify user of recovery outcome; unattended reinstall requires prior explicit opt-in and appropriate trust.
- Fatal **Reset** is a future workflow restoring Windows to the latest **healthy, available** restore point and DAX to its latest verified good backup. Before a destructive OS recovery, request user confirmation and summarize loss of apps/configuration. Coordinate with Windows System Restore / WinRE using documented supported mechanisms; create restore points when supported, validate System Protection and access rights, do not pretend recovery can always succeed.
- If restoration fails, offer alternative points. A WinRE/offline recovery integration is a separate signed, tested deliverable and should never be marketed as available until implemented and verified.
- Recovery operations are not currently implemented; the existing Reset merely opens DAX Recovery. Preserve this non-destructive behavior until verified mechanisms and tests exist.

## Distribution and updates

- Release channels: Stable, Preview and Experimental; channel selection must not silently change trust scope or install a privileged broker.
- User-triggered update check offers **Download update** (in-app progress, verified download and explicit install handoff) and **Download manually** (GitHub release page). Keep Windows-required authorization/installer UI where it is legally and technically required; do not imply the app can hide UAC or safely self-replace running binaries.
- User-facing site may be retired/hidden for now; do not delete hosting, domain, or public source without a separate explicit deletion decision. CHANGELOG shown publicly should describe user-facing changes only, not private design conversation.
- Extend Windows CI with safety, UI policy, recovery and queue tests. Gate releases on passing builds and publisher checks. Signing/release provenance must be established before automatic DAX or service updates.

## Explicit gaps

Current preview lacks Tallow as a standalone orchestrator, a production DAX companion service, reliable process suspension/enrollment, Defender verdict integration, WinRE integration, secure 20-minute image backups, Windows restore orchestration, the six-window queue, full notification persistence, and seamless installer UI. Design here must not be represented as live functionality.
