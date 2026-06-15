using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SnipTool.Selection;

namespace SnipTool.Capture;

public class GraphicsCaptureService : ICaptureService
{
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDC, IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr h);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr hmon, int type, out uint dx, out uint dy);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, int flags);
    private const int SRCCOPY = 0x00CC0020;
    private struct POINT { public int X, Y; }

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();
        foreach (var s in Screen.AllScreens)
        {
            var b = s.Bounds;
            var pt = new POINT { X = b.Left + 1, Y = b.Top + 1 };
            var hmon = MonitorFromPoint(pt, 2 /*NEAREST*/);
            double scale = 1.0;
            if (GetDpiForMonitor(hmon, 0, out uint dx, out _) == 0) scale = dx / 96.0;
            list.Add(new MonitorInfo(s.DeviceName, new PixelRect(b.Left, b.Top, b.Width, b.Height), scale));
        }
        return list;
    }

    public IReadOnlyList<CapturedFrame> FreezeAll()
    {
        var frames = new List<CapturedFrame>();
        foreach (var m in GetMonitors())
            frames.Add(new CapturedFrame(m, Grab(m.Bounds)));
        return frames;
    }

    private static BitmapSource Grab(PixelRect r)
    {
        int w = (int)r.Width, h = (int)r.Height;
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr bmp = CreateCompatibleBitmap(screen, w, h);
        IntPtr old = SelectObject(mem, bmp);
        try
        {
            BitBlt(mem, 0, 0, w, h, screen, (int)r.X, (int)r.Y, SRCCOPY);
            var src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(bmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
