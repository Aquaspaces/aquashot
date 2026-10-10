using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Clipboard = System.Windows.Clipboard;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;

namespace Aquashot.Pin;

// A floating, always-on-top copy of a captured/annotated region — pinned over everything
// for reference. Drag to move, scroll to zoom, Ctrl+C to re-copy, Esc / right-click to close.
public partial class PinWindow : Window
{
    private readonly BitmapSource _img;
    private readonly double _baseW, _baseH;
    private double _zoom = 1.0;
    private (int X, int Y)? _originPx;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    public PinWindow(BitmapSource img, double dpiScale)
    {
        InitializeComponent();
        _img = img;
        Img.Source = img;
        _baseW = img.PixelWidth / dpiScale;
        _baseH = img.PixelHeight / dpiScale;
        Width = _baseW;
        Height = _baseH;

        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        MouseRightButtonUp += (_, __) => Close();
        MouseWheel += OnWheel;
        KeyDown += OnKey;
        Loaded += (_, __) => { Activate(); Focus(); };
    }

    // Place the pin's top-left at a virtual-desktop pixel. WPF's Left/Top are DIPs scaled by the
    // window's current monitor, which is wrong for monitors left/above the primary or at another
    // scale, so the native window is moved before it is first shown.
    public void PlaceAtPixel(double virtualX, double virtualY) =>
        _originPx = ((int)Math.Round(virtualX), (int)Math.Round(virtualY));

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (_originPx is { } p)
            SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, p.X, p.Y, 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), 0.2, 6.0);
        Width = _baseW * _zoom;
        Height = _baseH * _zoom;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            try { Clipboard.SetImage(_img); } catch { /* clipboard may be transiently locked */ }
        }
    }
}
