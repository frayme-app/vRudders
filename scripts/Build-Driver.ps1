param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $root '.tools'
$out = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'artifacts/driver' }
New-Item -ItemType Directory -Force $out, "$cache/msvc" | Out-Null
if (!(Test-Path "$cache/msvc/VC/Tools/MSVC")) {
    foreach ($folder in (Get-ChildItem "$cache/msvc-*.vsix" -Directory)) {
        Copy-Item "$($folder.FullName)/Contents/*" "$cache/msvc" -Recurse -Force
    }
}
$vc = (Get-ChildItem "$cache/msvc/VC/Tools/MSVC" -Directory | Sort-Object Name -Descending | Select-Object -First 1).FullName
$sdk = "$cache/microsoft.windows.sdk.cpp/c"
$sdklib = "$cache/microsoft.windows.sdk.cpp.x64/c"
$wdk = "$cache/microsoft.windows.wdk.x64/c"
$ver = '10.0.26100.0'
$env:PATH = "$vc/bin/Hostx64/x64;" + $env:PATH
$env:INCLUDE = @("$vc/include", "$sdk/Include/$ver/um", "$sdk/Include/$ver/shared", "$sdk/Include/$ver/ucrt", "$wdk/Include/$ver/um", "$wdk/Include/$ver/shared", "$wdk/Include/$ver/km", "$wdk/Include/wdf/umdf/2.33") -join ';'
$env:LIB = @("$vc/lib/x64", "$sdklib/um/x64", "$sdklib/ucrt/x64", "$wdk/Lib/wdf/umdf/x64/2.33") -join ';'
& "$vc/bin/Hostx64/x64/cl.exe" /nologo /LD /MT /W4 /WX /O2 /GS /guard:cf /DUNICODE /D_UNICODE /DWIN32_LEAN_AND_MEAN /D_WIN32_WINNT=0x0A00 /DWINVER=0x0A00 /DUMDF_VERSION_MAJOR=2 /DUMDF_VERSION_MINOR=33 /DWDF_USER_MODE /DUMDF_USING_NTSTATUS "/Fo$out/VRudders.obj" "$root/driver/VRudders.c" /link "/OUT:$out/VRudders.dll" "/IMPLIB:$out/VRudders.lib" /DYNAMICBASE /NXCOMPAT /GUARD:CF WdfDriverStubUm.lib ntdll.lib mincore.lib
if ($LASTEXITCODE -ne 0) { throw "Driver compile failed ($LASTEXITCODE)" }
Copy-Item "$root/driver/VRudders.inf" $out
Copy-Item "$root/driver/LICENSE-MS-PL.txt" $out
& "$wdk/bin/$ver/x86/Inf2Cat.exe" "/driver:$out" /os:10_X64 /uselocaltime
if ($LASTEXITCODE -ne 0) { throw "Catalog validation failed ($LASTEXITCODE)" }
Write-Host "Unsigned driver built: $out"
