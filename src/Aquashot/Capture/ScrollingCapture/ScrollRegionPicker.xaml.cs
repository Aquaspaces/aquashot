using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Aquashot.Selection;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Point = System.Windows.Point;

namespace Aquashot.Capture.ScrollingCapture;

// Minimal full-desktop region picker for scrolling capture: the user drags a rectangle and we
// return it in virtual-desktop pixels. Deliberately separate from the capture/annotate overlay so
// the scrolling feature stays self-contained. ShowDialog returns true with Region set, or false on
// Esc / empty drag.
public partial class ScrollRegionPicker : Window
{
    private readonly double _scale;       // monitor DPI scale (DIP -> px)
    private readonly PixelRect _bounds;   // virtual-desktop bounds in px
    private Point? _start;
    private bool _dragging;

    // The picked region in virtual-desktop pixels (valid only when ShowDialog returned true).
    public PixelRect Region { get; private set; }

    public ScrollRegionPicker(PixelRect virtualBoundsPx, double dpiScale)
    {
        InitializeComponent();
        _bounds = virtualBoundsPx;
        _scale = dpiScale <= 0 ? 1 : dpiScale;

        // Cover the whole virtual desktop in DIP.
        Left = virtualBoundsPx.X / _scale;
        Top = virtualBoundsPx.Y / _scale;
        Width = virtualBoundsPx.Width / _scale;
        Height = virtualBoundsPx.Height / _scale;

        Loaded += (_, __) => { LayoutDim(0, 0, 0, 0); Activate(); };
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += OnKey;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; Close(); }
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(Root);
        _dragging = true;
        Hint.Visibility = Visibility.Collapsed;
        SelRect.Visibility = Visibility.Visible;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _start is not Point s) return;
        var p = e.GetPosition(Root);
        double x = Math.Min(s.X, p.X), y = Math.Min(s.Y, p.Y);
        double w = Math.Abs(p.X - s.X), h = Math.Abs(p.Y - s.Y);
        Canvas.SetLeft(SelRect, x); Canvas.SetTop(SelRect, y);
        SelRect.Width = w; SelRect.Height = h;
        LayoutDim(x, y, w, h);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_start is not Point s) { DialogResult = false; Close(); return; }
        var p = e.GetPosition(Root);
        // Map DIP selection back to virtual-desktop pixels (offset by this window's origin).
        var localPx = SelectionEngine.Normalize(s.X * _scale, s.Y * _scale, p.X * _scale, p.Y * _scale);
        var virtualPx = new PixelRect(localPx.X + _bounds.X, localPx.Y + _bounds.Y, localPx.Width, localPx.Height);
        var clamped = SelectionEngine.Clamp(virtualPx, _bounds);

        if (clamped.Width < 8 || clamped.Height < 8) { DialogResult = false; Close(); return; }
        Region = clamped;
        DialogResult = true;
        Close();
    }

    // Position the four dim panels around the current selection rect (in DIP).
    private void LayoutDim(double x, double y, double w, double h)
    {
        double W = Width, H = Height;
        Place(DimTop, 0, 0, W, y);
        Place(DimBottom, 0, y + h, W, Math.Max(0, H - (y + h)));
        Place(DimLeft, 0, y, x, h);
        Place(DimRight, x + w, y, Math.Max(0, W - (x + w)), h);
    }

    private static void Place(System.Windows.Shapes.Rectangle r, double x, double y, double w, double h)
    {
        Canvas.SetLeft(r, x); Canvas.SetTop(r, y);
        r.Width = Math.Max(0, w); r.Height = Math.Max(0, h);
    }
}
