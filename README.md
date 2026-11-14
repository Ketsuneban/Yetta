# Yetta

**Yetta** is a planned Windows 11 customization and system-management platform. This repository currently contains the public project website, architecture notes, security requirements, and project documentation. The Windows application itself is intentionally not included yet.

- Website: https://yetta.pages.dev/
- Repository: https://github.com/Ketsuneban/Yetta
- Reference design: https://yeta-6ip.pages.dev/

## Project status

The project is in the infrastructure and specification phase. No Windows service, privileged helper, Access Manager, DAX executable, or system-modification code is shipped from this repository at this stage.

## Planned systems

- **Yetta Core** — the future Windows application shell and module host.
- **Access Manager** — five user-selected access profiles: Guest, Safe User, User, Administrator, and System.
- **DAX (Defense & Access eXecutive)** — a unified security and access subsystem. DAX is intended to govern only processes and operations initiated through Yetta, including relevant descendants and their system effects.
- **DAX Guard** — enforces the minimum access profile required by each operation.
- **Yetta Console** — a user-operated command interface; Yetta should not autonomously make system changes.

See `docs/` for the current specification and open questions.

## Website development

The website is a dependency-free static site in `site/`.

- Entry point: `site/index.html`
- Styles: `site/styles.css`
- Behavior: `site/app.js`
- Build command: none
- Output directory: `site/`

Cloudflare Pages is configured to serve the `site/` directory from the `main` branch at `https://yetta.pages.dev/`.

## Security and responsible disclosure

Security is a first-class project requirement. Read `SECURITY.md` before reporting a vulnerability. Do not publish exploit details for an unpatched issue in a public issue.

No software can guarantee absolute protection against a fully compromised operating system or kernel. The future DAX design must minimize privileged attack surface, use a narrow authenticated IPC contract, verify signed components and updates, isolate untrusted modules, and fail closed when integrity checks fail.

## License

The project owner has not selected a software license yet. Until a license is added, public visibility does not grant permission to reuse, modify, or redistribute the code.
