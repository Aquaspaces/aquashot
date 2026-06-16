using System;

namespace Aquashot.ColorPicker;

// Maps a device-independent point (WPF units) on a frozen frame to a clamped pixel index,
// using the monitor's DPI scale. Mirrors the math in ColorPickerWindow.SampleAt.
public static class FrameSampler
{
    public static (int x, int y) PointToPixel(double scale, double px, double py, int widthPx, int heightPx)
    {
        int x = (int)(px * scale);
        int y = (int)(py * scale);
        return (Math.Clamp(x, 0, widthPx - 1), Math.Clamp(y, 0, heightPx - 1));
    }
}
