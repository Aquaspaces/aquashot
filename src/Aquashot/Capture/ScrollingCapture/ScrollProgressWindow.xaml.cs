using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Aquashot.Selection;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Aquashot.Capture.ScrollingCapture;

// Small non-activating toast shown while scrolling capture runs. Reports the current frame and
// offers Cancel; Esc also cancels. Positioned in a corner of the captured region's monitor but
// nudged clear of the region itself so it isn't part of what gets scrolled and stitched.
public partial class ScrollProgressWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    public event Action? Cancelled;

    public ScrollProgressWindow()
    {
        InitializeComponent();
        // Esc cancels even though we never take focus: a key handler covers the focused case, and
        // the controller also installs a global Esc hook for the no-focus case.
        PreviewKeyDown += OnKey;
    }

    // Park the toast at the top-left of the monitor that owns the region, in DIP. Stays out of the
    // captured rect's interior so it is never stitched into the result.
    public void PositionFor(PixelRect monitorBoundsVirtual, double dpiScale)
    {
        double sc = dpiScale <= 0 ? 1 : dpiScale;
        Left = (monitorBoundsVirtual.X / sc) + 24;
        Top = (monitorBoundsVirtual.Y / sc) + 24;
    }

    public void SetStatus(string text)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => SetStatus(text))); return; }
        StatusText.Text = text;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Cancelled?.Invoke(); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Cancelled?.Invoke();

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
