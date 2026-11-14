# Yetta

**Yetta** is an evolving Windows 11 customization and system-management project. The repository includes the public website, architecture and security specifications, and an initial **unprivileged Windows desktop preview (0.1.0-preview)**. Advanced system editing, Access Manager and DAX are not implemented.

- Website: https://yetta.pages.dev/
- Repository: https://github.com/Ketsuneban/Yetta
- Reference design: https://yeta-6ip.pages.dev/

## Project status

The project has entered the desktop application foundation phase. A Windows UI prototype is included, but **no Windows service, privileged helper, working Access Manager, DAX executable, or system-modification features** are shipped.

## Planned systems

- **Yetta Core** — the future Windows application shell and module host.
- **Access Manager** — five user-selected access profiles: Guest, Safe User, User, Administrator, and System.
- **DAX (Defense & Access eXecutive)** — a unified security and access subsystem. DAX is intended to govern only processes and operations initiated through Yetta, including relevant descendants and their system effects.
- **DAX Guard** — enforces the minimum access profile required by each operation.
- **Yetta Console** — a user-operated command interface; Yetta should not autonomously make system changes.

See `docs/` for the specifications. The first app prototype is in [`src/Yetta.Desktop`](src/Yetta.Desktop), with build instructions in [`src/Yetta.Desktop/README.md`](src/Yetta.Desktop/README.md). The machine-readable continuity specification is [`Project/Yetta_Project.json`](Project/Yetta_Project.json).

## Website development

The website is a dependency-free static site in `site/`.

- Entry point: `site/index.html`
- Styles: `site/styles.css`
- Behavior: `site/app.js`
- Build command: none
- Output directory: `site/`

Cloudflare Pages project `yetta` is connected to this repository and serves the `site/` directory from the `main` branch at https://yetta.pages.dev/. The site has no build step; the Pages output directory is `.` relative to the `site/` root.

## Security and responsible disclosure

Security is a first-class project requirement. Read `SECURITY.md` before reporting a vulnerability. Do not publish exploit details for an unpatched issue in a public issue.

No software can guarantee absolute protection against a fully compromised operating system or kernel. The future DAX design must minimize privileged attack surface, use a narrow authenticated IPC contract, verify signed components and updates, isolate untrusted modules, and fail closed when integrity checks fail.

## License

The project owner has not selected a software license yet. Until a license is added, public visibility does not grant permission to reuse, modify, or redistribute the code.
