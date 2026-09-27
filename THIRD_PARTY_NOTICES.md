# Third-party source

`driver/VRudders.c` and `driver/VRudders.inf` adapt the HID/UMDF driver structure and report-marshalling conventions from Microsoft's Windows driver samples:

- Repository: https://github.com/microsoft/Windows-driver-samples
- Reference commit: `2dc3fd3a0cc84a2933f2194e7ec0871584979071`
- Sample: `hid/vhidmini2` (`driver/vhidmini.c`, `driver/umdf2/util.c`, `driver/umdf2/VhidminiUm.inx`)
- Copyright (c) Microsoft Corporation. All rights reserved.
- License: Microsoft Public License (MS-PL), reproduced in `driver/LICENSE-MS-PL.txt`.

VRudders modifications provide a fixed joystick descriptor, Z-only feed, bounded protocol and neutral-on-timeout behavior. Sample-derived driver source is distributed under MS-PL. No vJoy, HidHide, ViGEmBus, or Joystick Gremlin source or binaries are incorporated.

Build tools and Windows/.NET runtime components retain their respective Microsoft licenses. The downloaded development toolchain is not part of the application package.

## Bundled runtime and installer

Published installers include Microsoft .NET 10 and Windows Desktop runtime components from `dotnet publish --self-contained true`. Their license and third-party notices are copied from the runtime packages into the installed `licenses/` folder. See https://github.com/dotnet/runtime and https://github.com/dotnet/winforms.

The installer/uninstaller uses unmodified NSIS 3.12 (https://nsis.sourceforge.io/). Its notices are included as `NSIS-COPYING.txt` in the installed package. The portable compiler stays in ignored `.tools/`; generated installers include NSIS runtime components.

Our original app, setup helper, installer script, build scripts, and docs use MIT; see the root `LICENSE`. Microsoft-derived driver files remain MS-PL.
