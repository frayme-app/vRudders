# Build VRudders yourself

## Prerequisites

Windows 11 x64, PowerShell 7 (`pwsh`), Git, and .NET SDK 10.0.302 (or a later patch accepted by `global.json`). First-time builds download tools/runtime packages. Azure CLI and access to your own Artifact Signing profile are needed only for signing through the supplied scripts.

Clone the repository or extract its release source ZIP. The release script requires a Git checkout to record a revision: from a source ZIP, initialize and commit a local repository first. App/driver compilation can run without Git.

## App

```powershell
dotnet publish src/VRudders -c Release -r win-x64 --self-contained true -o artifacts/local-app
Copy-Item docs/GETTING-STARTED.html artifacts/local-app/
.\artifacts\local-app\VRudders.exe --self-test artifacts/local-app/self-test.txt
.\artifacts\local-app\VRudders.exe --ui-self-test artifacts/local-app/ui-self-test.txt
.\artifacts\local-app\VRudders.exe
```

This includes its .NET runtime and is unsigned. It can monitor physical pedals immediately. Forwarding requires either an installed release driver or your own trusted driver build. To use only binaries you built yourself, compile and sign the driver too; compiling the GUI alone does not replace the installed driver.

## Artwork and icons

Project artwork is checked into `src/VRudders/Assets` and embedded in the app. Rebuild the original geometric icon (16–256 pixel frames) and SVG/PNG previews with `pwsh -NoProfile -File scripts/Build-Icon.ps1`; no additional graphics tools are required. The asset README records the built-in imagegen prompt for the helicopter illustration. Normal source builds use the checked-in PNG and require no image-generation service or API key.

## Driver and setup helper

```powershell
pwsh -NoProfile -File scripts/Get-Toolchain.ps1
pwsh -NoProfile -File scripts/Build-Driver.ps1
pwsh -NoProfile -File scripts/Build-InstallerHelper.ps1 -OutputDirectory artifacts/helper
```

The tools script downloads Microsoft compiler 14.44 and SDK/WDK packages 10.0.26100.6584 into ignored `.tools/`, checking compiler payloads against Microsoft's manifest. No global Visual Studio/WDK installation is required. The driver uses UMDF 2.33, targeting Windows 11 x64. The helper uses public SetupAPI/NewDev functions to manage only the VRudders root device under HIDClass.

Driver output is **unsigned** in `artifacts/driver`; compilation does not install it. Native code builds with warnings as errors, stack protection, ASLR, NX, and CFG.

## Signing

Copy `signing.example.json` to ignored `signing.local.json`, replacing the endpoint/account/profile placeholders with your own public-trust Artifact Signing configuration. Never put tokens, private keys, or passwords in these files.

```powershell
az login
pwsh -NoProfile -File scripts/Sign.ps1 -Metadata signing.local.json
```

This signs the DLL, regenerates its catalog, and signs the catalog. An app in `artifacts/local-app` is also signed if present. Override `-DriverDirectory` and `-AppDirectory` when using other output folders. Other signing services can work if Windows accepts their signatures for this UMDF package.

The distributed driver was accepted on the development PC without changing Windows security. This is not a claim of WHQL certification or kernel-driver signing. A freshly compiled unsigned driver is not ready for normal installation. Do not disable Windows security or import a root certificate for a public release.

## Complete installer

Commit the source first. Provide a directory with valid signed `VRudders.dll`, `VRudders.inf`, `vrudders.cat`, and `LICENSE-MS-PL.txt`:

```powershell
# Your trusted driver; app/helper/installer are unsigned.
pwsh -NoProfile -File scripts/Build-Release.ps1 -DriverDirectory artifacts/driver

# Sign the app, helper, installer, and embedded uninstaller too.
pwsh -NoProfile -File scripts/Build-Release.ps1 `
  -DriverDirectory artifacts/driver -Sign -SigningMetadata signing.local.json
