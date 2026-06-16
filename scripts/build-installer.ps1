<#
.SYNOPSIS
  Builds the Aquashot Windows installer locally: fetch ffmpeg -> publish self-contained -> compile Inno Setup.

.PARAMETER Version
  Version stamped into the assembly and installer (e.g. 1.2.3). Defaults to 0.0.0-dev.

.PARAMETER SkipFetch
  Skip downloading ffmpeg (use whatever is already in src/Aquashot/Resources).

.NOTES
  Requires: .NET 8 SDK and Inno Setup 6 (ISCC.exe on PATH, or installed at the default location).
  Bundling ffmpeg makes the produced binary GPL — see THIRD-PARTY-NOTICES.md.
#>
param(
    [string]$Version = '0.0.0-dev',
    [switch]$SkipFetch
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'publish'
$dist = Join-Path $root 'dist'

if (-not $SkipFetch) {
    Write-Host "==> Fetching ffmpeg (bundled for recording)..."
    & (Join-Path $PSScriptRoot 'fetch-ffmpeg.ps1')
}

Write-Host "==> Publishing self-contained win-x64 (v$Version)..."
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'src/Aquashot/Aquashot.csproj') `
    -c Release -r win-x64 --self-contained `
    -p:Version=$Version `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Locate ISCC.exe
$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue)?.Source
if (-not $iscc) {
    foreach ($p in @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $p) { $iscc = $p; break }
    }
}
if (-not $iscc) { throw "ISCC.exe (Inno Setup 6) not found. Install it: choco install innosetup" }

Write-Host "==> Compiling installer with $iscc ..."
New-Item -ItemType Directory -Force -Path $dist | Out-Null
& $iscc "/DMyAppVersion=$Version" "/DPublishDir=$publish" "/DOutputDir=$dist" (Join-Path $root 'installer/Aquashot.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

Write-Host "==> Done. Installer in $dist"
Get-ChildItem $dist -Filter '*.exe' | ForEach-Object { Write-Host "  $($_.Name) ($([math]::Round($_.Length/1MB,1)) MB)" }
