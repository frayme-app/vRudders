$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $root '.tools'
New-Item -ItemType Directory -Force $cache | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Expand-Package($archive, $destination) {
    if (!(Test-Path $destination)) {
        New-Item -ItemType Directory -Force $destination | Out-Null
        [System.IO.Compression.ZipFile]::ExtractToDirectory($archive, $destination)
    }
}
if (!(Test-Path "$cache/compiler-payloads.json")) {
    $channel = Invoke-RestMethod 'https://aka.ms/vs/17/release/channel'
    $manifest = Invoke-RestMethod (($channel.channelItems | Where-Object id -eq 'Microsoft.VisualStudio.Manifests.VisualStudio').payloads[0].url)
    $ids = @('Microsoft.VC.14.44.17.14.Tools.HostX64.TargetX64.base','Microsoft.VC.14.44.17.14.Tools.HostX64.TargetX64.Res.base','Microsoft.VC.14.44.17.14.CRT.Headers.base','Microsoft.VC.14.44.17.14.CRT.x64.Desktop.base','Microsoft.VC.14.44.17.14.CRT.x64.Store.base')
    $payloads = @($manifest.packages | Where-Object { $_.id -in $ids -and (!$_.language -or $_.language -eq 'en-US') } | ForEach-Object { $_.payloads } | Where-Object { $_.fileName -notmatch '\.(csy|deu|esn|fra|ita|jpn|kor|plk|ptb|rus|trk|chs|cht)\.' })
    $payloads | ConvertTo-Json -Depth 5 | Set-Content "$cache/compiler-payloads.json"
}
foreach ($payload in (Get-Content "$cache/compiler-payloads.json" -Raw | ConvertFrom-Json)) {
    $archive = Join-Path $cache $payload.fileName
    if (!(Test-Path $archive)) { Write-Host "Downloading $($payload.fileName)"; Invoke-WebRequest $payload.url -OutFile $archive }
    if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne $payload.sha256) { throw "Hash mismatch: $archive" }
    Expand-Package $archive "$cache/msvc-$($payload.fileName)"
}
$packages = @{
    'microsoft.windows.wdk.x64' = '10.0.26100.6584'
    'microsoft.windows.sdk.cpp' = '10.0.26100.6584'
    'microsoft.windows.sdk.cpp.x64' = '10.0.26100.6584'
    'microsoft.artifactsigning.client' = '1.0.128'
}
foreach ($id in $packages.Keys) {
    $version = $packages[$id]
    $archive = "$cache/$id.$version.nupkg"
    if (!(Test-Path $archive)) { Write-Host "Downloading $id $version"; Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg" -OutFile $archive }
    Expand-Package $archive "$cache/$id"
}
if (!(Test-Path "$cache/msvc/VC/Tools/MSVC")) {
    New-Item -ItemType Directory -Force "$cache/msvc" | Out-Null
    foreach ($folder in (Get-ChildItem "$cache/msvc-*.vsix" -Directory)) {
        Copy-Item "$($folder.FullName)/Contents/*" "$cache/msvc" -Recurse -Force
    }
}
Write-Host 'Local toolchain ready.'
