# GIF + MP4 Screen Recording — Design

**Date:** 2026-06-16
**Status:** Approved (pending spec review)
**Branch:** feature/snip-tool-v1

## Goal

Add screen *recording* to Aquashot (today it only captures single static frames). Record a
selected region and output:

- **MP4** — hardware-encoded (NVENC / QuickSync / AMF / Media Foundation), small + high quality.
- **GIF** — best-quality possible via FFmpeg two-pass palette (`palettegen` → `paletteuse`).

Both produced from one recording. Files kept **under 50 MB**. Engine = **bundled FFmpeg**,
shipped as an embedded resource so Aquashot stays a self-contained exe.

## Why FFmpeg

- Only path that satisfies all constraints: hardware video encode, top-tier GIF quality,
  file-size targeting, "build on existing tech", shippable in an exe.
- Hardware GIF encoding **does not exist** (GIF = LZW, CPU, 256 colors). Hardware encoders only
  produce H.264/HEVC/AV1 → MP4. So GIF stays CPU; MP4 gets the hardware pipeline.
- FFmpeg captures the live region directly (`ddagrab`/`gdigrab`) — no C# frame loop needed.

## Capture pipeline

1. **Region select** — lightweight overlay, drag-select, returns a virtual-desktop `PixelRect`.
   No annotation phase (that belongs to the screenshot flow).
2. **Record** live region → temp intermediate (visually-lossless / high-bitrate MP4, hw encoder
   if available). Decouples capture from final encoding; required because GIF two-pass needs a
   complete source file.
   - **`ddagrab`** (Desktop Duplication API, GPU) when available → frames stay on GPU → feed
     straight into `*_nvenc`/`*_qsv`/`*_amf` = zero-copy full-GPU pipeline.
   - **`gdigrab`** fallback (universal: any GPU, RDP, older Windows) → software or hw encode.
   - Probe which to use once at startup.
3. **Stop** → produce finals from the temp:
   - **MP4**: size-targeted transcode. Bitrate ≈ size_budget / duration, `-fs 49M` hard safety.
   - **GIF**: `fps`+`scale` → `palettegen` (pass 1) → `paletteuse` with dithering (pass 2).
     Size held < 50 MB by capping fps (default 20) and width (cap ~800 px). If the result is
     still over, auto-halve fps/scale once and re-encode, then warn.

## Encoder selection

Auto-pick the first encoder in the ladder that **actually test-encodes** (~5 synthetic frames),
not merely the first present in `ffmpeg -encoders`. This is the critical NVENC correctness point:
`*_nvenc` can be compiled-in yet fail at runtime when the NVIDIA driver's `nvEncodeAPI64.dll` is
missing or too old. The test-encode proves the full path works before recording starts.

Ladder (ordered, easily extended):

| Vendor            | Encoders (preferred → fallback)            |
|-------------------|--------------------------------------------|
| NVIDIA            | `av1_nvenc` → `hevc_nvenc` → `h264_nvenc`  |
| Intel QSV         | `av1_qsv` → `hevc_qsv` → `h264_qsv`        |
| AMD AMF           | `av1_amf` → `hevc_amf` → `h264_amf`        |
| Windows generic   | `hevc_mf` → `h264_mf` (Media Foundation)   |
| Software fallback  | `libx264`                                 |

- Result cached for the session. Optional `EncoderOverride` setting (default `Auto`).
- AV1 outputs use `.mp4` (or `.mkv`) — AV1-in-MP4 plays in modern browsers/Discord; H.264/HEVC
  for widest compatibility when AV1 hw absent.

## Components (new, isolated)

