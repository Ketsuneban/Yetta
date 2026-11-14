# Yetta architecture overview

## Current state

A first unprivileged Windows desktop preview (0.1.0-preview) is implemented in C#/.NET 10/WPF. It provides navigation, Fluent-inspired and Glass-inspired palettes, local preferences, read-only system information, user-confirmed shortcuts to Windows tools, and an allowlisted internal console.

The preview does **not** include a working Access Manager, DAX, DAX Guard, privileged service, TTY, or Windows system editors. Those remain specifications and future work.

## Planned high-level components

- **Yetta UI** — two presentation modes sharing one application and settings model: Windows 11 Fluent-style and a custom translucent glass interface.
- **Yetta Core** — module lifecycle, settings, localization, diagnostics, and update coordination.
- **Access Manager** — installation, status inspection, and revocation of the five access profiles.
- **DAX** — a unified policy, security, and process-observation subsystem. Internally it is split into specialized logical workers while appearing as one system to the user.
- **DAX Guard** — a mandatory policy gate. It checks the requested operation against the active access profile before execution.
- **Yetta Console** — commands are initiated by the user; the application does not autonomously perform broad system changes.
- **Modules** — independent editors for individual Windows settings and components.

## Process scope

DAX should monitor only processes launched through Yetta and the descendants of those processes. It should not become a machine-wide surveillance agent. Where technically feasible, it should correlate relevant file, registry, service, scheduled-task, and driver changes with that process tree. Gaps in attribution must be reported rather than silently treated as safe.

## Privileged boundary

A future privileged service may perform narrowly defined operations after validating the caller, request schema, authorization, policy, and integrity state. It must not expose unrestricted SYSTEM command execution to the UI or arbitrary local processes. Each operation should have an explicit capability, bounded inputs, and an auditable result.

## Open decisions

- Exact long-term UI framework and app packaging strategy (the first preview uses WPF; WinUI 3 remains a possible target).
- Formal permission matrix for each access profile.
- TTY (Trusted Token Yield) format, trust model, issuance, expiry, and revocation.
- License for public source code.
- Signing certificate and release provenance strategy.
- Detailed Windows editor/module scope, to be specified by the owner.
