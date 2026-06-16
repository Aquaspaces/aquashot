using System;
using System.Windows;
using System.Windows.Input;
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
