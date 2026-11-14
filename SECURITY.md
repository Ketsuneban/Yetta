# Security Policy

## Scope

This repository currently contains the Yetta project website and design/specification documents. The Windows application and DAX implementation have not been started.

## Reporting a vulnerability

Please do not publish exploit code, credentials, private keys, or unpatched vulnerability details in public issues. Use GitHub's private vulnerability reporting for this repository if it is enabled. If private reporting is unavailable, contact the repository owner through a private channel before disclosing details publicly.

Include the affected component and revision, impact, reproduction conditions, and a suggested mitigation where possible. Do not include real user data.

## Security principles

Future privileged components must:

- use least privilege and narrowly scoped operations;
- authenticate and authorize every privileged IPC request;
- reject unknown, malformed, stale, or unsigned requests;
- verify code-signing and update signatures before installation;
- protect secrets with OS-backed key storage rather than hard-coded keys;
- keep auditable records of privileged actions;
- isolate user-supplied modules and track process descendants;
- fail closed when policy, integrity, or authorization checks fail.

A SYSTEM-level service is a high-value attack target. It must never expose a general-purpose unauthenticated command shell to local processes. No design can promise absolute immunity against a compromised kernel or fully privileged host.
