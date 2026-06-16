# Aquashot

Windows-Native screenshot and screen capture tool designed with speed and power users in mind, while maintaining simplicity and ease of use.
inspired by [flameshot](https://github.com/flameshot-org/flameshot), [ShareX](https://github.com/ShareX/ShareX), and Snipping Tool.

## Features (v1)

- **Region capture** — drag a rectangle.
- **Window capture** — hover a window, click to grab it (bounds via DWM extended frame, so drop-shadow padding is excluded).
- **Inline annotation toolbar** on the frozen screen — no separate editor window:
  - arrow, rectangle, ellipse, line, freehand pen
  - text labels and auto-incrementing numbered counters
  - blur / pixelate redaction
  - color controls: quick swatches, a full **HSV color wheel**, an **eyedropper** to sample any pixel on screen, stroke width, undo/redo (also `Ctrl+Z` / `Ctrl+Y`)
- **Dual output, one action** — every confirmed capture is copied to the clipboard **and** saved to disk simultaneously, silently, as a timestamped PNG.
- **Screenshot history** — a gallery window of recent captures with thumbnails, plus **OCR text search**: every screenshot is indexed with the built-in Windows OCR engine so you can search for words *inside* your images.
- **Global hotkey** — `PrtSc` by default, rebindable. Detects and can disable the Windows 11 "use PrtSc to open Snipping Tool" mapping.
- **System tray** — capture region/window, history, settings, quit. Optional run-at-startup.

### The multi-monitor fix

The core design decision: the capture overlay is **one borderless window per monitor, each positioned in physical pixels and running at its own monitor's DPI** — never a single window spanning the virtual desktop. A single spanning surface is what breaks under mixed DPI. Selection coordinates are unified in virtual-desktop pixel space, so a drag reports correct coordinates regardless of which monitor (or DPI) it happens on.

## Tech stack

- C# / .NET 8, WPF (`PerMonitorV2` DPI awareness via app manifest)
- GDI `BitBlt` per-monitor freeze-frame capture behind an `ICaptureService` interface (swappable to `Windows.Graphics.Capture` later without touching callers)
- WPF `DrawingVisual` for vector annotation rendering
- `Windows.Media.Ocr` (built-in WinRT engine) for screenshot text indexing
- xUnit + FluentAssertions for tests

## Build & run

```bash
dotnet build
dotnet run --project src/Aquashot
```

The app starts in the system tray (no main window). Press `PrtSc` (or use the tray menu) to capture.

> Requires the **.NET 8 Windows Desktop** SDK. Targets `net8.0-windows10.0.19041.0`
> (the Windows 10 2004+ SDK projection, needed for the built-in OCR engine). A newer
> SDK (10.x) also builds it as long as the .NET 8 targeting pack is installed.

### Screen recording (optional)

GIF/MP4 recording needs an `ffmpeg.exe` that is **not** shipped in this repo. Run
`pwsh scripts/fetch-ffmpeg.ps1` to fetch one, or drop your own in
`src/Aquashot/Resources/`. Without it the app runs fine; recording just shows
"unavailable". Note the licensing consequence of bundling FFmpeg in a release
binary — see [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

## Tests

```bash
dotnet test
```

Pure logic is covered by unit tests (settings round-trip, filename generation, multi-monitor geometry, selection math, annotation undo/redo, blur tiling, hotkey parsing, startup registration, and the headless render/compose pipeline). Capture, overlay interaction, tray, and editor UX are validated by running the app.

## Project layout

```
src/Aquashot/
  Capture/      monitor enumeration + per-monitor freeze-frame
  Selection/    PixelRect, VirtualDesktop, SelectionEngine, WindowDetector
  Overlay/      per-monitor overlay windows + controller (the multi-mon core)
  Annotation/   shapes, document (undo/redo), renderer, blur math
  Editor/       inline toolbar, live annotation layer, text prompt
  Output/       compose + clipboard/disk save, filename generator
  Settings/     AppSettings, JSON store, settings window, startup registration
  Input/        global hotkey service
  Tray/         tray host wiring it all together
tests/Aquashot.Tests/
```

## Backlog (deferred past v1)

Scrolling capture · built-in upload/share · fullscreen / per-monitor one-shot mode · last-region repeat · highlighter tool · post-capture crop · "brighten only the selection" cutout polish · cross-monitor drag clamping.

## Not planned

Cross platform support, this tool is highly designed with Windows 10/11 in mind. Tested only on Windows 11, but 10 should work.

## Vibe Coding Disclosure

This software is entirely vibe coded/AI assisted (as of 2026-06-16). I can't promise it's
free of bugs, security vulnerabilities, or other major issues, and I'm not
responsible for any problems you run into using it. Read the code before
trusting it in sensitive contexts.

This software is highly tailored for my specific use cases, any major changes will have to be made by yourself.

Built using Claude Code with Opus 4.8

## License

[WTFPL](LICENSE) — do whatever you want. Third-party components (FFmpeg) carry
their own terms; see [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
