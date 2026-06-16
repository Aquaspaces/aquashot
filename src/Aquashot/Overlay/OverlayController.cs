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

    public event Action<CapturedFrame, PixelRect, AnnotationDocument>? Confirmed;
    public event Action? PinRequested;
    public event Action? Cancelled;

    public void Show(IReadOnlyList<CapturedFrame> frames)
    {
        foreach (var frame in frames)
        {
            var w = new OverlayWindow(frame) { Mode = Mode, Recorder = Recorder };
            w.RegionCommitted += CloseOthers;
            w.Confirmed += (f, r, d) => { Close(); Confirmed?.Invoke(f, r, d); };
            w.PinRequested += () => { Close(); PinRequested?.Invoke(); };
            w.Cancelled += () => { Close(); Cancelled?.Invoke(); };
            _windows.Add(w);
            w.Show();
        }
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
