using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Aquashot.Recording;

public partial class RecordingControlBar : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _startedUtc;
    private bool _recording;

    public event Action? RecordStarted;
    public event Action? Stopped;
    public event Action? Cancelled;

    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11; // hides window from screen capture (Win10 2004+)

    public RecordingControlBar()
    {
        InitializeComponent();
        _timer.Tick += (_, __) =>
        {
            var t = DateTime.UtcNow - _startedUtc;
            Timer.Text = $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
    }

    // Place the bar just above the region (DIP coordinates).
    public void PlaceAbove(double leftDip, double topDip)
    {
        Left = leftDip;
        Top = Math.Max(0, topDip - 48);
    }

    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        if (_recording) { Stopped?.Invoke(); return; }
        _recording = true;
        _startedUtc = DateTime.UtcNow;
        _timer.Start();
        RecordBtn.Content = "■ Stop";
        RecordStarted?.Invoke();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Cancelled?.Invoke();
}
