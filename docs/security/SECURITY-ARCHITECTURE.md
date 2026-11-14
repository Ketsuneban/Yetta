# Yetta security architecture

## Security objectives

Protect the host from unintended Yetta actions, protect DAX from misuse by other local processes, and reduce the consequences of a compromise in any one component.

## Required controls

- Code signing for release binaries and privileged components.
- Signed update manifests and packages; verify signatures and version/rollback policy before applying updates.
- Windows DPAPI/CNG or another OS-backed key store for local secrets; never hard-code private keys or API tokens.
- Authenticated IPC with strict caller checks, request validation, replay protection, and per-operation authorization.
- A small privileged service surface with no general-purpose shell endpoint.
- Separate low-privilege UI, module workers, and privileged operations.
- ACL-protected service files, configuration, and logs.
- Integrity checks for critical files and configuration; fail closed on mismatch.
- Append-oriented audit records with timestamps, operation IDs, caller identity, policy decision, and result.
- Safe update channel with staged rollout, rollback protection, and recovery procedure.
- Dependency pinning, automated static checks, and documented supply-chain provenance.

## Limitations

No application can make itself impossible to access or modify on a host controlled by a kernel-level attacker or a fully privileged local administrator. Security claims must describe tested controls and residual risks, not promise invulnerability.

## Public releases

Every public release should include source revision, build workflow, artifact hashes, signature status, changelog, and a security contact. Security fixes should be published with enough detail for users to understand impact without exposing unpatched exploit instructions.
