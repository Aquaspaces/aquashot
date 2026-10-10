# Bundled ffmpeg.exe

Place a Windows `ffmpeg.exe` in this folder to enable screen recording. It is
git-ignored (large binary, supplied per build). When present, the build embeds it
as the resource `Aquashot.Resources.ffmpeg.exe`; when absent, the app still runs
but choosing GIF/MP4 in the capture toolbar shows "recording unavailable".

**Quick fetch:** run `pwsh scripts/fetch-ffmpeg.ps1` from the repo root to download the
pinned, checksum-verified GPL build into this folder.

**Required build features:** `--enable-nvenc --enable-amf --enable-libvpl` (qsv)
`--enable-libx264`, the `gdigrab` and `dshow` input devices, the native `aac` encoder, and the
`palettegen`/`paletteuse`/`scale`/`fps`/`overlay`/`concat`/`trim`/`atrim`/`setpts`/`asetpts`/`amix`
filters.

**Source:** gyan.dev "essentials" static build, version and SHA-256 pinned in
`scripts/fetch-ffmpeg.ps1` (currently 9.0.2). A custom trimmed build configured with only the
features above also works.

**License:** This is a GPL build (includes libx264). Aquashot's distribution must
comply with GPL terms. nvenc needs no extra DLL — the NVIDIA driver provides
`nvEncodeAPI64.dll` at runtime.
