using System;
using System.Runtime.InteropServices;

namespace Aquashot.Selection;

public class WindowDetector
{
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    private delegate bool EnumProc(IntPtr h, IntPtr p);
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private struct RECT { public int L, T, R, B; }

    // 'exclude' is our own overlay window — it's topmost and covers the whole monitor, so
    // without skipping it EnumWindows would always report it (the monitor) as the hit.
    public PixelRect? WindowAt(double vx, double vy, IntPtr exclude = default)
    {
        PixelRect? hit = null;
        EnumWindows((h, _) =>
        {
            if (hit is not null) return true;
            if (h == exclude) return true;
            if (!IsWindowVisible(h)) return true;
            if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>()) != 0)
                if (!GetWindowRect(h, out r)) return true;
            var pr = new PixelRect(r.L, r.T, r.R - r.L, r.B - r.T);
            if (pr.Width > 0 && pr.Height > 0 && pr.Contains(vx, vy)) hit = pr;
            return true; // EnumWindows yields topmost first
        }, IntPtr.Zero);
        return hit;
    }
}
