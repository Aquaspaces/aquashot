# Bundled ffmpeg.exe

Place a Windows `ffmpeg.exe` in this folder to enable screen recording. It is
git-ignored (large binary, supplied per build). When present, the build embeds it
as the resource `Aquashot.Resources.ffmpeg.exe`; when absent, the app still runs
but choosing GIF/MP4 in the capture toolbar shows "recording unavailable".

**Quick fetch:** run `pwsh scripts/fetch-ffmpeg.ps1` from the repo root to download a
prebuilt GPL build into this folder automatically.

**Required build features:** `--enable-nvenc --enable-amf --enable-libvpl` (qsv)
`--enable-libx264`, plus the `ddagrab`/`gdigrab` input devices and the
`palettegen`/`paletteuse`/`scale`/`fps` filters.

**Source:** BtbN FFmpeg-Builds `ffmpeg-master-latest-win64-gpl-shared` (or `-gpl`),
or a custom trimmed build (~25-35 MB) configured with only the features above.

**License:** This is a GPL build (includes libx264). Aquashot's distribution must
comply with GPL terms. nvenc needs no extra DLL — the NVIDIA driver provides
`nvEncodeAPI64.dll` at runtime.
