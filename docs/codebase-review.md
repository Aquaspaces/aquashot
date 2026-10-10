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

## Remaining findings

These are known limitations found in the review, not completed fixes.

1. **P1 — Automatic redaction does not cover every capture path.** The automatic OCR step lives in the normal region/window confirmation callback in `Tray/TrayHost.cs`. All-monitor, repeat, delayed, and scrolling saves bypass it, and disabling OCR also bypasses it. Unify the save pipeline before describing automatic redaction as applying to every capture. OCR matching itself still requires visual verification.
2. **P2 — Undo does not reverse moves, deletion, or crop.** `Annotation/AnnotationDocument.cs` uses a stack of added shapes; `MoveAt`, `RemoveAt`, and replacement/crop operations do not preserve prior document/image state. Add operation-level history before promising full undo.
3. **P2 — Secondary-monitor pin placement uses local coordinates.** `Overlay/OverlayWindow.xaml.cs` passes monitor-local DIP coordinates to the pin window's absolute position. Position the native window in virtual-desktop pixels and verify monitors left/above the primary and mixed scales.
4. **P2 — Tiny selections at monitor edges can exceed bounds.** `OverlayWindow.ClampToMonitor` applies a minimum size after clipping. Clamp the resulting size and origin together, then exercise selection/resize at all four corners.
5. **P2 — MP4 detail view has no video renderer.** `HistoryWindow.LoadDetailHeavy` uses a bitmap loader for non-GIF files. The gallery can therefore show a blank detail area for MP4; opening the file externally remains the available playback route.
6. **P2 — Some optimized GIF previews composite incorrectly.** `History/GifAnimator.cs` paints transparent pixels over old content for disposal mode 2 and approximates mode 3 by keeping the frame. Implement actual background restoration and prior-frame restoration with fixture GIFs.
7. **P2 — Early recording-process failures surface only when stopped.** A failed encoder/device can leave the UI timer running. Add a process-completion signal to the capture session and connect it to the recording UI; GDI capture return values also need explicit failure diagnostics.
8. **P3 — FFmpeg builds are not reproducible.** `scripts/fetch-ffmpeg.ps1` downloads the mutable latest build without a pinned version/digest. Pin and verify the binary used for releases and retain its source/license provenance.

## Validation

All 326 Release solution tests pass on Windows with .NET SDK 8.0.425, using
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
