<#
.SYNOPSIS
  Downloads a prebuilt static GPL ffmpeg.exe into src/Aquashot/Resources/ for embedding.

.DESCRIPTION
  Pulls the BtbN win64 GPL static build (single self-contained ffmpeg.exe, no DLLs) and
  extracts only ffmpeg.exe. The binary is git-ignored; this script makes it reproducible.
  The build includes nvenc/qsv/amf/libx264 encoders and gdigrab/ddagrab capture devices.

.NOTES
  GPL build (includes libx264) — distribution must comply with GPL.
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root 'src/Aquashot/Resources'
$exe  = Join-Path $dest 'ffmpeg.exe'
$url  = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip'

New-Item -ItemType Directory -Force -Path $dest | Out-Null
$tmpZip = Join-Path ([System.IO.Path]::GetTempPath()) ("ffmpeg-" + [guid]::NewGuid().ToString('N') + '.zip')
$tmpDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ffmpeg-" + [guid]::NewGuid().ToString('N'))

Write-Host "Downloading $url ..."
Invoke-WebRequest -Uri $url -OutFile $tmpZip -UseBasicParsing

Write-Host "Extracting ffmpeg.exe ..."
Expand-Archive -Path $tmpZip -DestinationPath $tmpDir -Force
$found = Get-ChildItem -Path $tmpDir -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
if (-not $found) { throw "ffmpeg.exe not found in archive" }
Copy-Item -Path $found.FullName -Destination $exe -Force

Remove-Item $tmpZip -Force -ErrorAction SilentlyContinue
Remove-Item $tmpDir -Recurse -Force -ErrorAction SilentlyContinue

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Done: $exe ($sizeMb MB)"
& $exe -hide_banner -version | Select-Object -First 1