```

To keep a separate driver build, choose an output directory. The release script requires its INF to match the source revision:

```powershell
pwsh -NoProfile -File scripts/Build-Driver.ps1 -OutputDirectory artifacts/driver-0.2.0
pwsh -NoProfile -File scripts/Sign-File.ps1 -Metadata signing.local.json -Path artifacts/driver-0.2.0/VRudders.dll
& ./.tools/microsoft.windows.wdk.x64/c/bin/10.0.26100.0/x86/Inf2Cat.exe /driver:artifacts/driver-0.2.0 /os:10_X64 /uselocaltime
pwsh -NoProfile -File scripts/Sign-File.ps1 -Metadata signing.local.json -Path artifacts/driver-0.2.0/vrudders.cat
pwsh -NoProfile -File scripts/Build-Release.ps1 `
  -DriverDirectory artifacts/driver-0.2.0 -Sign -SigningMetadata signing.local.json
```

Compilation and packaging do not install the driver or change an existing installation. Use the generated Setup to install or update the virtual controller; the setup helper checks the hardware identity and refuses duplicate devices.

Each build creates a fresh `artifacts/builds/<version>-<timestamp>/` containing `payload/` and `release/`. Release files include Setup, an exact-source ZIP for clean Git builds, `build-manifest.json`, release notes, and `SHA256SUMS.txt`. Nothing is published automatically.

NSIS 3.12 is downloaded as a portable ZIP and checked against a pinned SHA-256. No NSIS installation or DevCon redistribution is required. The release includes .NET runtime 10.0.10; maintainers should update this pin for future security servicing. `-AllowDirty` creates a local test build marked dirty, without a source ZIP. Do not publish that build.

Uninstaller signing runs through a small build-only Windows launcher with console creation disabled. It propagates the signing process's exit code and is not included in the installed app.

Normal Setup allows the Windows Security publisher-approval dialog for the signed driver. Signature validity and previously approved publisher trust are separate checks: a newly signed catalog can require another prompt. The native helper's `install` command allows that UI; `install-silent` suppresses it and fails if approval is needed. Setup `/S` uses the latter and records the failure in `driver-setup.log` without a blocking error dialog. If it reports `0xE0000242`, rerun Setup interactively. The helper never imports certificates or changes Windows security policy. See [Microsoft's installation flag documentation](https://learn.microsoft.com/en-us/windows/win32/api/newdev/nf-newdev-updatedriverforplugandplaydevicesw) and [publisher-prompt troubleshooting](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/troubleshooting-driver-signing-installation).

Signatures/timestamps and tool servicing mean bit-for-bit reproducibility is not promised. The manifest records the Git revision, tool/runtime versions, driver version, and payload hashes. Keep credentials, `.tools/`, and `artifacts/` out of Git.

## Verification

Read-only diagnostics:

```powershell
.\artifacts\helper\VRudders.DriverSetup.exe status
.\artifacts\local-app\VRudders.exe --list artifacts/devices.json
```

The following test **actively moves virtual yaw**. Close every VRudders copy and leave the game first:

```powershell
.\artifacts\local-app\VRudders.exe --driver-test artifacts/driver-test.txt
Get-Content artifacts/driver-test.txt
```

The app blocks competing forwarding/driver-test processes. Read-only listing, screenshots, and isolated UI workflow checks are exempt. The UI test uses a temporary settings directory and never starts forwarding.

Before publishing: inspect signatures/hashes; test clean install on another Windows 11 x64 PC; verify the dots and an in-game flight; test disconnect, app exit, uninstall, and reinstall. Windows may need online certificate-chain validation even though the runtime is bundled. A source ZIP contains only the committed source, never Azure credentials or the local development toolchain.

The GitHub Actions workflow compiles the app, unsigned driver, and setup helper, then runs the app's mathematical and isolated UI checks. It does not sign, install a driver, or publish releases. Physical hardware and flight checks remain manual.
