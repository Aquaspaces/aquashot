using System;
using System.Collections.Generic;
using System.Linq;
using Aquashot.Annotation;
using Aquashot.Capture;
using Aquashot.Selection;

namespace Aquashot.Overlay;

public class OverlayController
{
    private readonly List<OverlayWindow> _windows = new();

    public OverlayWindow.OverlayMode Mode { get; set; } = OverlayWindow.OverlayMode.Region;

    public event Action<CapturedFrame, PixelRect, AnnotationDocument>? Confirmed;
    public event Action? Cancelled;

    public void Show(IReadOnlyList<CapturedFrame> frames)
    {
        foreach (var frame in frames)
        {
            var w = new OverlayWindow(frame) { Mode = Mode };
            w.RegionCommitted += CloseOthers;
            w.Confirmed += (f, r, d) => { Close(); Confirmed?.Invoke(f, r, d); };
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
