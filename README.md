# vRudders

[![Build](https://github.com/frayme-app/vRudders/actions/workflows/build.yml/badge.svg)](https://github.com/frayme-app/vRudders/actions/workflows/build.yml)
[![App license: MIT](https://img.shields.io/badge/app_license-MIT-blue.svg)](LICENSE)

![Original VRudders helicopter artwork](src/VRudders/Assets/helicopter-header.png)

**Your pedals. Your response.**

VRudders reads your pedals in Windows and forwards movement to a virtual joystick you can bind to yaw. Select a controller, select Z, check the moving dots, and fly.

**Early beta · Windows 11 x64 · yaw only.** Yaw forwarding has been flight-tested with Turtle Beach VelocityOne Rudder, and the 0.2.0-beta.3 installer has passed a local upgrade test. Broader flight and clean-machine testing remain in progress. Other controllers that expose WinMM axes can be selected, but other hardware and games are unverified. The DirectInput fallback currently targets VelocityOne Rudder.

## Install

1. Download the installer from [GitHub Releases](https://github.com/frayme-app/vRudders/releases) when available. Checksums are in `SHA256SUMS.txt`. If no release is listed, follow [Build from source](#build-from-source).
2. Finish your flight and close **all** VRudders windows.
3. Run Setup. It installs the app, bundled .NET runtime, and signed virtual controller driver. Administrator permission is required for setup/removal; normal app use does not require it.
4. Restart only if requested. Launch **VRudders** from the Start menu.

Requires Windows 11 on an Intel/AMD 64-bit PC, pedals in PC mode, and permission to install a device. Windows 10 and ARM64 are not supported. No Azure account, SDK, vJoy, HidHide, or separate .NET installation is needed to use the release.

**Upgrading:** run the new Setup after closing the app and finishing your flight. It replaces the existing installation and updates the same virtual device, without installing a second controller. Profiles and calibration stay in your user settings. Setup changes the exact legacy Windows label **VRudders POC** to **VRudders Yaw**, preserving custom labels. Restart games to reload their controller lists. Existing bindings should carry over because the identity stays the same; verify both directions, since a game that stores names in bindings may require reassignment.

### Windows prompts

- **User Account Control:** official signed releases show publisher **Ermis Catevatis**, the developer's signing identity. Verify the source and publisher before approving.
- **Windows Security — device software:** Windows may separately ask to install the controller driver from **Ermis Catevatis**, including on an upgrade with a new signing certificate. Review the publisher and choose **Install** to continue. Setup leaves this decision to you. If setup reports **0xE0000242**, run the latest Setup normally, without `/S`, so Windows can show that prompt. Beta.2 incorrectly suppressed it.
- **SmartScreen:** new signed downloads may still show **Windows protected your PC** or an unrecognized-app warning. If you verified the official download and publisher, **More info** may offer **Run anyway**. Managed PCs may prohibit this. [Microsoft explains reputation warnings here.](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)
- **Invalid signature, different/unknown publisher on an official release, driver trust failure, or antivirus detection:** stop and report the exact message. Do not disable antivirus, Secure Boot, memory integrity, or driver-signature enforcement, and do not import a root certificate. No such changes were needed on the tested PC.

Optional PowerShell verification (official releases should report `Valid`):

```powershell
Get-AuthenticodeSignature .\VRudders-0.2.0-beta.3-Setup-x64.exe |
    Format-List Status, SignerCertificate
Get-FileHash .\VRudders-0.2.0-beta.3-Setup-x64.exe -Algorithm SHA256
```

## Get flying

1. Select **VelocityOne Rudder** and input axis **Z**.
2. Select **VRudders · Original response**. Move the pedals: the first dot is raw physical input; the second is processed yaw.
3. Click **Start**. The third dot shows Windows' actual virtual Z readback.
4. Open the **flight or HOTAS controls** in the game and bind yaw left/right to **VRudders Yaw**. The app also accepts the older driver named **VRudders POC**. The new driver's visible name changes; its hardware ID and Z protocol stay the same. Verify game bindings after upgrading.
5. Test left, right, and neutral in a flight. If reversed, stop, open **Tuning & profiles**, **Duplicate** the protected profile, enable **Reverse yaw direction**, and **Apply & save**. Bypass can stay enabled for reversal alone.
6. **Keep VRudders running while flying.** Minimizing is fine. Closing or stopping centers virtual yaw.

The large green **START** button sends pedal movement to virtual yaw. While active it turns red and reads **STOP**. The adjacent text shows whether output is live or why Start is unavailable. Stop returns virtual yaw to center.

The three displays verify each Windows stage; the flight verifies the game's response. **Setup guide** opens offline instructions. Forwarding never starts automatically.

## Tune your pedals

Stop forwarding before editing. In **Tuning & profiles**, duplicate the protected profile or create a new one, give it a name, and uncheck **Bypass tuning**. Edits preview immediately on the graph. **Apply & save** makes them active; unsaved edits block forwarding.

| Control | Behavior |
| --- | --- |
| Center/travel calibration | In **Device calibration**, release and capture center, then hold and capture each stop. Save, then enable calibration in the profile. Each side's travel is mapped separately. Windows/firmware calibration is untouched. |
| Center dead zone | Removes resting drift, continuously rescaling the remaining travel. 0–20%. |
| End dead zones | Reach full output before the physical stops. 0–30% per side; linked by default. |
| Response curve | −100 to 0 gives more yaw for less travel; 0 is linear; 0 to +100 softens the center. At −100, 25% travel produces about 58% yaw. Both directions retain endpoints. |
| Sensitivity / yaw strength | Limits maximum output to 10–100%. Keep 100% for full rudder authority. |
| Optional smoothing | Exponential filter, 0–200 ms; 0 off. This time constant describes added response delay, not measured game latency. |
| Precision key | Opt-in hold key, F8 by default, blends to a softer curve over 300 ms. Choose a key unbound in the game. |
| Rudder trim | Opt-in F9/F10 steps left/right by 1%, F11 resets; buttons are also available. Limited to ±20%, eases at 10% per second, resets on Stop/profile change. |
| Profiles | Name, duplicate, save, reset unsaved edits, import/export game or aircraft tuning. Calibration belongs to the device/axis and is not exported with profiles. |
| Tray/reconnect | Optional minimize-to-tray keeps forwarding active. Tray menu offers Show, Stop, Exit. Disconnect stops and centers yaw. Matching devices are rediscovered; resume manually. Closing the window exits. |

The graph line shows static response; the orange dot includes smoothing, precision, and trim. The third meter independently reads Windows' virtual axis while forwarding. Use linear game settings when evaluating an app curve, where available, to avoid stacking curves.

The protected **VRudders · Original response** profile bypasses every new tuning stage. New profiles start in bypass. Settings live in `%LOCALAPPDATA%\VRudders\settings.json`, validated and saved atomically. Precision/trim currently use function keys; no HOTAS button assignments or automatic profile switching.

For an aggressive profile, disable bypass, set Response curve below zero, and keep yaw strength at 100%. End dead zones can additionally shorten the travel needed for full yaw. The graph previews the result before applying. Version 0.2.0-beta.2 writes profile format 2; format 1 imports keep their existing soft response. The first settings migration saves an exact `settings-v1-backup-*.json` alongside the new file. Custom profile names are never renamed.

## Troubleshooting

| Symptom | Next step |
| --- | --- |
| No first-dot movement | Check USB/PC mode, Refresh, controller selection, and Z. Test the physical device in Windows Game Controllers. |
| VelocityOne lists only X/Y in the previous beta | Update to 0.2.0. Some systems report zero axes/positions through WinMM. The DirectInput fallback obtains the real X/Y/Z; it never fabricates a Z entry. |
| Input/processed dots move, virtual dot does not | Start. If unavailable, close VRudders and rerun Setup. Check Device Manager for VRudders Yaw (or the older VRudders POC). Save the exact error. |
| All dots move; game does not bind | Start before launching the game, check HOTAS and conflicting bindings. Separately test Steam Input disabled for that game if needed. |
| Pedals were unplugged | Wait for rediscovery, verify movement, and Start. If the identity changed, for example after moving USB ports, Refresh and select manually. |
| Start is disabled after editing | Apply & save, or Reset draft. Duplicate the protected profile to edit it. |
| Saved settings are invalid | The original file is kept and the app falls back to its protected preset. Back up and repair/move the file to enable saving again. |
| Another copy is running | Use or close that window, including the original POC or another Windows session. Do not open the old POC after starting the beta. |
| Driver setup fails | Keep Windows security enabled. Inspect `C:\Program Files\VRudders\driver-setup.log` and relevant entries in `C:\Windows\INF\setupapi.dev.log`. Rerun Setup or remove the partially installed app through Settings. |

Report the app/Windows/game versions, pedal model, which dots move, and exact error in a [GitHub issue](https://github.com/frayme-app/vRudders/issues). Review logs before sharing local paths/device identifiers. VRudders does not upload diagnostics or collect telemetry. See [SECURITY.md](SECURITY.md) for private vulnerability reports.

## Uninstall

Close VRudders, then use **Windows Settings → Apps → Installed apps → VRudders → Uninstall**. This removes the app and virtual device, leaving your physical pedals installed. The signed package remains inactive in Windows' Driver Store for reinstall. Game bindings and local profiles/calibration remain. Remove %LOCALAPPDATA%\VRudders yourself if you also want to clear your settings.

## Build from source

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) selected by `global.json`, then run in the source directory:

```powershell
dotnet publish src/VRudders -c Release -r win-x64 --self-contained true -o artifacts/local-app
Copy-Item docs/GETTING-STARTED.html artifacts/local-app/
.\artifacts\local-app\VRudders.exe
```

Physical input monitoring works without a virtual driver. Forwarding needs a trusted installed VRudders driver. Rebuilding only the app still relies on that driver binary; building a driver yourself requires your own suitable signing setup for normal installation.

See [BUILDING.md](docs/BUILDING.md) for the driver, signing, and `scripts/Build-Release.ps1` installer workflow. App/tools/docs use [MIT](LICENSE); the Microsoft-derived driver retains [MS-PL](driver/LICENSE-MS-PL.txt). See [third-party notices](THIRD_PARTY_NOTICES.md).

## Scope

One virtual yaw axis. No toe-brake output, startup service, updater, telemetry upload, or game injection. The custom driver runs in user mode and centers yaw after approximately 300 ms without a heartbeat. It retains development identifiers from the sample; broader coexistence testing remains necessary. Ambiguous device matches require manual selection.

This independent community project is not affiliated with a hardware or game manufacturer and has not undergone a comprehensive security audit.

## Contributing and license

See [CONTRIBUTING.md](CONTRIBUTING.md) for the development workflow. The original app, installer, scripts, documentation, and artwork use the [MIT license](LICENSE). Microsoft-derived driver files use [MS-PL](driver/LICENSE-MS-PL.txt); retain their notices when redistributing. Runtime and installer notices are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
