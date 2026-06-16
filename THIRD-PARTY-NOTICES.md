# Third-Party Notices

## Aquashot source code

Aquashot's own source is released under the **WTFPL** (see `LICENSE`). Do
whatever you want with it.

## FFmpeg (screen recording feature only)

Aquashot does **not** include `ffmpeg.exe` in this repository. The screen
recording feature is optional and inert unless an `ffmpeg.exe` is present at
`src/Aquashot/Resources/ffmpeg.exe` (git-ignored). `scripts/fetch-ffmpeg.ps1`
downloads a prebuilt **GPL** FFmpeg build (it includes libx264).

This has a licensing consequence you must respect if you redistribute binaries:

- **Source-only distribution** (this repo as-is): unaffected. No FFmpeg ships
  here, so the WTFPL covers everything you receive.
- **Binary distribution that embeds the GPL FFmpeg build** (e.g. a release
  `.exe` built after running `fetch-ffmpeg.ps1`): that combined binary becomes
  subject to the **GNU GPL**. If you publish such a build you must comply with
  the GPL for the distributed artifact — including offering the corresponding
  source. The WTFPL on Aquashot's own code is GPL-compatible, so this is
  permitted; it just means the *binary you ship* carries GPL obligations.

To avoid GPL obligations on a redistributed binary, build FFmpeg (or obtain a
build) under LGPL terms without GPL-only components such as libx264, or ship
without the recording feature.

FFmpeg: https://ffmpeg.org — © the FFmpeg developers.
