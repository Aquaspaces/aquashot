using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Aquashot.Overlay;

// A borderless, click-through countdown bubble centred on a point in virtual-pixel space.
// It floats ABOVE the recording region but is closed before ffmpeg starts, so the digits are
// never captured into the clip. ShowFor returns true if the countdown finished, false if cancelled.
public partial class CountdownWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly TaskCompletionSource<bool> _tcs = new();
    private int _remaining;
    private bool _done;

    public CountdownWindow(int seconds, double centerVx, double centerVy, double dpiScale)
    {
        InitializeComponent();
        _remaining = Math.Max(1, seconds);
        CountText.Text = _remaining.ToString();

        // Position so the bubble is centred on the region centre (point is virtual px; the window
        // is laid out in DIP, so divide by the monitor's DPI scale). Size is fixed at 160x160.
        Loaded += (_, __) =>
        {
            double w = ActualWidth > 0 ? ActualWidth : 160;
            double h = ActualHeight > 0 ? ActualHeight : 160;
            Left = centerVx / dpiScale - w / 2;
            Top = centerVy / dpiScale - h / 2;
        };

        _timer.Tick += OnTick;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _remaining = CountdownSequence.Next(_remaining);
        if (_remaining <= 0) { Finish(true); return; }
        CountText.Text = _remaining.ToString();
    }

    private void Finish(bool completed)
    {
        if (_done) return;
        _done = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _tcs.TrySetResult(completed);
        Close();
    }

    // Cancel the countdown from the host (e.g. the overlay's Esc handler).
    public void Cancel() => Finish(false);

    // Show the bubble and start ticking; resolves true when it elapses, false if cancelled.
    public Task<bool> RunAsync()
    {
        Show();
        _timer.Start();
        return _tcs.Task;
    }
}

// Pure countdown stepping — extracted so the tick logic is unit-testable without WPF.
public static class CountdownSequence
{
    // The next value shown after a tick; 0 (or below) means "done".
    public static int Next(int current) => current - 1;
}
