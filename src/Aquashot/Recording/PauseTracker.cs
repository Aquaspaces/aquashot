using System.Collections.Generic;

namespace Aquashot.Recording;

// Tracks paused [start,end] intervals over a continuous gdigrab capture (which can't truly
// pause) so the encoder can DROP them. Pure + unit-tested: nothing is sent to ffmpeg here.
// Times are wall-clock seconds relative to whatever clock Start was given (0-based is fine).
public sealed class PauseTracker
{
    private readonly List<(double start, double? end)> _paused = new();
    private double _t0;

    public bool IsPaused { get; private set; }

    // Reset to a fresh recording starting at nowSeconds (use 0 for a 0-based clock).
    public void Start(double nowSeconds)
    {
        _paused.Clear();
        _t0 = nowSeconds;
        IsPaused = false;
    }

    // Open a paused interval (idempotent: a second Pause while paused is ignored).
    public void Pause(double nowSeconds)
    {
        if (IsPaused) return;
        _paused.Add((Rel(nowSeconds), null));
        IsPaused = true;
    }

    // Close the open paused interval (idempotent when not paused).
    public void Resume(double nowSeconds)
    {
        if (!IsPaused) return;
        var last = _paused[^1];
        _paused[^1] = (last.start, Rel(nowSeconds));
        IsPaused = false;
    }

    private double Rel(double now) => now - _t0;

    // The KEPT spans (the complement of the paused intervals) clamped to [0, totalSeconds].
    // An interval still open at stop is treated as paused through the end. No pauses -> one
    // full span [0, total]. Adjacent/overlapping pauses collapse; zero-length spans are dropped.
    public IReadOnlyList<(double Start, double End)> KeptIntervals(double totalSeconds)
    {
        var kept = new List<(double Start, double End)>();
        if (totalSeconds <= 0) return kept;

        // Normalise the paused intervals: clamp to [0,total], close open ones at total, sort, merge.
        var paused = new List<(double s, double e)>();
        foreach (var (s, e) in _paused)
        {
            double a = System.Math.Clamp(s, 0, totalSeconds);
            double b = System.Math.Clamp(e ?? totalSeconds, 0, totalSeconds);
            if (b > a) paused.Add((a, b));
        }
        paused.Sort((x, y) => x.s.CompareTo(y.s));

        var merged = new List<(double s, double e)>();
        foreach (var p in paused)
        {
            if (merged.Count > 0 && p.s <= merged[^1].e) merged[^1] = (merged[^1].s, System.Math.Max(merged[^1].e, p.e));
            else merged.Add(p);
        }

        double cursor = 0;
        foreach (var (s, e) in merged)
        {
            if (s > cursor) kept.Add((cursor, s));
            cursor = e;
        }
        if (cursor < totalSeconds) kept.Add((cursor, totalSeconds));
        return kept;
    }
}
