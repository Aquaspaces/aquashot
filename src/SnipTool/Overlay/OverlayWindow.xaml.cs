using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using SnipTool.Capture;
using SnipTool.Selection;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace SnipTool.Overlay;

public partial class OverlayWindow : Window
{
    private readonly CapturedFrame _frame;
    private Point _start;
    private bool _dragging;

    public event Action<PixelRect>? RegionSelected;
    public event Action? Cancelled;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    public OverlayWindow(CapturedFrame frame)
    {
        InitializeComponent();
        _frame = frame;
        FrozenImage.Source = frame.Bitmap;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var b = _frame.Monitor.Bounds;
        SetWindowPos(hwnd, HWND_TOPMOST, (int)b.X, (int)b.Y, (int)b.Width, (int)b.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
        Activate();
        Focus();
    }

    private PixelRect ToVirtual(Point startDip, Point endDip)
    {
        double sc = _frame.Monitor.DpiScale;
        var local = SelectionEngine.Normalize(startDip.X * sc, startDip.Y * sc, endDip.X * sc, endDip.Y * sc);
        return new PixelRect(local.X + _frame.Monitor.Bounds.X, local.Y + _frame.Monitor.Bounds.Y,
            local.Width, local.Height);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(Overlay);
        _dragging = true;
        SelRect.Visibility = Visibility.Visible;
        Overlay.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(Overlay);
        double x = Math.Min(_start.X, p.X), y = Math.Min(_start.Y, p.Y);
        Canvas.SetLeft(SelRect, x);
        Canvas.SetTop(SelRect, y);
        SelRect.Width = Math.Abs(p.X - _start.X);
        SelRect.Height = Math.Abs(p.Y - _start.Y);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Overlay.ReleaseMouseCapture();
        var rect = ToVirtual(_start, e.GetPosition(Overlay));
        if (rect.Width < 1 || rect.Height < 1) { Cancelled?.Invoke(); return; }
        RegionSelected?.Invoke(rect);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancelled?.Invoke();
    }
}
