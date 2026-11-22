# Yetta security policy

Yetta 0.7.0-preview runs a per-user WPF interface, Tallow coordination, Marvine console and DAX companions. It is a preview, not a replacement for Microsoft Defender.

DAX Administrator runs as LocalService with a service SID. Installation uses Windows UAC. Its bounded IPC permits status, enable, disable and revoke only. The client verifies the pipe server PID against SCM; the service validates the caller SID. It does not execute privileged commands.

DAX policy gates package management and Python launch. Script launch rechecks the service grant. Python inherits the real process token. Stop remains available even after a profile change. The tray protocol is a health channel, not an authentication boundary.

Script process observation polls once per second. Short-lived children and children that outlive a root may be missed. No claim of complete monitoring, malware detection, suspension or sandboxing is made.

Updates use HTTPS and GitHub asset SHA-256. Authenticode signing and independently signed manifests are not configured. Privileged service updates remain explicit installations through UAC. Text updates use the installed release tag. Local diagnostic audit is user-writable and rotated at approximately 4 MiB, retaining one previous file; it is not a tamper-resistant forensic log.

Report vulnerabilities privately to invzum@gmail.com. Do not post credentials or sensitive user data in public issues.
