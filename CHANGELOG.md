# Yetta Changelog

## 0.4.0-preview — 2026-11-15

- Introduce seven warning families (standard, caution, dangerous, critical, fatal, consent and DAX), the owner's numbered icons and cursor symbols, distinct severity tints, scrollable description and fixed actions.
- Replace WPF MessageBox windows with Yetta warning windows; approval, optional agreement checkbox and 64-character TTY verification are explicitly separated from OS permissions.
- Persist a random TTY confirmation key in user-scoped Windows DPAPI, supporting copy and rotation; add Windows security smoke tests.
- Add DAX Process: on-demand SHA-256, Authenticode signature status, Defender file scan, user-approved non-elevated launch and limited WMI child-process observation. Fail closed when Defender reports a nonzero result or a file changes during scan.
- Extend monitoring to Yetta's own embedded user PowerShell process; append bounded real event records to DAX Logs.
- Add opt-in VirusTotal lookup by SHA-256 hash only, without automatic file upload.
- Add warning preview in Settings; fatal warning has topmost priority and its «Сброс» button navigates to DAX Recovery without attempting to reset Windows.
- Request Desktop Acrylic through DWM on Windows 11 22621+ in the Glass theme, with Fluent opaque and older Windows fallback variants; improve text contrast and single-rounded modal chrome.
- Extend the safe Marvine commands and add DAX Process navigation; preserve per-user installation and in-app update checks.
- Still not shipped: privileged ADMIN/SYS broker, ConPTY terminal emulation, automatic Windows recovery, full malware detection, signed release/update chain.


## 0.3.5-preview — 2026-11-14

- Remove the second, rectangular native frame from the glass DAX and consent windows.
- Use frameless WPF windows with per-pixel transparent rounded corners and one visible rounded border; retain modal behaviour.
- Restore dragging from the window heading; retain permanently visible footer actions and consent checkbox.
- Use transparent (alpha) layered gradient for the glass surface instead of a fully opaque rectangular backplate.
- Document that layered WPF transparency is not native DWM blur/refraction; avoid falsely claiming it.


## 0.3.4-preview — 2026-11-14

- Restore the owner-provided DAX mark from the original high-resolution source, replacing the blurry 40x40 asset with a clean 160x160 version.
- Replace the DAX popup with glass-inspired panels, specular highlights and a fixed footer; longer descriptions scroll inside their own region.
- Add a shared WPF glass modal layout for DAX and consent dialogs, keeping confirmation buttons and the consent checkbox outside scrollable text.
- Enable best-effort Windows 11 DWM dark titlebar and translucent system backdrop where supported, with a WPF gradient/glass fallback.
- Preserve existing permissions; no real DAX runtime, SYSTEM broker or malicious-code detection is claimed.


## 0.3.3-preview — 2026-11-14

- Fix the consent dialog layout: required checkbox is fixed above action buttons and cannot be clipped by longer text.
- Scroll long descriptions separately without hiding agreement controls; dialog is resizable and respects display work area.
- Keep the accept button disabled until explicit consent is checked; cancellation still works.
- Bump version and preserve the existing in-app updater and GitHub Releases workflow.


## 0.3.2-preview — 2026-11-14

- Hide the sidebar scrollbar without disabling mouse-wheel navigation.
- Apply custom rounded, theme-aware scrollbar thumbs to scrolling content.
- Replace the native DAX Guard ComboBox visuals with a rounded dropdown, themed item states and a custom arrow.
- Integrate the Windows title bar into Yetta with a dark themed header, the original logo, and minimize/maximize/restore/close controls.
- Retain native Windows resize borders and dragging through WPF WindowChrome.
- No changes to OS privileges or DAX policy.


## 0.3.1-preview — 2026-11-14

- Add the Activity Center: up to five timed toast notifications, details, entry animation and optional sound.
- Make the navigation pane scrollable and reset page scroll on section changes.
- Consolidate device details in one card and display the user-local data folder.
- Improve DAX Guard dropdown contrast and gray access level identifiers.
- Rename Mavrine to Marvine; add safe activity/overview commands and reserve /snob without OS elevation.
- Link Icons8 in the footer, include owner-provided icon 31 and update website branding and release links.
- ADMIN/SYS, threat detection and recovery remain unavailable until security-reviewed implementation.


## 0.3.0-preview — 2026-11-14

- Build and publish YettaSetup.exe per-user installer and direct portable Yetta.exe.
- Add settings-based release check and explicit SHA-256 verified installer download.
- Add gray consent dialog with checkbox and original warning icons 26, 27, 35.
- Embed genuine, non-elevated PowerShell text streams inside Yetta; retain current-user permissions and disclose ConPTY limitations.
- Apply owner-provided Yetta/DAX logos and Windows EXE icon.
- Add DAX information popup, access level identifiers, updated documentation.
- Publish encrypted v0.3 assistant instruction supplement.
- Real ADMIN/SYS broker, signed updates, full notification center and other numbered icons remain future work.

## 0.2.1-preview — 2026-11-14

- Fix broken GitHub Releases link for prerelease-only projects in Sources.
- Link the home page download button directly to this version's EXE.
- Synchronize preview version across application, site and documentation.

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
