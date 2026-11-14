# Yetta roadmap

## Phase 0 — Infrastructure (completed baseline)
- [x] Create public repository.
- [x] Publish project website and written specifications.
- [x] Verify Cloudflare Pages production deployment.
- [ ] Confirm repository license with project owner.
- [ ] Configure repository branch protection and security settings (manual GitHub Settings step; connector write API unavailable).
- [x] Create encrypted project continuity archive (WinZip AES-256; password supplied separately).

## Phase 1 — Product specification
- [ ] Confirm final UI design for both visual modes.
- [ ] Confirm all four warning designs (owner will provide them).
- [ ] Define exact access profile capability matrix.
- [ ] Define DAX threat model and process attribution limits in implementation detail.
- [ ] Define TTY trust and lifecycle model.
- [ ] Collect detailed requirements for each Windows editor from the owner.

## Phase 2 — Application foundation
- [x] Implement first unprivileged app shell (WPF preview, not final WinUI 3).
- [x] Implement local settings and a safe read-only module catalogue.
- [x] Add Core smoke tests and a Windows build workflow.
- [ ] Test the interactive application on the owner's Windows 11 machine.
- [ ] Decide whether to migrate the UI from WPF to WinUI 3.
- [ ] Add a signed installer/release process.

## Phase 3 — Access and security foundation (not started)
- [ ] Implement Access Manager.
- [ ] Implement a narrowly scoped privileged broker.
- [ ] Implement DAX Guard and audit events.
- [ ] Perform independent security review before enabling System profile.

## Phase 4 — Modules and releases (not started)
- [ ] Add individual Windows editors.
- [ ] Add update verification and signed releases.
- [ ] Publish reproducible build instructions and release artifacts.
