# Yetta Changelog

## 0.2.0-preview — 2026-11-14

- Implement eight application sections specified by the project owner.
- Rewrite Overview tiles; consolidate local device and app capabilities on Information.
- Add Mavrine / Windows Terminal / PowerShell presentation modes.
- Launch the genuine Windows Terminal without elevation only from active logical User profile.
- Add logical DAX Guard Guest/SafeUser/User install/apply/dropdown workflow; block Admin/System profiles until secure authorization is implemented.
- Add truthful DAX Logs and DAX Recovery placeholder screens.
- Add Sources links: GitHub, releases, Yetta website, Icons8 and invzum@gmail.com.
- Reduce the public website to Home and GitHub.
- Apply MIT license and distribute open-licensed Inter font (SIL OFL 1.1), verified at build time.
- Create GitHub Releases workflow for **direct Yetta.exe** downloads and SHA-256 manifest, without nested ZIPs.
- Keep every privileged or system-modification feature disabled until audited.

## 0.1.0-preview — 2026-11-14

- First unprivileged C# / .NET 10 / WPF desktop shell and core smoke tests.
- First site hosted on Cloudflare Pages, open-source infrastructure baseline.
- Project continuity instructions stored in password-protected AES-256 ZIP in `Project/`.
