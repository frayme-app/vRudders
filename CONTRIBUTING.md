# Contributing

VRudders is an early beta. Bug reports, hardware compatibility reports, documentation fixes, and focused pull requests are welcome.

## Development

1. Fork and clone the repository. Create a branch from `main`.
2. Follow [BUILDING.md](docs/BUILDING.md) on Windows 11 x64.
3. Make a focused change and run the relevant self-tests. Include reproduction steps and validation in your pull request.

The Windows build workflow compiles the app, driver, and setup helper. It runs the mathematical and isolated UI checks without sending virtual input. Keep the physical-to-virtual Z path, device identity, neutral-on-stop behavior, and heartbeat timeout compatible. Changes to those behaviors need explicit tests and an explanation.

Never run `--driver-test` during a flight: it actively moves virtual yaw. No driver installation or signing is needed for ordinary app self-tests. Use separate output directories so builds do not overwrite a running installation.

## Reports

For bugs, include the app version, Windows version, controller model, selected axis, steps to reproduce, and which of the three dots move. Compatibility reports should distinguish Windows input, virtual readback, and an actual game test. Remove personal paths and device identifiers from shared logs.

Report vulnerabilities privately as described in [SECURITY.md](SECURITY.md).

## Source hygiene and licensing

Keep generated binaries, tool caches, private planning documents, personal profiles, credentials, and signing material out of commits. `signing.example.json` contains placeholders; local settings belong in ignored `signing.local.json`.

Contributions to the app, tools, docs, and original artwork use MIT. Contributions to the Microsoft-derived driver retain its MS-PL license and notices. Disclose any added third-party dependencies and their licenses.
