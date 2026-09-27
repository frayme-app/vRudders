#Requires -Version 7.0
# Original vector artwork for VRudders. MIT license; no external graphics tools.
[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src/VRudders/Assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null

# These same coordinates are emitted to SVG below. A rotor and split yaw chevron
# are deliberately simple enough to remain recognizable at 16 pixels.
$rotor = '38,65 118,65 118,42 138,42 138,65 218,65 218,83 138,83 138,98 118,98 118,83 38,83'
$left = '34,107 96,124 128,185 128,226 87,178'
$right = '222,107 160,124 128,185 128,226 169,178'
$needle = '119,109 137,109 137,139 128,156 119,139'
function Fill-Polygon($Graphics, [string]$Points, [string]$Color) {
    [System.Drawing.PointF[]]$shape = @($Points.Split(' ') | ForEach-Object {
        $pair = $_.Split(','); [System.Drawing.PointF]::new([single]$pair[0], [single]$pair[1])
    })
    $brush = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml($Color))
    try { $Graphics.FillPolygon($brush, $shape) } finally { $brush.Dispose() }
}
$frames = [Collections.Generic.List[byte[]]]::new()
$sizes = @(16,20,24,32,40,48,64,128,256)
foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $stream = [IO.MemoryStream]::new()
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.ScaleTransform($size / 256.0, $size / 256.0)
        $path.AddArc(8,8,88,88,180,90); $path.AddArc(160,8,88,88,270,90)
        $path.AddArc(160,160,88,88,0,90); $path.AddArc(8,160,88,88,90,90); $path.CloseFigure()
        $background = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.Rectangle]::new(0,0,256,256),
            [System.Drawing.ColorTranslator]::FromHtml('#163B50'),
            [System.Drawing.ColorTranslator]::FromHtml('#071522'), 90.0)
        $edge = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#36788C'), 3.0)
        try { $graphics.FillPath($background, $path); $graphics.DrawPath($edge, $path) }
        finally { $background.Dispose(); $edge.Dispose() }
        Fill-Polygon $graphics $rotor '#35CCE3'
        Fill-Polygon $graphics $left '#EAF7FF'
        Fill-Polygon $graphics $right '#35CCE3'
        Fill-Polygon $graphics $needle '#FFAA55'
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $frames.Add($stream.ToArray())
        if ($size -eq 256) { $bitmap.Save((Join-Path $OutputDirectory 'vrudders-mark.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    } finally { $stream.Dispose(); $path.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}
$file = [IO.File]::Create((Join-Path $OutputDirectory 'vrudders.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
@"
<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <defs><linearGradient id="navy" x1="0" y1="0" x2="0" y2="1"><stop stop-color="#163B50"/><stop offset="1" stop-color="#071522"/></linearGradient></defs>
  <rect x="8" y="8" width="240" height="240" rx="44" fill="url(#navy)" stroke="#36788C" stroke-width="3"/>
  <polygon points="$rotor" fill="#35CCE3"/>
  <polygon points="$left" fill="#EAF7FF"/>
  <polygon points="$right" fill="#35CCE3"/>
  <polygon points="$needle" fill="#FFAA55"/>
</svg>
"@ | Set-Content (Join-Path $OutputDirectory 'vrudders-mark.svg') -Encoding utf8
Write-Host "Created original SVG, PNG, and multi-resolution Windows icon in $OutputDirectory"
