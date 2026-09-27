param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $root '.tools'
$vc = (Get-ChildItem "$cache/msvc/VC/Tools/MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
if (!$vc) { throw 'Run Get-Toolchain.ps1 and Build-Driver.ps1 to prepare the native compiler first.' }
$sdk = "$cache/microsoft.windows.sdk.cpp/c"
$libs = "$cache/microsoft.windows.sdk.cpp.x64/c"
$ver = '10.0.26100.0'
$env:PATH = "$sdk/bin/$ver/x64;$vc/bin/Hostx64/x64;" + $env:PATH
$env:INCLUDE = @("$vc/include", "$sdk/Include/$ver/um", "$sdk/Include/$ver/shared", "$sdk/Include/$ver/ucrt") -join ';'
$env:LIB = @("$vc/lib/x64", "$libs/um/x64", "$libs/ucrt/x64") -join ';'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
& "$vc/bin/Hostx64/x64/cl.exe" /nologo /MT /W4 /WX /O2 /GS /guard:cf /D_WIN32_WINNT=0x0A00 "/Fo$OutputDirectory/DriverSetup.obj" "$root/installer/DriverSetup.c" /link "/OUT:$OutputDirectory/VRudders.DriverSetup.exe" /DYNAMICBASE /NXCOMPAT /GUARD:CF /MANIFEST:EMBED "/MANIFESTUAC:level='asInvoker' uiAccess='false'" setupapi.lib newdev.lib cfgmgr32.lib wintrust.lib advapi32.lib winmm.lib
if ($LASTEXITCODE -ne 0) { throw "Installer helper compile failed ($LASTEXITCODE)" }
& "$vc/bin/Hostx64/x64/cl.exe" /nologo /MT /W4 /WX /O2 /GS /guard:cf "/Fo$OutputDirectory/SignRunner.obj" "$root/installer/SignRunner.c" /link "/OUT:$OutputDirectory/VRudders.SignRunner.exe" /SUBSYSTEM:WINDOWS /DYNAMICBASE /NXCOMPAT /GUARD:CF /MANIFEST:EMBED "/MANIFESTUAC:level='asInvoker' uiAccess='false'"
if ($LASTEXITCODE -ne 0) { throw "Windowless signing launcher compile failed ($LASTEXITCODE)" }
