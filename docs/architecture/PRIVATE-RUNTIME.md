# Private runtime migration

Source owner: Ketsuneban/tlw (private). Public contracts, Yetta interface, Marvine, warning presentation and IPC clients remain here.

Private components: DAX tray/service, access policy, process observation, audit, TTY custody and Tallow text update worker. Public clients must continue validating actual Windows permissions and service identity.

Normal users receive the compiled runtime inside Yetta.exe. No GitHub credential is included. Public builds download only the binary runtime release pinned in runtime.lock.json and verify SHA-256.

Migration order: validate the private source build; publish the binary-only runtime package; pin its checksum; validate this branch without private sources; merge only after those checks pass. A pending lock deliberately fails closed.

Existing public history and old MIT releases are retained. Migration closes future implementation development; it does not erase previously published code.

Publishing from tlw requires a fine-grained token or GitHub App token scoped to Contents:write on Ketsuneban/Yetta, stored only as the private Actions secret YETTA_PUBLISH_TOKEN. It must not be added to the public repository, installers, logs or client code.
