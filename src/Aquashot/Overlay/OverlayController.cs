using System;
using System.Collections.Generic;
using System.Linq;
using Aquashot.Annotation;
using Aquashot.Capture;
using Aquashot.Output;
using Aquashot.Recording;
using Aquashot.Selection;

namespace Aquashot.Overlay;

public class OverlayController
{
    private readonly List<OverlayWindow> _windows = new();

    public OverlayWindow.OverlayMode Mode { get; set; } = OverlayWindow.OverlayMode.Region;

    // The headless recording engine the overlay's toolbar drives (set by the tray).
    public RecordingController? Recorder { get; set; }

    // App settings + OCR service threaded to each overlay (highlighter/spotlight/auto-redact).
    public Aquashot.Settings.AppSettings Settings { get; set; } = new();
    public Aquashot.History.IOcrService? Ocr { get; set; }

    // Default clipboard action for the confirm/Stop button (set by the tray from settings).
    public Aquashot.Output.ClipboardMode DefaultClip { get; set; } = Aquashot.Output.ClipboardMode.Image;

    // Supplies a fresh capture of every monitor (the tray's FreezeAll) for re-freeze requests.
    public Func<IReadOnlyList<CapturedFrame>>? Refreeze { get; set; }

    public event Action<CapturedFrame, PixelRect, AnnotationDocument, ClipboardMode>? Confirmed;
    public event Action? PinRequested;
    public event Action? Cancelled;
    public event Action<PixelRect, int>? DelayedCapture; // region, seconds — re-capture later
    public event Action<PixelRect>? RegionCommitted;     // LAST-REGION: the committed selection

    public void Show(IReadOnlyList<CapturedFrame> frames)
    {
        foreach (var frame in frames)
        {
            var w = new OverlayWindow(frame)
            { Mode = Mode, Recorder = Recorder, DefaultClip = DefaultClip, Settings = Settings, Ocr = Ocr };
            w.RegionCommitted += CloseOthers;
            w.RegionCommittedRect += r => RegionCommitted?.Invoke(r);
            w.Confirmed += (f, r, d, clip) => { Close(); Confirmed?.Invoke(f, r, d, clip); };
            w.PinRequested += () => { Close(); PinRequested?.Invoke(); };
            w.Cancelled += () => { Close(); Cancelled?.Invoke(); };
            w.DelayedCaptureRequested += (r, s) => { Close(); DelayedCapture?.Invoke(r, s); };
            w.RefreezeRequested += () => RefreezeWindow(w);
            _windows.Add(w);
            w.Show();
        }
    }

    // Grab a fresh snapshot of all monitors and hand this window back the frame for its own
    // monitor, so it can re-freeze the live region on its new contents.
    private void RefreezeWindow(OverlayWindow w)
    {
        var frames = Refreeze?.Invoke();
        var match = frames?.FirstOrDefault(f => f.Monitor.Id == w.MonitorId);
        if (match != null) w.ApplyRefreeze(match);
    }

    private void CloseOthers(OverlayWindow keep)
    {
        foreach (var w in _windows.ToList())
            if (w != keep) { w.Close(); _windows.Remove(w); }
    }

    public void Close()
    {
        foreach (var w in _windows.ToList()) w.Close();
        _windows.Clear();
    }
}
