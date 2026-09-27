param(
    [Parameter(Mandatory)][string[]]$Path,
    [string]$Metadata = (Join-Path (Split-Path $PSScriptRoot -Parent) 'signing.local.json'),
    [string]$CopyTo
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$signTool = "$root/.tools/microsoft.windows.sdk.cpp/c/bin/10.0.26100.0/x64/signtool.exe"
$dlib = "$root/.tools/microsoft.artifactsigning.client/bin/x64/Azure.CodeSigning.Dlib.dll"
if (!(Test-Path -LiteralPath $Metadata)) { throw 'Provide your signing metadata with -Metadata. See signing.example.json.' }
if ($CopyTo -and $Path.Count -ne 1) { throw '-CopyTo requires exactly one input file.' }
foreach ($file in $Path) {
    & $signTool sign /fd SHA256 /tr 'http://timestamp.acs.microsoft.com' /td SHA256 /dlib $dlib /dmdf $Metadata $file
    if ($LASTEXITCODE -ne 0) { throw "Signing failed: $file" }
    & $signTool verify /pa $file
    if ($LASTEXITCODE -ne 0) { throw "Signature verification failed: $file" }
    if ($CopyTo) { Copy-Item -LiteralPath $file -Destination $CopyTo }
}
