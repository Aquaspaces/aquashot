using System;
using System.Collections.Generic;
using System.Linq;
using Aquashot.Capture;

namespace Aquashot.ColorPicker;

// Shows a picker window per monitor and tears them all down once a colour is picked
// or the user cancels. Mirrors OverlayController.
public class ColorPickerController
{
    private readonly List<ColorPickerWindow> _windows = new();

    public event Action<string>? Picked;
    public event Action? Cancelled;

    public void Show(IReadOnlyList<CapturedFrame> frames)
    {
        foreach (var frame in frames)
        {
            var w = new ColorPickerWindow(frame);
            w.Picked += hex => { Close(); Picked?.Invoke(hex); };
            w.Cancelled += () => { Close(); Cancelled?.Invoke(); };
            _windows.Add(w);
            w.Show();
        }
    }

    public void Close()
    {
        foreach (var w in _windows.ToList()) w.Close();
        _windows.Clear();
    }
}
