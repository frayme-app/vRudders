# VRudders 0.2.0-beta.3 — Rudder Control

Early beta. Flight test and local upgrade verified with Turtle Beach VelocityOne Rudder on Windows 11 x64. Other hardware, games, and clean-machine configurations remain unverified.

## Installer correction

- Normal Setup now allows Windows to request approval for the signed driver publisher. Beta.2 always suppressed that dialog, causing `0xE0000242` when the new certificate had not yet been approved.
- Explicit `/S` installations still suppress prompts and fail with a readable instruction to rerun Setup normally if publisher approval is required. No certificate imports or Windows security changes.
- Same driver package and flight behavior as beta.2; this correction changes installer behavior and documentation.

## Visual, curve, and upgrade update

- Original rotor/yaw icon for the app, title bar, taskbar, tray, installer, and uninstaller.
- Original generated helicopter header artwork, with no game logos or titles.
- Large green START / red STOP button with adjacent forwarding state and instructions.
- Protected default renamed **VRudders · Original response**; custom names remain unchanged.
- Bidirectional curve: negative values amplify small pedal inputs; positive values preserve the existing soft response. At −100%, 25% travel gives about 58% yaw. Endpoints remain unchanged. Format 1 profiles migrate without changing their response; new settings/exports use format 2, with a backup of the original settings on first save.
- Setup updates the app and virtual device in place, preserving profiles and calibration.
- Restart games after upgrading. The device identity stays the same; verify both yaw directions and reassign bindings if needed.

## Missing VelocityOne Z input fixed

Some systems report no usable axes or positions for VelocityOne through WinMM, even while DirectInput exposes X/Y/Z. A nonexclusive DirectInput fallback now reads VelocityOne when WinMM lacks Z. The app no longer fabricates X/Y entries for a device reporting no axes.

## Flight controls

- Per-device center and unequal-travel calibration.
- Center/end dead zones, response curve, sensitivity/output strength.
- Protected working passthrough and game/aircraft profiles: duplicate, save, reset draft, import/export.
- Physical, processed, and Windows readback meters; live curve preview beside the editor.
- Optional time-based smoothing, hold-to-precision function key, and bounded rudder trim.
- Optional tray operation, remembered selections, input-loss stop, and matching-device rediscovery with manual resume.
- Explicit apply while stopped. All new tuning defaults to bypass; calibration stays with the device/axis.

## Branding and compatibility

App: **VRudders — Rudder Control**. Virtual controller: **VRudders Yaw**. Setup keeps the same device identity and Z protocol across upgrades. Stop forwarding or close the app to center virtual yaw; the driver also centers after a lost heartbeat.

## Validation and limits

Read-only self-tests cover every 16-bit Z value and packet in both directions, calibration, curve monotonicity/bounds, dead-zone continuity, strength, smoothing timing, precision transitions, trim reset, and profile validation. An isolated UI workflow test covers editing, saving, reset, protected bypass, and simulated disconnect/rediscovery. These do not replace physical unplug/replug and flight testing.

Windows 11 x64. Yaw forwarding has been flight-tested with VelocityOne; other WinMM controllers remain unverified. No toe-brake output, HOTAS button assignments, automatic game/profile switching, or automatic forwarding. Precision/trim shortcuts use opt-in F keys. Keep the app running during play.

MIT app/tools/docs; Microsoft-derived driver remains MS-PL. Public source/build instructions included. Setup/removal require administrator permission. New signed downloads can receive SmartScreen warnings; keep Windows security enabled. Local profiles/calibration remain after uninstall.
