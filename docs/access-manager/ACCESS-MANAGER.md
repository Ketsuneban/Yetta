# Access Manager specification

Access Manager is the single user-facing place to inspect the current access profile, install a higher profile, and revoke installed capabilities.

## Profiles

1. **Guest** — default after installation; minimal app capabilities.
2. **Safe User** — read-only inspection of resources the granted Windows token can actually read. It must not claim universal access to every protected file.
3. **User** — operations available to the current standard user account.
4. **Administrator** — operations requiring an elevated administrator token. Initial elevation must use the genuine Windows consent mechanism.
5. **System** — selected operations performed by a dedicated, hardened service running with the necessary system privileges. This profile is not an assertion that Windows protections can all be bypassed.

## Installation and persistence

The user explicitly installs a profile and grants the required Windows permission during setup. The app may then reuse the installed capability without repeatedly prompting for the same ordinary operation, until the user revokes it or the authorization expires/is invalidated.

A displayed profile is not sufficient proof of access. The manager must query and display actual token/service capabilities, installation health, and limitations.

## Enforcement

DAX Guard is mandatory for every Yetta-initiated operation. If the active profile is below the operation's declared minimum, the operation is denied before execution. There is no fallback to a more privileged context without explicit profile installation.

## Revocation and recovery

Revocation must stop new privileged requests, invalidate sessions/tokens, disable or remove the privileged service safely, and record the result. A failed or partial revocation must be clearly reported.
