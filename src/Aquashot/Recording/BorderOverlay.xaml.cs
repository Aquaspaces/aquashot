using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Aquashot.Selection;

namespace Aquashot.Recording;

// A click-through, capture-excluded chrome window that keeps the selected region's
// border visible during recording. Click-through so the user can interact with the
// app being recorded; excluded from capture so the border never appears in the output.
public partial class BorderOverlay : Window
{
    private readonly PixelRect _region;
    private readonly PixelRect _monitor;
    private readonly double _sc;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000,
                      WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    public BorderOverlay(PixelRect region, PixelRect monitorBounds, double scale)
    {
        InitializeComponent();
        _region = region;
        _monitor = monitorBounds;
        _sc = scale;
        Width = monitorBounds.Width / scale;
        Height = monitorBounds.Height / scale;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
        SetWindowPos(hwnd, HWND_TOPMOST, (int)_monitor.X, (int)_monitor.Y,
            (int)_monitor.Width, (int)_monitor.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);

        Canvas.SetLeft(Box, (_region.X - _monitor.X) / _sc);
        Canvas.SetTop(Box, (_region.Y - _monitor.Y) / _sc);
        Box.Width = _region.Width / _sc;
        Box.Height = _region.Height / _sc;
    }
}
