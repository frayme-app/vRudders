# Security

VRudders is an early beta; it has not undergone a comprehensive security audit. Security fixes target the latest release and `main`.

## Report a vulnerability

Use GitHub's [private vulnerability reporting](https://github.com/frayme-app/vRudders/security/advisories/new). Include affected versions, reproduction steps, impact, and a minimal example when possible. Do not open a public issue containing an exploit, credentials, or personal information. If private reporting is unavailable, open an issue requesting a private contact without disclosing the vulnerability.

## Scope

Reports about input parsing, the user-mode driver, installer privileges, dependency integrity, or unsafe output behavior are welcome. The driver runs in user mode, but installation requires administrator access. Normal app use does not.

Keep Windows security protections enabled. Official releases should have a valid signature from Ermis Catevatis. Signing keys and credentials are never part of this repository; source builds require your own signing setup if you rebuild the driver for normal installation.
