param(
    [string]$Metadata = (Join-Path (Split-Path $PSScriptRoot -Parent) 'signing.local.json'),
    [string]$DriverDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/driver'),
    [string]$AppDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/local-app'),
    [switch]$AppOnly
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$tools = Join-Path $root '.tools'
$dlib = "$tools/microsoft.artifactsigning.client/bin/x64/Azure.CodeSigning.Dlib.dll"
if (!(Test-Path $dlib)) { throw 'Run scripts/Get-Toolchain.ps1 first.' }
if (!(Test-Path -LiteralPath $Metadata)) { throw 'Copy signing.example.json to signing.local.json and supply your own signing configuration.' }
az account show --output none
if ($LASTEXITCODE -ne 0) { throw 'Sign into Azure with az login first.' }
# The catalogue must be generated AFTER signing the DLL, which changes its hash.
if (!$AppOnly) {
    $DriverDirectory = (Resolve-Path -LiteralPath $DriverDirectory).Path
    & "$PSScriptRoot/Sign-File.ps1" -Metadata $Metadata -Path "$DriverDirectory/VRudders.dll"
    & "$tools/microsoft.windows.wdk.x64/c/bin/10.0.26100.0/x86/Inf2Cat.exe" "/driver:$DriverDirectory" /os:10_X64 /uselocaltime
    if ($LASTEXITCODE -ne 0) { throw 'Catalog generation failed.' }
    & "$PSScriptRoot/Sign-File.ps1" -Metadata $Metadata -Path "$DriverDirectory/VRudders.cat"
}
foreach ($name in @('VRudders.dll', 'VRudders.exe')) {
    $path = Join-Path $AppDirectory $name
    if (Test-Path -LiteralPath $path) { & "$PSScriptRoot/Sign-File.ps1" -Metadata $Metadata -Path $path }
}
Write-Host 'Signed package ready. Kernel-policy signing is not claimed: this is a UMDF driver.'
