using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Aquashot.Capture;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Aquashot.Freeze;

// A fullscreen, opaque, always-on-top snapshot of one monitor — makes the desktop look
// paused. Click or Esc to resume (dismiss). The hint banner shows only on the primary
// frame to avoid repeating across monitors.
public partial class FreezeWindow : Window
{
    private readonly CapturedFrame _frame;
    private readonly double _sc;

    public event Action? Dismissed;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;

    public FreezeWindow(CapturedFrame frame, bool showHint)
    {
        InitializeComponent();
        _frame = frame;
        _sc = frame.Monitor.DpiScale;
        FrozenImage.Source = frame.Bitmap;
        var b = frame.Monitor.Bounds;
        Width = b.Width / _sc;
        Height = b.Height / _sc;
        if (!showHint) Hint.Visibility = Visibility.Collapsed;
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

    private void OnDismiss(object sender, MouseButtonEventArgs e) => Dismissed?.Invoke();

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Dismissed?.Invoke();
    }
}
