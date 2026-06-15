using System;
using System.Collections.Generic;
using SnipTool.Capture;
using SnipTool.Selection;

namespace SnipTool.Overlay;

public class OverlayController
{
    private readonly List<OverlayWindow> _windows = new();

    public event Action<PixelRect>? RegionSelected;
    public event Action? Cancelled;

    public void Show(IReadOnlyList<CapturedFrame> frames)
    {
        foreach (var frame in frames)
        {
            var w = new OverlayWindow(frame);
            w.RegionSelected += r => { Close(); RegionSelected?.Invoke(r); };
            w.Cancelled += () => { Close(); Cancelled?.Invoke(); };
            _windows.Add(w);
            w.Show();
        }
    }

    public void Close()
    {
        foreach (var w in _windows) w.Close();
        _windows.Clear();
    }
}