| Unit | Responsibility | Tested |
|------|----------------|--------|
| `Capture/FFmpegAssets` | Extract embedded `ffmpeg.exe` → `%TEMP%/Aquashot/ffmpeg-<hash>.exe`; cache + verify SHA. | hash/cache unit test w/ fake payload |
| `Capture/IFFmpegRunner` + `FFmpegRunner` | Run process, stream stderr (progress), graceful stop (`q` to stdin), surface exit code + stderr tail. | interface mocked |
| `Recording/HardwareEncoderDetector` | Parse `-encoders`; run test-encode probe per candidate; return chosen encoder. Cached. | parse + ladder logic unit-tested |
| `Recording/FFmpegArgs` | **Pure** arg builders: capture (ddagrab/gdigrab), mp4 transcode, gif two-pass. | unit-tested (arg strings) |
| `Recording/SizeTargeter` | **Pure** math: bitrate from duration/budget; fps/scale downscale decisions for < 50 MB. | unit-tested |
| `Recording/RecordOverlay` | Region drag-select (reuse `SelectionEngine`); returns `PixelRect`. | — (UI) |
| `Recording/RecordingControlBar` | Always-on-top bar: Record/Stop, elapsed timer, live size estimate, format toggle. Excluded from capture via `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` so its UI/border never appears in the recording. | — (UI) |
| `Recording/RecordingController` | Orchestrate select → record → finalize → save → notify. Mirrors `OverlayController`. | — |
| `Output` (extend) | Save to `SaveFolder` (filename pattern + `.gif`/`.mp4`); copy file to clipboard as `CF_HDROP`; notify path + final size. | save-path/format unit test |

## Wiring

- Tray: add **"Record…"** item → `RecordingController.Start()`.
- Rename `TrayHost._capturing` → `_busy`; shared lock prevents overlapping screenshot + record.
- Optional record hotkey deferred (YAGNI for v1).

## Output / UX

- Filenames reuse `FilenamePattern`, extension `.gif` / `.mp4` per format.
- Clipboard: file copied as `CF_HDROP` (drop-paste into Discord, Explorer, chat). Raw GIF bytes on
  the clipboard are not broadly useful, so file-drop is the pragmatic choice.
- Notification (existing balloon pattern): path + final file size; warn if size cap forced a
  quality reduction.

## Error handling

- ffmpeg extract/launch failure → balloon error, abort.
- No working hw encoder → fall back to `libx264` (still records). Logged, not fatal.
- nonzero ffmpeg exit → capture stderr tail → balloon error.
- Region too small (< min) → block, like screenshot flow.
- Save/disk failure → balloon error (existing pattern).

## Testing strategy

- Pure units (`FFmpegArgs`, `SizeTargeter`, encoder-list parsing, asset hash/cache) fully
  unit-tested in `Aquashot.Tests`.
- Real recording is mocked via `IFFmpegRunner` — CI has no display/GPU, cannot run ddagrab/nvenc.
- Manual verification checklist (local, real hardware): nvenc path produces MP4; GIF quality;
  < 50 MB enforcement; gdigrab fallback; control bar absent from output.

## Build / packaging

- Bundle FFmpeg as an **embedded resource** (single-file exe, offline).
- **Trimmed build (~25-35 MB)**: keep only `ddagrab`, `gdigrab`, encoders
  `nvenc`/`qsv`(libvpl)/`amf`/`mf`/`libx264`, gif filters (`palettegen`/`paletteuse`/`scale`/`fps`),
  mp4 + gif muxers. Must be compiled `--enable-nvenc --enable-amf --enable-libvpl --enable-libx264`.
  BtbN gpl-shared Windows build satisfies this; document the exact source/build used.
- nvenc needs **no extra bundled DLL** — the NVIDIA driver provides `nvEncodeAPI64.dll` at runtime.
- **License: GPL build** (includes `libx264`). Acceptable for a personal/source-available tool.
  If closed-source distribution is ever needed, switch to an LGPL build (loses `libx264`; hw
  encoders + gif filters still work, so only no-hw-encoder machines lose MP4).

## Out of scope (v1, YAGNI)

- Annotation/drawing on recordings.
- Countdown timer, cursor-click highlight, audio capture.
- Record hotkey, fps picker UI (encoder override setting is the only optional knob).
- Trimming/editing recorded clips.

## Open assumptions (confirmed defaults)

- GPL ffmpeg build. ✓
- Trimmed binary embedded. ✓
- Auto encoder selection with NVENC verified via test-encode. ✓
