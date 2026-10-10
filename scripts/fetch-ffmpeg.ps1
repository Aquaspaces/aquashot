<#
.SYNOPSIS
  Downloads a pinned, checksum-verified static GPL ffmpeg.exe into src/Aquashot/Resources/ for embedding.

.DESCRIPTION
  Pulls a versioned gyan.dev "essentials" build from GyanD/codexffmpeg (a single static
  ffmpeg.exe, no DLLs), verifies the archive's SHA-256, and extracts only ffmpeg.exe. Versioned
  releases there are permanent, so every build of a given Aquashot commit embeds the same binary.
  The build includes libx264, nvenc/qsv/amf encoders and the gdigrab/dshow capture devices.

  To upgrade: pick a release at https://github.com/GyanD/codexffmpeg/releases, update $version
  and $sha256 (GitHub lists each asset's digest), then check recording still works.

.NOTES
  GPL build (includes libx264). Distribution must comply with the GPL; see THIRD-PARTY-NOTICES.md.
  Corresponding source: https://ffmpeg.org/releases/ffmpeg-<version>.tar.xz, build scripts at
  https://www.gyan.dev/ffmpeg/builds/.
#>
$ErrorActionPreference = 'Stop'

$version = '9.0.2'
$sha256  = '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
$url     = "https://github.com/GyanD/codexffmpeg/releases/download/$version/ffmpeg-$version-essentials_build.zip"

$root = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $root 'src/Aquashot/Resources'
$exe  = Join-Path $dest 'ffmpeg.exe'

New-Item -ItemType Directory -Force -Path $dest | Out-Null
$tmpZip = Join-Path ([System.IO.Path]::GetTempPath()) ("ffmpeg-" + [guid]::NewGuid().ToString('N') + '.zip')
$tmpDir = Join-Path ([System.IO.Path]::GetTempPath()) ("ffmpeg-" + [guid]::NewGuid().ToString('N'))

try {
    Write-Host "Downloading FFmpeg $version ..."
    Invoke-WebRequest -Uri $url -OutFile $tmpZip -UseBasicParsing

    $actual = (Get-FileHash -Path $tmpZip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $sha256) {
        throw "FFmpeg archive checksum mismatch.`n  expected $sha256`n  actual   $actual"
    }

    Write-Host "Checksum verified. Extracting ffmpeg.exe ..."
    Expand-Archive -Path $tmpZip -DestinationPath $tmpDir -Force
    $found = Get-ChildItem -Path $tmpDir -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
    if (-not $found) { throw "ffmpeg.exe not found in archive" }
    Copy-Item -Path $found.FullName -Destination $exe -Force
}
finally {
    Remove-Item $tmpZip -Force -ErrorAction SilentlyContinue
    Remove-Item $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
}

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Done: $exe ($sizeMb MB)"
& $exe -hide_banner -version | Select-Object -First 1
