using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aquashot.Capture;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Size = System.Windows.Size;

namespace Aquashot.ColorPicker;

// Fullscreen frozen-frame overlay: hover to preview the pixel colour, click to copy it.
public partial class ColorPickerWindow : Window
{
    private readonly CapturedFrame _frame;
    private readonly double _sc;
    private (byte r, byte g, byte b) _current;

    public event Action<string>? Picked;     // hex
    public event Action? Cancelled;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;

    public ColorPickerWindow(CapturedFrame frame)
    {
        InitializeComponent();
        _frame = frame;
        _sc = frame.Monitor.DpiScale;
        FrozenImage.Source = frame.Bitmap;
        var b = frame.Monitor.Bounds;
        Width = b.Width / _sc;
        Height = b.Height / _sc;
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

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Overlay);
        _current = SampleAt((int)(p.X * _sc), (int)(p.Y * _sc));
        var hex = ColorHex.Rgb(_current.r, _current.g, _current.b);

        Swatch.Background = new SolidColorBrush(
            System.Windows.Media.Color.FromRgb(_current.r, _current.g, _current.b));
        HexText.Text = hex;
        Readout.Visibility = Visibility.Visible;
        Readout.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double lx = Math.Min(p.X + 18, ActualWidth - Readout.DesiredSize.Width - 4);
        double ly = Math.Min(p.Y + 18, ActualHeight - Readout.DesiredSize.Height - 4);
        Canvas.SetLeft(Readout, Math.Max(4, lx));
        Canvas.SetTop(Readout, Math.Max(4, ly));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        Picked?.Invoke(ColorHex.Rgb(_current.r, _current.g, _current.b));
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancelled?.Invoke();
    }

    private (byte r, byte g, byte b) SampleAt(int x, int y)
    {
        var bmp = _frame.Bitmap;
        x = Math.Clamp(x, 0, bmp.PixelWidth - 1);
        y = Math.Clamp(y, 0, bmp.PixelHeight - 1);
        var one = new FormatConvertedBitmap(
            new CroppedBitmap(bmp, new Int32Rect(x, y, 1, 1)), PixelFormats.Bgra32, null, 0);
        var px = new byte[4];
        one.CopyPixels(px, 4, 0);
        return (px[2], px[1], px[0]); // B,G,R,A → R,G,B
    }
}
