# Codebase review — 2026-09-08

Reviewed application startup/tray coordination, settings/hotkeys, capture and selection,
annotation/redaction, recording and FFmpeg process handling, history/OCR, output/sharing,
tests, and build/release configuration. The changes prioritize preserving captures,
correct output, and usable settings. No application dependencies were added.

## Fixed

| Priority | Finding and resulting behavior | Main implementation |
| --- | --- | --- |
| P1 | Repeated screenshot filenames overwrote prior captures. Saves now exclusively create a new file and use numeric suffixes on collision. Pending recordings reserve their output names until encoding completes. | `Output/OutputService.cs`, `Recording/RecordingController.cs` |
| P1 | Audio-enabled MP4 output could omit the video stream. Both streams are now explicitly mapped. | `Recording/FFmpegArgs.cs` |
| P1 | FFmpeg's file-size cutoff could discard the end of an MP4. Complete clips are preserved and actual size excess is reported. GIF retries are checked and no longer increase small/low-FPS sources. | `Recording/RecordingEncoder.cs`, `Recording/SizeTargeter.cs` |
| P1 | Blur/pixelate annotations disappeared from recording overlays. Recordings now use opaque masks for these areas. Still-image effect rectangles are intersected with the image and cover fractional edges. | `Annotation/AnnotationRenderer.cs` |
| P1 | OCR failures were indistinguishable from no matching text. Line recognition now reports failure; automatic region-capture saving stops on failure, and manual redaction shows an error. Exports wait for pending manual redaction and stale results are discarded after image changes. | `History/WindowsOcrService.cs`, `Tray/TrayHost.cs`, editor/overlay windows |
| P2 | Settings extended below the screen without scrolling. The form now scrolls, Save/Cancel remain fixed, hotkey inputs support Tab navigation, and the freeze shortcut is editable. Non-finite numeric input is rejected. | `Settings/SettingsWindow.xaml`, code-behind |
| P2 | Swapping shortcuts left actions unregistered. Old bindings are released together before new ones are registered. Invalid shortcut strings and conflicts are reported. | `Input/HotkeyService.cs`, `Tray/TrayHost.cs` |
| P2 | A previous recording finishing could clear the busy state of a newer capture. Completion now respects ownership of that state. Repeated start/stop clicks and cancellation during startup are guarded. | `Tray/TrayHost.cs`, `Overlay/OverlayWindow.xaml.cs` |
| P2 | Encoder discovery truncated stdout, canceled subprocesses could survive, and stopping blocked the calling thread. Probe output is retained, canceled children are terminated, and stop waits asynchronously while draining pipes. | `Capture/FFmpegRunner.cs` |
| P2 | Failed startup/invalid output folders could leave capture processes or temporary overlays behind. Cleanup and stop ordering are corrected; elapsed recording time uses a monotonic clock; empty trims cannot export the untrimmed original. | `Recording/RecordingController.cs` |
| P2 | Settings/history writes could truncate the previous JSON file. Writes now replace from a neighboring temporary file. Explicit null settings use defaults; null/duplicate history entries are filtered and chronology is preserved during import. | `Settings/JsonFile.cs`, `Settings/SettingsStore.cs`, `History/CaptureLibrary.cs` |
| P2 | History deletion retained the library entry; disabled OCR still ran; playback/timers outlived the window. Deletion now updates persistent history and asynchronous/detail work observes settings and window lifetime. | `History/HistoryWindow.xaml.cs` |
| P2 | Reannotation and recording completion accessed the clipboard from worker threads. Reannotation saving and recording clipboard actions now run on the UI dispatcher; successful files can reach history normally. | `History/HistoryWindow.xaml.cs`, `Recording/RecordingController.cs` |
| P2 | Crop-button and Save actions could discard a pending crop. They now apply it; annotation and color-picker release positions are respected. | `Editor/AnnotationEditorWindow.xaml.cs`, `Editor/CropController.cs`, `ColorPicker/ColorPickerWindow.xaml.cs`, `Overlay/OverlayWindow.xaml.cs` |
| P2 | Filename tokens reinterpreted braces in window titles and could throw while typing a date format. Expansion is now a single pass and incomplete/invalid date tokens remain literal. | `Output/FilenameGenerator.cs` |
| P2 | Fullscreen/delayed/scrolling/reannotation saves ignored the clipboard preference. Those paths now apply it, and fullscreen capture resumes a frozen desktop first. | `Tray/TrayHost.cs` |
| P2 | The `.slnx` solution failed with the .NET 8 SDK configured in CI. It is replaced by `.sln`. Release builds now test before packaging and manual releases use the resolved version tag. | `Aquashot.sln`, `.github/workflows/release.yml` |
| P3 | Share markup failed to escape filenames/URLs and uploads buffered entire files. Markup is escaped and uploads stream from disk. | `Share/ShareService.cs`, uploaders |

