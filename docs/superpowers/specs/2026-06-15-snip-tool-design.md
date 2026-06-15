# Aquashot — Design Spec

**Date:** 2026-06-15
**Status:** Approved (brainstorm)

A native-feeling Windows screenshot tool with power-user features. Goal: feel as polished as the Windows Snipping Tool, but with a fast Flameshot-style inline annotation flow and multi-monitor handling done correctly (the area where Flameshot itself is weak).

## Goals

- Native Windows feel and polish.
- Correct multi-monitor + mixed-DPI capture and selection (Flameshot's main weakness).
- Fast inline annotation: never leave the capture overlay.
- One action captures, annotates, and outputs to both clipboard and disk.

## Non-Goals (v1 cuts)

Explicitly deferred past v1: scrolling capture, OCR / text grab, built-in upload/share, fullscreen / per-monitor one-shot mode, last-region repeat, highlighter tool, post-capture crop.

## Tech Stack

- **C# / .NET 8 + WPF.**
- **Windows.Graphics.Capture** (Win10 1903+) for screen grab.
- Per-monitor DPI awareness via `PerMonitorV2` declared in the app manifest.
- WPF `DrawingVisual` for the annotation layer.

Rationale: mature PerMonitorV2 DPI support, trivial vector annotation, easy tray/hotkey/installer story, fast to build. C++/Direct2D rejected as overkill for this scope; Avalonia rejected (no cross-platform requirement, weaker native-Windows capture story).

## Architecture

Single WPF application that runs in the system tray.

| Component | Responsibility |
|-----------|----------------|
| **TrayHost** | App entry point. Owns the `NotifyIcon`, global hotkey, and startup registration. |
| **HotkeyService** | Registers the global hotkey via `RegisterHotKey` (PrtSc by default, rebindable). Detects the Windows 11 "use PrtSc to open screen snipping" OS mapping and offers to disable it. |
| **CaptureService** | Freezes the screen using `Windows.Graphics.Capture`. Produces one frozen bitmap **per monitor** at that monitor's native DPI. |
| **OverlayController** | Spawns **one borderless, topmost overlay window per monitor** — never a single window spanning the virtual desktop. Each overlay runs at its own monitor's DPI and shows that monitor's frozen bitmap as its background. |
| **SelectionEngine** | Region drag selection and window-detect selection (enumerate windows, hit-test under cursor, snap to window bounds). Normalizes selection coordinates into virtual-desktop space across overlays. |
| **AnnotationEditor** | In-overlay editor. Renders the inline toolbar near the selection and the vector annotation layer. |
| **OutputService** | Flattens the annotated result and writes to clipboard and a timestamped PNG simultaneously. |
| **SettingsStore** | JSON settings persisted in `%APPDATA%`. |

### The multi-monitor fix

The core design decision: **one overlay window per monitor, each at its native DPI**, rather than one giant window spanning the whole virtual desktop. A single spanning surface is exactly what breaks under mixed DPI (Flameshot's bug). With per-monitor overlays there is no stretched surface, so each monitor renders pixel-correct. Selection coordinates are unified by mapping each overlay's local coordinates into shared virtual-desktop space.

## Capture Flow

1. User presses the hotkey (PrtSc default).
2. `CaptureService` grabs a frozen bitmap for every monitor.
3. `OverlayController` shows a per-monitor frozen overlay on each screen.
4. User either drags a region rectangle, or hovers a window and clicks to grab its detected bounds.
5. The inline toolbar appears next to the selection.
6. User annotates in place.
7. User confirms (Enter / confirm button).
8. `OutputService` runs; overlays close.

`Esc` cancels and closes all overlays with no output.

## Capture Modes (v1)

- **Region** — freeform drag-to-select rectangle.
- **Window** — hover to highlight the window under the cursor, click to capture its bounds at correct DPI.

## Annotation Model

- An ordered list of vector annotation objects. Each is a serializable shape and remains re-editable until the capture is committed.
- Tools in v1: **arrow, rectangle, ellipse, line, freehand pen, text, auto-incrementing numbered counter, blur/pixelate.**
- Toolbar controls: tool selection, color picker, stroke width. The counter tool auto-increments its label (1, 2, 3, …).
- Blur/pixelate is a region effect that samples the underlying frozen bitmap.
- Undo/redo implemented as a command stack.
- Rendering: a WPF `DrawingVisual` layer composited over the frozen bitmap. On confirm, the layer is flattened into the final bitmap.

## Output

On confirm, the annotated result is composited into a single bitmap, then:

1. `Clipboard.SetImage` sets the clipboard.
2. The image is saved to disk as `Pictures\Screenshots\Screenshot_yyyy-MM-dd_HHmmss.png`.

Both happen as one action, silently, with no prompts. Save folder, filename pattern, and image format are configurable in settings. Default folder `Pictures\Screenshots`, default format PNG.

## Tray / Hotkey / Settings / Startup

- **Tray menu:** Capture region, Capture window, Settings, Quit.
- **Settings window** (WPF): hotkey rebind, save folder, filename pattern, image format, run-at-startup toggle, default tool/color.
- **Startup:** registered via the registry `Run` key (fallback: Startup folder shortcut). Toggleable from settings.
- **Hotkey conflict:** on first run, detect the Windows 11 PrtSc→Snipping Tool mapping; if present, offer to disable it so PrtSc reaches this app.

## Testing

- **Unit tests:**
  - `SelectionEngine` coordinate math, including multi-monitor and mixed-DPI fixture cases.
  - Filename generation from pattern.
  - Settings serialization round-trip.
  - Annotation undo/redo command stack.
  - Blur/pixelate region computation.
- **Manual test harness:** real capture, overlay rendering, and hotkey behavior are validated manually (hard to automate); multi-monitor coordinate logic is covered by fixture-based unit tests independent of real hardware.

## Open Questions / Risks

- `Windows.Graphics.Capture` shows a yellow capture border by default in some Windows versions; verify it can be suppressed for a frozen single-frame grab, or fall back to DXGI Desktop Duplication / BitBlt for the freeze frame.
- Window-bounds detection must account for DWM extended frame (drop shadows) to crop cleanly.
- Registering PrtSc as a global hotkey may fail if another app holds it; surface a clear settings error and allow rebinding.
