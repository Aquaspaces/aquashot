using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        PlayExpand();
    }

    // Smooth expand: grow vertically from the top edge + fade in.
    private void PlayExpand()
    {
        var scale = new ScaleTransform(1, 0.6);
        Root.RenderTransform = scale;
        Root.Opacity = 0;
        var dur = TimeSpan.FromMilliseconds(150);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.6, 1, dur) { EasingFunction = ease });
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur));
    }

    // Absolute placement in DIP (controller computes the spot below the selection).
    public void Place(double leftDip, double topDip)
    {
        Left = leftDip;
        Top = topDip;
    }

    // Started by the controller the moment capture actually begins, so the clock is
    // aligned with the recording rather than the button press.
    public void BeginTimer()
    {
        _startedUtc = DateTime.UtcNow;
        _timer.Start();
    }

    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        if (_recording) { Stopped?.Invoke(); return; }
        _recording = true;
        RecordBtn.Content = "■ Stop";
        RecordBtn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3D, 0x42));
        RecordStarted?.Invoke();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Cancelled?.Invoke();

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