## Second pass — 2026-10-10

The remaining findings from the first pass, all resolved.

| Priority | Finding and resulting behavior | Main implementation |
| --- | --- | --- |
| P1 | Automatic redaction covered only region/window captures, and turning off text indexing turned it off. Every still capture (region, window, monitor, all monitors, repeat, delayed, scrolling) now goes through one pipeline: redact, flatten, write, clipboard, then history, indexing, and auto-share. Redaction is independent of indexing. If redaction fails, nothing is saved. Encoding runs off the UI thread. | `Output/CapturePipeline.cs`, `Tray/TrayHost.cs` |
| P2 | Undo did not reverse moves, deletion, spotlight replacement, or crop. The document records a snapshot before each edit; a drag is one step, and the editor's crop stores its bitmap in the snapshot. Region resizes re-base history so undo keeps shapes in place. | `Annotation/AnnotationDocument.cs`, editor/overlay windows |
| P2 | Pins from a secondary monitor used monitor-local DIPs as absolute coordinates. The native window is now placed in virtual-desktop pixels before it is shown. | `Pin/PinWindow.xaml.cs` |
| P2 | Tiny selections at monitor edges could exceed bounds. The minimum size now grows inward within the monitor. | `Selection/SelectionEngine.ClampWithMinSize` |
| P2 | MP4s showed a blank detail view. They now play in a `MediaElement` (looping, click to pause); the poster frame stays if playback can't start. | `History/HistoryWindow.xaml(.cs)` |
| P2 | GIF disposal 2 and 3 composited incorrectly. A tested BGRA compositor implements clear-to-transparent and restore-previous. | `History/GifCompositor.cs`, `History/GifAnimator.cs` |
| P2 | FFmpeg failing mid-recording surfaced only on Stop. Sessions expose `Exited`; the overlay stops at once and the tray shows FFmpeg's error. A failed output reservation no longer orphans the temp capture. | `Capture/FFmpegRunner.cs`, `Recording/RecordingController.cs`, `Overlay/OverlayWindow.xaml.cs` |
| P3 | FFmpeg builds were not reproducible. The fetch script pins gyan.dev 9.0.2 essentials by version and SHA-256 and records provenance. | `scripts/fetch-ffmpeg.ps1`, `THIRD-PARTY-NOTICES.md` |

Still open: GDI capture return values are not checked explicitly, and the pin, MP4
playback, and secondary-monitor placement have not been exercised on a live
multi-monitor desktop.

## Validation

Second pass: all 354 Release tests pass with .NET SDK 8.0.425. A live gdigrab
recording through `RecordingController` with the pinned FFmpeg produced a valid MP4
and GIF, and a failing FFmpeg process completed `Exited` immediately.

First pass: all 326 Release solution tests pass on Windows with .NET SDK 8.0.425, using
`dotnet test Aquashot.sln --configuration Release --verbosity minimal`. Tests cover
regression cases for saving, hotkeys, rendering, OCR errors, recording arguments and
process lifecycle, size planning, persistence, and window construction/layout.
The Settings form was also rendered to PNG and visually inspected at a constrained
viewport; scrolling and the fixed action buttons were verified.

Live FFmpeg recording, actual microphones/loopback devices, GPU encoders, mixed-DPI
dragging/pinning, and the installer/release workflow were not exercised. FFmpeg is not
bundled in this checkout. The existing WFAC010 analyzer warning remains: the app uses
the manifest for WPF per-monitor DPI awareness despite also referencing WinForms for
its tray icon. The manifest was retained to preserve that behavior.
