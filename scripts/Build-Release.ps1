#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$')][string]$Version = '0.2.0-beta.3',
    [string]$DriverDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/driver'),
    [switch]$Sign,
    [string]$SigningMetadata = (Join-Path (Split-Path $PSScriptRoot -Parent) 'signing.local.json'),
    [switch]$AllowDirty
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $dirty = @(git status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw 'Build from a Git checkout so the source revision can be recorded.' }
    if ($dirty.Count -gt 0 -and !$AllowDirty) { throw 'Commit changes first, or use -AllowDirty for a local test build (no source ZIP).' }
    $commit = (git rev-parse HEAD).Trim()
    if ($Sign) {
        if (!(Test-Path -LiteralPath $SigningMetadata)) { throw 'Signing metadata missing. Supply -SigningMetadata; credentials do not belong in that file.' }
        $SigningMetadata = (Resolve-Path -LiteralPath $SigningMetadata).Path
        & az account show --output none
        if ($LASTEXITCODE -ne 0) { throw 'Sign in to your signing account with az login first.' }
    }
    & "$PSScriptRoot/Get-Toolchain.ps1"
    & "$PSScriptRoot/Get-InstallerToolchain.ps1"
    $signTool = "$root/.tools/microsoft.windows.sdk.cpp/c/bin/10.0.26100.0/x64/signtool.exe"
    $DriverDirectory = (Resolve-Path -LiteralPath $DriverDirectory).Path
    foreach ($name in @('VRudders.dll','VRudders.inf','vrudders.cat','LICENSE-MS-PL.txt')) {
        if (!(Test-Path -LiteralPath (Join-Path $DriverDirectory $name))) { throw "Driver package missing $name" }
    }
    if ((Get-FileHash "$DriverDirectory/VRudders.inf").Hash -ne (Get-FileHash "$root/driver/VRudders.inf").Hash) {
        throw 'The supplied driver INF does not match this source revision. Rebuild and sign the driver so the package includes the current name/version.'
    }
    # Package only a trusted driver. No test-signing or Windows security changes.
    foreach ($member in @('VRudders.inf','VRudders.dll')) {
        & $signTool verify /pa /c "$DriverDirectory/vrudders.cat" "$DriverDirectory/$member"
        if ($LASTEXITCODE -ne 0) { throw "Driver catalog/member verification failed: $member" }
    }
    $driverVersion = [regex]::Match((Get-Content "$DriverDirectory/VRudders.inf" -Raw), '(?m)^DriverVer=(.+)$').Groups[1].Value.Trim()
    if (!$driverVersion) { throw 'Driver INF has no DriverVer.' }
    # Unique build folders preserve prior release candidates.
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $build = Join-Path $root "artifacts/builds/$Version-$stamp"
    $payload = Join-Path $build 'payload'
    $out = Join-Path $build 'release'
    New-Item -ItemType Directory -Force $payload,$out | Out-Null
    $numericVersion = ($Version -split '-')[0] + '.0'
    & dotnet publish "$root/src/VRudders" -c Release -r win-x64 --self-contained true -o $payload "-p:Version=$Version" "-p:AssemblyVersion=$numericVersion" "-p:FileVersion=$numericVersion" -p:RuntimeFrameworkVersion=10.0.10 -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }
    & "$PSScriptRoot/Build-InstallerHelper.ps1" -OutputDirectory "$build/native"
    Copy-Item "$build/native/VRudders.DriverSetup.exe" $payload
    New-Item -ItemType Directory -Force "$payload/driver" | Out-Null
    foreach ($name in @('VRudders.dll','VRudders.inf','vrudders.cat','LICENSE-MS-PL.txt')) { Copy-Item (Join-Path $DriverDirectory $name) "$payload/driver" }
    & "$payload/VRudders.DriverSetup.exe" verify
    if ($LASTEXITCODE -ne 0) { throw 'The setup helper could not verify the trusted driver package.' }
    foreach ($name in @('LICENSE','README.md','THIRD_PARTY_NOTICES.md')) { Copy-Item (Join-Path $root $name) $payload }
    Copy-Item "$root/docs/GETTING-STARTED.html" $payload
    Copy-Item "$root/docs" "$payload/docs" -Recurse
    Copy-Item "$root/.tools/nsis-3.12/COPYING" "$payload/NSIS-COPYING.txt"
    $nugetRoot = (& dotnet nuget locals global-packages --list) -replace '^[^:]+:\s*',''
    New-Item -ItemType Directory -Force "$payload/licenses" | Out-Null
    Copy-Item "$nugetRoot/microsoft.netcore.app.runtime.win-x64/10.0.10/LICENSE.TXT" "$payload/licenses/dotnet-runtime-LICENSE.txt"
    Copy-Item "$nugetRoot/microsoft.netcore.app.runtime.win-x64/10.0.10/THIRD-PARTY-NOTICES.TXT" "$payload/licenses/dotnet-runtime-THIRD-PARTY-NOTICES.txt"
    Copy-Item "$nugetRoot/microsoft.windowsdesktop.app.runtime.win-x64/10.0.10/LICENSE" "$payload/licenses/dotnet-windowsdesktop-LICENSE.txt"

    $selfTest = Start-Process -FilePath "$payload/VRudders.exe" -ArgumentList @('--self-test',('"' + "$build/self-test.txt" + '"')) -WindowStyle Hidden -PassThru -Wait
    if ($selfTest.ExitCode -ne 0 -or !(Test-Path "$build/self-test.txt")) { throw 'App self-test failed.' }
    $uiTest = Start-Process -FilePath "$payload/VRudders.exe" -ArgumentList @('--ui-self-test',('"' + "$build/ui-self-test.txt" + '"')) -WindowStyle Hidden -PassThru -Wait
    if ($uiTest.ExitCode -ne 0 -or !(Test-Path "$build/ui-self-test.txt")) { throw 'Isolated UI workflow test failed.' }
    if ($Sign) { & "$PSScriptRoot/Sign-File.ps1" -Metadata $SigningMetadata -Path @("$payload/VRudders.exe","$payload/VRudders.dll","$payload/VRudders.DriverSetup.exe") }

    $files = @(Get-ChildItem -LiteralPath $payload -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($payload,$_.FullName).Replace('\','/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    [ordered]@{ version=$Version; sourceCommit=$commit; sourceDirty=($dirty.Count -gt 0); sdk=(& dotnet --version); runtime='10.0.10'; installerCompiler='NSIS 3.12'; publisherSigned=[bool]$Sign; driverVersion=$driverVersion; files=$files } | ConvertTo-Json -Depth 6 | Set-Content "$payload/build-manifest.json" -Encoding utf8
    Copy-Item "$payload/build-manifest.json" $out

    $uninstall = [Collections.Generic.List[string]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $payload -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($payload,$file.FullName).Replace('$','$$')
        $uninstall.Add('Delete "$INSTDIR\' + $relative + '"')
    }
    foreach ($dir in Get-ChildItem -LiteralPath $payload -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending) {
        $relative = [IO.Path]::GetRelativePath($payload,$dir.FullName).Replace('$','$$')
        $uninstall.Add('RMDir "$INSTDIR\' + $relative + '"')
    }
    $uninstall | Set-Content "$build/UninstallFiles.nsh" -Encoding utf8
    $sizeKB = [int][Math]::Ceiling((($files | Measure-Object bytes -Sum).Sum) / 1024)
    $uninstallInclude = Join-Path $build 'UninstallFiles.nsh'
    $compilerArgs = @('/V2','/INPUTCHARSET','UTF8',"/DROOT=$root","/DPAYLOAD=$payload","/DOUTPUT=$out","/DVERSION=$Version","/DNUMERIC_VERSION=$numericVersion","/DSIZE_KB=$sizeKB","/DUNINSTALL_FILES=$uninstallInclude")
    if ($Sign) { $compilerArgs += @("/DSIGN_SCRIPT=$PSScriptRoot/Sign-File.ps1","/DSIGN_METADATA=$SigningMetadata","/DPWSH=$PSHOME/pwsh.exe","/DUNINSTALL_ARCHIVE=$build/native/Uninstall.signed.exe") }
    $compilerArgs += "$root/installer/VRudders.nsi"
    & "$root/.tools/nsis-3.12/Bin/makensis.exe" @compilerArgs
    if ($LASTEXITCODE -ne 0) { throw 'Installer compile failed.' }
    $installer = "$out/VRudders-$Version-Setup-x64.exe"
    if ($Sign) { & "$PSScriptRoot/Sign-File.ps1" -Metadata $SigningMetadata -Path $installer }
    if ($dirty.Count -eq 0) {
        & git archive --format=zip "--prefix=VRudders-$Version/" "--output=$out/VRudders-$Version-source.zip" HEAD
        if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
    }
    Copy-Item "$root/docs/RELEASE-NOTES.md" $out
    Get-ChildItem -LiteralPath $out -File | Sort-Object Name | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name } | Set-Content "$out/SHA256SUMS.txt" -Encoding ascii
    Write-Host "Release built: $out"
    Write-Host "Verified driver package: $driverVersion"
    Write-Host 'This build has not been installed or flight-tested automatically.'
} finally { Pop-Location }
