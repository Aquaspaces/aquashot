<#
.SYNOPSIS
  Builds a multi-resolution Windows .ico from a square PNG.

.DESCRIPTION
  Resizes the source PNG to several icon sizes (high-quality), PNG-encodes each,
  and packs them into a single .ico container (PNG-compressed entries, which
  Windows Vista+ supports — required for the 256px size). Used to regenerate
  src/Aquashot/Resources/aquashot.ico from the source art.

.EXAMPLE
  pwsh scripts/make-icon.ps1 -Source C:\Users\Aqua\Documents\aquashot.png
#>
param(
    [string]$Source = (Join-Path (Split-Path -Parent $PSScriptRoot) 'art/aquashot.png'),
    [string]$Out    = (Join-Path (Split-Path -Parent $PSScriptRoot) 'src/Aquashot/Resources/aquashot.ico')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $Source)) { throw "Source not found: $Source" }
$sizes = 16, 24, 32, 48, 64, 128, 256

$src = [System.Drawing.Image]::FromFile($Source)
try {
    $pngs = foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.DrawImage($src, 0, 0, $s, $s)
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        ,$ms.ToArray()
    }
} finally { $src.Dispose() }

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Out) | Out-Null
$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
try {
    # ICONDIR
    $bw.Write([uint16]0)              # reserved
    $bw.Write([uint16]1)              # type = icon
    $bw.Write([uint16]$sizes.Count)   # image count

    $offset = 6 + (16 * $sizes.Count) # header + directory entries
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $data = $pngs[$i]
        $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # width  (0 = 256)
        $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # height (0 = 256)
        $bw.Write([byte]0)            # palette count
        $bw.Write([byte]0)            # reserved
        $bw.Write([uint16]1)          # color planes
        $bw.Write([uint16]32)         # bits per pixel
        $bw.Write([uint32]$data.Length)
        $bw.Write([uint32]$offset)
        $offset += $data.Length
    }
    foreach ($data in $pngs) { $bw.Write($data) }
} finally { $bw.Dispose(); $fs.Dispose() }

Write-Host "Wrote $Out ($([math]::Round((Get-Item $Out).Length/1KB,1)) KB, sizes: $($sizes -join ','))"
