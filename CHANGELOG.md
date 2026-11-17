# Yetta Changelog

## 0.3.20-preview — 2026-11-17

- Проверка новых релизов при запуске и каждую минуту во время работы.
- Системное уведомление Windows с прогрессом загрузки и кнопкой отмены.
- Автоматический запуск установщика после проверки SHA-256; ручное обновление сохранено.
- Переключатель автоматических обновлений в настройках.

## 0.3.17-preview — 2026-11-15

- Watch local.json for changes and poll GitHub main for new text approximately every minute.
- Apply changes to open WPF windows and the DAX tray without restarting processes.
- Download only validated, size-limited catalogs over HTTPS using conditional requests and atomic local writes; keep the last known good text if updates fail.
- Preserve user input and confirmation state while refreshing static UI elements.
- Add smoke tests for live text changes, invalid JSON rejection and change notifications.


## 0.3.16-preview — 2026-11-15

- Introduce a shared `local.json` for Yetta, Tallow, DAX, and Marvine, with Russian and existing English texts in the same catalog.
- Move static WPF labels and C# messages into common key lookups without changing internal security decisions.
- Load and cache the shared file in both EXEs, with an embedded fallback when it is missing or damaged.
- Include `local.json` in the installer, portable release assets, and CI checks.


## 0.3.15-preview — 2026-11-15

- Add Tallow's five-window limit for nonmodal warnings with a browsable sixth queue window, deduplication, and fatal priority.
- Allow users to reject queued requests; queued approvals still require the full warning dialog.
- Keep Escape from dismissing warning dialogs and play the Windows critical-error system sound for fatal warnings.
- Store activity notifications locally, group them by source and allow retention settings (24h default).
- Restore existing Yetta UI from DAX tray on left-click when Windows allows it.
- Add smoke tests for queue behavior and local notification cleanup.
- DAX service/Defender, recovery, privileged operations and full six-window decision UI remain planned.


## 0.3.14-preview — 2026-11-15

- Add the lightweight DAX tray companion and six owner-provided icons.
- Show Windows tray context actions and initial component availability reports.
- Gate Yetta usage when the companion is disabled, closed or unavailable.
- Fix the 0.3.13 installer omission: include both Yetta.exe and Yetta.DAX.exe.
- Full DAX scanning, protection and recovery are still planned.

## 0.3.12-preview — 2026-11-15

- Fix warning blue statuses for Agreement, DAX, Guest, SafeUser and Windows Safe Mode.
- Correct generic application fallback icon mapping and distinguish DAX informational warnings from DAX-rejected actions.
- Keep warning explanations visible with hidden scrollbars and fixed action footer.
- Make single warning action buttons stretch to the available width and square their corners.
- Use the supplied image icon for the close control, and display muted unknown publisher text.
- This release does not introduce real Windows rollback, DAX restoration or malware scanning; fatal recovery remains a non-destructive placeholder.

## 0.3.10-preview — 2026-11-15

- Apply the dark UAC-inspired warning layout to all seven Yetta warning kinds. Each has its own restrained header, body, and footer palette and a matching highlighted button; source icon remains independent of warning severity.
- Preserve fixed action row, scrollable details, confirmation checkbox, TTY verification, topmost fatal window, DAX Recovery navigation and existing audit behavior. This is an app-owned dialog, never a Windows UAC replacement.
- Increase button hit targets (including TTY copy and key rotation) and keep the entire control surface clickable.
- Add modest, reduced-motion-aware animations to button hover/press, sidebar navigation, caption controls, page transitions and warning appearance.
- Bring queued improvements from the working branch: unified Windows Terminal-like tab strip with Marvine/PowerShell and a single non-elevated PowerShell session, removal of default ScrollViewer focus outlines and restored native-style caption glyphs.
- Keep experimental unsigned Administrator broker in its separate development branch; it is not part of this public release.


## 0.3.9-preview — 2026-11-15

- Rework **only Standard** warnings to a dedicated dark, Windows UAC-inspired presentation: compact caption, prominent action question, icon/publisher row with adjacent DAX status, expandable "Подробнее" description, and fixed bottom action bar.
- Retain the original Yetta icons, independently configured header icon, operational approval checks, TTY verification, DAX audit and nonmodal informational warnings. This is not Windows UAC or an OS privilege prompt.
- Remove remaining thin decorative UI hairlines by suppressing legacy OutlineBrush strokes in both themes, simplifying access dropdown and terminal field borders, and asking Windows 11 DWM not to draw the native app window border.
- Keep the six other warning designs unchanged.
- Bump app, updater, installer, site and downloadable release metadata to v0.3.9-preview.


## 0.3.8-preview — 2026-11-15

- Integrate accumulated thin-line removal in main window shell, sidebar, cards, toasts and console.
- Rework all seven warning visuals to a non-glass, squared Windows/Fluent-like appearance; each complete warning window is severity-tinted with high-contrast text.
- Center pinned warning buttons and move status to the same horizontal row as the object's icon.
- Decouple top event icon from the severity type through the optional HeaderIconId.
- Use DPI-independent vector equivalents for the tiny original warning/status badges to avoid pixelated enlargement.
- Replace the DAX Guard sidebar icon with the DAX logo and Sources with the owner-provided external-link PNG.
- Preserve non-modal overlapping warning behavior, confirmation/TTY gates, audit logging and Windows permission boundaries from 0.3.7.


## 0.3.7-preview — 2026-11-15

- Fix warning stacking: informational windows, DAX messages and all seven Settings previews can now remain open together instead of blocking the main app.
- Fatal UI previews stay topmost, with Reset only navigating to DAX Recovery (no OS reset).
- Retain blocking modal confirmation for actual operations such as PowerShell preflight, installer updates and TTY/key rotation: no implicit approval.
- Preserve the owner-provided 50-icon atlas, cursors, encrypted credential storage, Yetta-only audit and Windows 11 backdrop introduced in 0.3.6.
- Still no audited ADMIN/SYSTEM broker, complete Defender scan or automatic Windows restoration; this remains a preview.


## 0.3.6-preview — 2026-11-15

- Add a reusable seven-variant warning UI (standard, caution, dangerous, critical, fatal, agreement, DAX) with owner-provided warning, file-type and status icons.
- Keep action buttons and checkbox/TTY input fixed outside the scrollable text body; display multiple independent windows, fatal always topmost.
- Add encrypted per-user 64-character rotating TTY acknowledgement key via Windows Credential Manager; copy is explicitly warned; token never grants Windows rights.
- Add DAX local JSONL audit and track only the child-process tree of Yetta's embedded PowerShell (not every system process).
- Add heuristic preflight of potentially harmful user-entered PowerShell commands before execution. This is not Defender or malware detection.
- Add 50 numbered 32x32 PNG icons atlas from the owner-provided archive; use icons in navigation and warning UI; test-only cursor 17 selected for arrow.
- Add best-effort Windows 11 Acrylic backdrop in the Glass theme and a blurred, darkened owner surface behind own alert windows, maintaining text contrast.
- Convert native WPF MessageBoxes into Yetta warning windows; add interactive warning previews to Settings.
- Fatal warning "Reset" navigates to DAX Recovery; does not restore Windows until an audited recovery engine exists.
- ADMIN/SYSTEM broker, full ConPTY and Defender threat integration remain unavailable; never silently bypass OS security.


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
