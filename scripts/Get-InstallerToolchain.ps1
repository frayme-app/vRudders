$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $root '.tools'
$archive = Join-Path $cache 'nsis-3.12.zip'
$expected = '56581f90db321581c5381193d796fffcf2d24b2f8fed2160a6c6a3baa67f2c4f'
New-Item -ItemType Directory -Force $cache | Out-Null
if (!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) {
    & curl.exe --fail --location --silent --show-error --max-time 120 --output $archive 'https://downloads.sourceforge.net/project/nsis/NSIS%203/3.12/nsis-3.12.zip'
    if ($LASTEXITCODE -ne 0) { throw 'NSIS download failed.' }
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'NSIS archive SHA-256 mismatch. No downloaded code was executed.' }
if (!(Test-Path "$cache/nsis-3.12/Bin/makensis.exe")) { Expand-Archive -LiteralPath $archive -DestinationPath $cache -Force }
Write-Host 'NSIS 3.12 ready in .tools (no system installation).'
