using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Aquashot.Selection;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Ellipse = System.Windows.Shapes.Ellipse;
using Point = System.Windows.Point;

namespace Aquashot.Recording.InputHud;

// Topmost, click-through overlay painted over the recorded region during recording so gdigrab
// captures it. Draws an expanding/fading ring at each mouse click and a row of fading keystroke
// chips at the bottom. Click-through + no-activate so it never steals focus or input from the
// app being recorded. All drawing happens on the UI thread (callers marshal hook events here).
public partial class InputHudWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;

    private readonly PixelRect _region;     // recorded region in virtual px
    private readonly double _sc;            // monitor DPI scale
    private readonly Color _ringColor;
    private readonly double _ringRadius;    // ring radius in region-local px
    private readonly TimeSpan _chipLinger;

    // Outstanding chip-removal timers; stopped on close so they don't tick onto a torn-down panel.
    private readonly System.Collections.Generic.List<DispatcherTimer> _chipTimers = new();
    private bool _closed;

    public InputHudWindow(PixelRect region, double dpiScale, Color ringColor, double ringRadiusPx, int chipSeconds)
    {
        InitializeComponent();
        _region = region;
        _sc = dpiScale <= 0 ? 1 : dpiScale;
        _ringColor = ringColor;
        _ringRadius = ringRadiusPx;
        _chipLinger = TimeSpan.FromSeconds(Math.Max(1, chipSeconds));

        var (left, top, w, h) = HudGeometry.WindowDipRect(region, _sc);
        Left = left; Top = top; Width = w; Height = h;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        // Click-through + layered + tool window (no taskbar/alt-tab) + never activate.
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        ex |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    // Draw an expanding, fading ring at a screen (virtual px) click point. No-op if the click
    // landed outside the recorded region. Safe to call from any thread (marshals to the UI thread).
    public void ShowClick(int screenVx, int screenVy)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => ShowClick(screenVx, screenVy))); return; }
        if (!HudGeometry.InRegion(_region, screenVx, screenVy)) return;

        var (lx, ly) = HudGeometry.ToLocalDip(_region, screenVx, screenVy, _sc);
        double r = _ringRadius / _sc;

        var ring = new Ellipse
        {
            Stroke = new SolidColorBrush(_ringColor),
            StrokeThickness = 3,
            Width = r * 2,
            Height = r * 2,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        Canvas.SetLeft(ring, lx - r);
        Canvas.SetTop(ring, ly - r);
        var scale = new ScaleTransform(0.4, 0.4);
        ring.RenderTransform = scale;
        RingCanvas.Children.Add(ring);

        var dur = TimeSpan.FromMilliseconds(450);
        var grow = new DoubleAnimation(0.4, 1.0, dur);
        var fade = new DoubleAnimation(0.9, 0.0, dur);
        fade.Completed += (_, __) => RingCanvas.Children.Remove(ring);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        ring.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    // Append a keystroke caption chip that self-removes after the linger time. Safe to call from
    // any thread. Keeps only the most recent few chips so the row doesn't run off-screen.
    public void ShowKey(string caption)
    {
        if (string.IsNullOrEmpty(caption)) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => ShowKey(caption))); return; }

        var chip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x1C, 0x1C, 0x22)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(4, 0, 0, 0),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = caption,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            }
        };
        KeysPanel.Children.Add(chip);
        while (KeysPanel.Children.Count > 6) KeysPanel.Children.RemoveAt(0);

        var timer = new DispatcherTimer { Interval = _chipLinger };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            _chipTimers.Remove(timer);
            if (_closed) return; // window gone; don't animate/remove on a dead panel
            var fade = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(250));
            fade.Completed += (_, __) => KeysPanel.Children.Remove(chip);
            chip.BeginAnimation(UIElement.OpacityProperty, fade);
        };
        _chipTimers.Add(timer);
        timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        foreach (var t in _chipTimers) t.Stop();
        _chipTimers.Clear();
        base.OnClosed(e);
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
