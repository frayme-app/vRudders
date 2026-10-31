# VRudders 0.3.0 — Background forwarding and system tray

This stable release adds system tray operation and keeps yaw forwarding independent of the window. The release candidate's flight test was confirmed on October 31, 2026.

VRudders can now start and stay in the system tray. Forwarding runs independently of the window: a slow redraw, hide/restore operation, or UI delay no longer triggers the old window-timer pause check. This addresses a path consistent with reports of yaw stopping after Alt-Tab; the original reporter's PC has not been tested.

## What changed

- **App & tray** settings for starting without a window, minimizing to the tray, and closing to the tray. The X button hides by default; disable close-to-tray if you prefer it to quit.
- A **VRudders (tray)** Start menu shortcut and `--tray` command-line option.
- Tray controls for Show, green **Start forwarding** / red **Stop forwarding**, and **Exit VRudders**. Double-click reopens the window. Starting the app never starts forwarding automatically.
- Pedal reads, tuning, precision/trim keys, virtual writes, and Windows readback run on a dedicated thread. The window consumes snapshots instead of driving the forwarding loop.
- Disconnects, failed output/readback, and actual forwarding-thread pauses still stop and center yaw. A hidden app requests a Windows notification when output stops; Windows notification settings determine whether it appears.

The existing signed driver and virtual device identity are reused. No new driver protocol or binding identity is introduced. The original linear response, tuning, profiles, and calibration are retained; existing minimize-to-tray preferences are preserved.

## Install or upgrade

1. Finish your flight. **Exit VRudders** using its tray menu before running Setup. Closing the window may only hide the app.
2. Run **VRudders-0.3.0-Setup-x64.exe**. Windows 11 x64 is required. Setup includes the runtime and signed virtual driver.
3. Open VRudders and verify your pedals, axis, profile, and all three meters. In **App & tray**, choose the startup behavior you want.
4. Start forwarding, then test Alt-Tab to another app, minimize/restore, close/reopen, and tray Start/Stop during a flight. Use **Exit VRudders** to quit.

Setup replaces the installation and updates the same virtual device. Profiles and calibration stay in user settings. Restart games after upgrading, verify both yaw directions, and reassign bindings only if required by the game. A clean uninstall is not necessary.

Official signed releases use publisher **Ermis Catevatis**. Windows may show SmartScreen or a separate driver approval prompt. The README illustrates both prompts. Keep Windows security enabled.

## Verification

- The user confirmed a successful flight test of the background/tray release candidate on October 31, 2026. Stable promotion changes version metadata and documentation; the tested yaw and tray implementation is unchanged.
- Every 16-bit input value is checked in both directions through the forwarding session and the original response/packet checks.
- A regression test deliberately blocks the UI for 650 ms and verifies continued background writes and fresh Windows-readback samples from simulated controllers.
- Checks exercise focus changes, repeated minimize/hide/restore/close cycles, tray-only startup, tray Start/Stop/Exit, settings persistence, and existing settings migration.
- Disconnect, readback failure, real forwarding pause, neutral output, manual reconnect/resume, and reader/output thread ownership are checked with simulated I/O.
- Previous beta.4 slow-start protection is retained. The driver package is unchanged from the previous releases.

Broader controller, game, and Windows configurations remain unverified; the original reporter's PC has not been retested. App/tools/docs use MIT; the Microsoft-derived driver retains MS-PL. Source build instructions are in `docs/BUILDING.md`.
