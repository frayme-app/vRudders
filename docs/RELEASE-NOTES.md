# VRudders 0.2.0-beta.4 — Start forwarding fix

This release fixes **Start immediately stopping with “Stopped after an app pause. Verify input and restart when ready.”** The physical and processed indicators could keep moving while virtual yaw stayed stopped.

On some PCs, connecting to controllers takes longer than the app expected. VRudders was counting that connection time as an interruption during flight and stopping itself. The pause check now begins after the connection finishes.

## What changed

- Slow controller connections no longer trigger the pause check immediately after Start.
- The app checks the pedals again after connecting, before allowing forwarding. If input disappeared during connection, the pending connection is closed.
- Actual pauses during forwarding still stop output and center virtual yaw. The message now includes the pause duration in milliseconds to help with troubleshooting.
- Added regression checks for slow startup, continued output, real pauses, manual restart, and input lost during connection.

The virtual driver, device identity, Z-axis conversion, tuning controls, and profile format remain the same. Calibration, dead zones, soft/aggressive response curves, smoothing, precision, trim, profiles, and live displays are included.

## Install or upgrade

1. Download **VRudders-0.2.0-beta.4-Setup-x64.exe** from this release.
2. Finish your flight and close VRudders, then run Setup. Windows 11 x64 is required; Setup includes the .NET runtime and signed virtual driver.
3. Launch VRudders, select your pedals and **Z**, then click **Start**. Check the physical, processed, and Windows readback indicators before flying.

Setup replaces the existing installation and updates the same virtual device without adding a second controller. Profiles and calibration stay in your user settings; a clean uninstall is not required. Restart games to reload their controller lists. Existing bindings should carry over; verify both directions and reassign the axis if needed.

Setup needs administrator permission. Official signed releases use publisher **Ermis Catevatis**. Windows may separately request approval for the driver publisher, or skip that prompt if approval is already remembered. New downloads can still receive SmartScreen reputation warnings. Keep Windows security enabled; see the README for installation troubleshooting.

## Verification and scope

- The slow-start failure was reproduced by an automated test before the fix; the corrected startup and pause/neutral checks pass.
- Tests cover every 16-bit yaw value and packet in both directions, tuning, profile storage, and the isolated UI workflow.
- Beta.4 was clean-installed on the development PC after removing the previous app, settings, virtual device, and both old driver packages. Windows reported successful driver installation with no device errors or reboot required. That PC retained its prior publisher approval.
- Beta.3 previously passed a flight test with Turtle Beach VelocityOne Rudder. Broader hardware, game, and previously unused Windows configurations remain unverified.

This is an early beta for Windows 11 on Intel/AMD 64-bit PCs. Keep VRudders running while playing. No toe-brake output, automatic forwarding, or automatic game/profile switching.

The release includes the signed installer, matching source ZIP, build manifest, and SHA-256 checksums. Public app/tools/docs use MIT; the Microsoft-derived driver retains MS-PL. Build instructions are in `docs/BUILDING.md`.
