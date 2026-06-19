using System;
using System.Windows;

namespace Aquashot.Overlay;

// A simple in/out trim dialog over a recorded clip's duration. Two sliders pick start/end seconds
// (clamped so start < end with a small minimum span). The stepping math lives in TrimMath so it's
// unit-testable without WPF. Returns the chosen range via Trimmed (null if "Keep full"/cancelled).
public partial class TrimWindow : Window
{
    private readonly double _duration;
    private bool _ready;

    public (double Start, double End)? Trimmed { get; private set; }

    public TrimWindow(TimeSpan duration)
    {
        InitializeComponent();
        _duration = Math.Max(0.1, duration.TotalSeconds);
        StartSlider.Maximum = _duration;
        EndSlider.Maximum = _duration;
        StartSlider.Value = 0;
        EndSlider.Value = _duration;
        _ready = true;

        StartSlider.ValueChanged += (_, __) => OnSliderChanged(start: true);
        EndSlider.ValueChanged += (_, __) => OnSliderChanged(start: false);
        UpdateLabels();
    }

    private void OnSliderChanged(bool start)
    {
        if (!_ready) return;
        _ready = false;
        // Keep a minimum gap so the trimmed clip is never zero/negative length.
        var (s, e) = TrimMath.Clamp(StartSlider.Value, EndSlider.Value, _duration, movedStart: start);
        StartSlider.Value = s;
        EndSlider.Value = e;
        _ready = true;
        UpdateLabels();
    }

    private void UpdateLabels()
    {
        StartLabel.Text = Fmt(StartSlider.Value);
        EndLabel.Text = Fmt(EndSlider.Value);
        KeptLabel.Text = "Keeps " + Fmt(Math.Max(0, EndSlider.Value - StartSlider.Value));
    }

    private static string Fmt(double sec)
    {
        var t = TimeSpan.FromSeconds(sec);
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds / 100}";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Trimmed = (StartSlider.Value, EndSlider.Value);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Trimmed = null;
        DialogResult = true; // "Keep full" — proceed to encode the whole (pause-trimmed) clip
    }
}

// Pure trim-slider math (extracted for unit testing).
public static class TrimMath
{
    public const double MinSpan = 0.1; // never produce a sub-100ms clip

    // Clamp a (start,end) pair into [0,duration] keeping start+MinSpan <= end. movedStart says which
    // handle the user just dragged, so the OTHER handle yields when they would cross.
    public static (double Start, double End) Clamp(double start, double end, double duration, bool movedStart)
    {
        start = Math.Clamp(start, 0, duration);
        end = Math.Clamp(end, 0, duration);
        if (end - start >= MinSpan) return (start, end);

        if (movedStart)
        {
            start = Math.Min(start, duration - MinSpan);
            end = Math.Min(duration, start + MinSpan);
        }
        else
        {
            end = Math.Max(end, MinSpan);
            start = Math.Max(0, end - MinSpan);
        }
        return (start, end);
    }
}
