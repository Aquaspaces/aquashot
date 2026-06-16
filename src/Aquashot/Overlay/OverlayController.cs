using System;
using System.Collections.Generic;
using System.Linq;
using Aquashot.Annotation;
using Aquashot.Capture;
using Aquashot.Recording;
using Aquashot.Selection;

namespace Aquashot.Overlay;

public class OverlayController
{
    private readonly List<OverlayWindow> _windows = new();

    public OverlayWindow.OverlayMode Mode { get; set; } = OverlayWindow.OverlayMode.Region;

    // The headless recording engine the overlay's toolbar drives (set by the tray).
    public RecordingController? Recorder { get; set; }

    // Supplies a fresh capture of every monitor (the tray's FreezeAll) for re-freeze requests.
    public Func<IReadOnlyList<CapturedFrame>>? Refreeze { get; set; }

    public event Action<CapturedFrame, PixelRect, AnnotationDocument>? Confirmed;
    public event Action? PinRequested;
    public event Action? Cancelled;
    public event Action<PixelRect, int>? DelayedCapture; // region, seconds — re-capture later

    public void Show(IReadOnlyList<CapturedFrame> frames)
    {
        foreach (var frame in frames)
        {
            var w = new OverlayWindow(frame) { Mode = Mode, Recorder = Recorder };
            w.RegionCommitted += CloseOthers;
            w.Confirmed += (f, r, d) => { Close(); Confirmed?.Invoke(f, r, d); };
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
