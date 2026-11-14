# Encrypted Yetta assistant handoff

Project instructions are encrypted, not published as plaintext.

- `Yetta_Continuity_AES256.zip`: original full continuity handoff (through Yetta 0.2.x).
- `Yetta_v0.3_Supplement_AES256.zip`: encrypted owner instructions added in November 2026 for one-time consent, warning status icons 26/27/35, SYS/Admin requirements, integrated real PowerShell, installer and updater.
- `Yetta_v0.3.1_Supplement_AES256.zip`: encrypted continuation from the 0.3.1-preview release. Contains changes shipped (Activity Center, scroll fixes, Marvine, UI adjustments), CI and deployment state, remaining work and safety constraints. Read after the original continuity and the 0.3 supplement. This file does **not** replace either older archive.


**Future assistant:** read BOTH encrypted archives, in that order, and then inspect the current GitHub repository / release state. The supplement supersedes older statements where requirements changed. Do not infer that a feature has been deployed without inspecting code, Actions and published assets.

ZIPs use WinZip AES-256 (AE-2). Use 7-Zip or WinRAR; Windows File Explorer may not extract AES ZIP. The owner provides the password separately. Never commit the password or an unencrypted copy of private assistant instructions.

An up-to-date consolidated archive `Yetta_Continuity_AES256_v0.3.zip` was also prepared as a conversation artifact; it should eventually replace the original snapshot after a byte-for-byte verified transfer. The two published encrypted archives together preserve the changes in GitHub.
