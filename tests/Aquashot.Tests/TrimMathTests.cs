using System.Collections.Generic;
using FluentAssertions;
using Aquashot.Overlay;
using Aquashot.Recording;
using Xunit;

namespace Aquashot.Tests;

public class TrimMathTests
{
    // ---- TrimWindow slider clamping ----

    [Fact]
    public void Clamp_keeps_a_valid_range_unchanged()
    {
        TrimMath.Clamp(2, 8, 10, movedStart: true).Should().Be((2.0, 8.0));
    }

    [Fact]
    public void Clamp_pushes_end_when_start_crosses_it()
    {
        var (s, e) = TrimMath.Clamp(9, 9, 10, movedStart: true);
        s.Should().BeApproximately(9, 1e-9);
        e.Should().BeApproximately(9 + TrimMath.MinSpan, 1e-9);
    }

    [Fact]
    public void Clamp_pulls_start_back_when_end_crosses_it()
    {
        var (s, e) = TrimMath.Clamp(5, 5, 10, movedStart: false);
        e.Should().BeApproximately(5, 1e-9);
        s.Should().BeApproximately(5 - TrimMath.MinSpan, 1e-9);
    }

    [Fact]
    public void Clamp_bounds_to_zero_and_duration()
    {
        TrimMath.Clamp(-3, 99, 10, movedStart: true).Should().Be((0.0, 10.0));
    }

    [Fact]
    public void Clamp_at_the_far_end_keeps_min_span_inside_duration()
    {
        var (s, e) = TrimMath.Clamp(10, 10, 10, movedStart: true);
        s.Should().BeApproximately(10 - TrimMath.MinSpan, 1e-9);
        e.Should().BeApproximately(10, 1e-9);
    }

    // ---- RecordingController.IntersectTrim / KeptSeconds / HasGaps ----

    [Fact]
    public void KeptSeconds_sums_span_lengths()
    {
        RecordingController.KeptSeconds(new[] { (0.0, 5.0), (8.0, 12.0) }).Should().Be(9);
    }

    [Fact]
    public void HasGaps_false_for_single_full_span()
    {
        RecordingController.HasGaps(new[] { (0.0, 10.0) }, 10).Should().BeFalse();
    }

    [Fact]
    public void HasGaps_true_when_a_span_is_dropped()
    {
        RecordingController.HasGaps(new[] { (0.0, 4.0), (6.0, 10.0) }, 10).Should().BeTrue();
    }

    [Fact]
    public void IntersectTrim_on_a_single_span_slices_directly()
    {
        var r = RecordingController.IntersectTrim(new[] { (0.0, 10.0) }, 2, 7);
        r.Should().Equal(new[] { (2.0, 7.0) });
    }

    [Fact]
    public void IntersectTrim_maps_recorded_time_back_across_a_gap()
    {
        // Kept spans [0,5] and [8,12] => recorded timeline is 0..9 (gap 5..8 dropped).
        // A recorded trim [3,7] = 3s into span0 .. 7s recorded (2s into span1).
        var keep = new[] { (0.0, 5.0), (8.0, 12.0) };
        var r = RecordingController.IntersectTrim(keep, 3, 7);
        r.Should().Equal(new[] { (3.0, 5.0), (8.0, 10.0) });
    }

    [Fact]
    public void IntersectTrim_empty_when_end_not_after_start()
    {
        RecordingController.IntersectTrim(new[] { (0.0, 10.0) }, 5, 5).Should().BeEmpty();
    }
}
