using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Aquashot.Capture;
using Aquashot.Selection;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Aquashot.Recording;

public partial class RecordOverlay : Window
{
    private readonly CapturedFrame _frame;
    private readonly double _sc;
    private Point _start;
    private bool _dragging;

    public event Action<PixelRect>? RegionSelected;
    public event Action? Cancelled;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;

    public RecordOverlay(CapturedFrame frame)
    {
        InitializeComponent();
        _frame = frame;
        _sc = frame.Monitor.DpiScale;
        FrozenImage.Source = frame.Bitmap;
        var b = frame.Monitor.Bounds;
        Width = b.Width / _sc; Height = b.Height / _sc;
        Dim.Width = Width; Dim.Height = Height;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var b = _frame.Monitor.Bounds;
        SetWindowPos(hwnd, HWND_TOPMOST, (int)b.X, (int)b.Y, (int)b.Width, (int)b.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
        Activate(); Focus();
    }

    private PixelRect ToVirtualRect(Point a, Point b)
    {
        var n = SelectionEngine.Normalize(a.X * _sc, a.Y * _sc, b.X * _sc, b.Y * _sc);
        return new PixelRect(n.X + _frame.Monitor.Bounds.X, n.Y + _frame.Monitor.Bounds.Y, n.Width, n.Height);
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
        Canvas.SetLeft(SelRect, Math.Min(_start.X, p.X));
        Canvas.SetTop(SelRect, Math.Min(_start.Y, p.Y));
        SelRect.Width = Math.Abs(p.X - _start.X);
        SelRect.Height = Math.Abs(p.Y - _start.Y);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Overlay.ReleaseMouseCapture();
        var rect = ToVirtualRect(_start, e.GetPosition(Overlay));
        if (rect.Width < 10 || rect.Height < 10) { Cancelled?.Invoke(); return; }
        RegionSelected?.Invoke(rect);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancelled?.Invoke();
    }
}
