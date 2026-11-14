# Encrypted Yetta assistant handoff

Project instructions are encrypted, not published as plaintext.

- `Yetta_Continuity_AES256.zip`: original full continuity handoff (through Yetta 0.2.x).
- `Yetta_v0.3_Supplement_AES256.zip`: encrypted owner instructions added in November 2026 for one-time consent, warning status icons 26/27/35, SYS/Admin requirements, integrated real PowerShell, installer and updater.
- `Yetta_v0.3.1_Supplement_AES256.zip`: encrypted continuation from the 0.3.1-preview release. Contains changes shipped (Activity Center, scroll fixes, Marvine, UI adjustments), CI and deployment state, remaining work and safety constraints. Read after the original continuity and the 0.3 supplement. This file does **not** replace either older archive.
- `Yetta_v0.4_Supplement_AES256.zip`: encrypted planning and change log for the seven warning categories, owner-provided icon atlas, DPAPI key management, DAX Process/Defender constraints, Acrylic compatibility, testing and remaining ADMIN/SYS/ConPTY/recovery work. Read after all previous archives; it does **not** replace them.


**Future assistant:** read ALL encrypted archives in chronological order, and then inspect the current GitHub repository / release state. The supplement supersedes older statements where requirements changed. Do not infer that a feature has been deployed without inspecting code, Actions and published assets.

ZIPs use WinZip AES-256 (AE-2). Use 7-Zip or WinRAR; Windows File Explorer may not extract AES ZIP. The owner provides the password separately. Never commit the password or an unencrypted copy of private assistant instructions.

An up-to-date consolidated archive `Yetta_Continuity_AES256_v0.3.zip` was also prepared as a conversation artifact; it should eventually replace the original snapshot after a byte-for-byte verified transfer. The encrypted archives in GitHub preserve the cumulative changes; append new supplements after major releases.
