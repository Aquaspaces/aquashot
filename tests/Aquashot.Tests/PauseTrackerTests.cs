using System.Collections.Generic;
using FluentAssertions;
using Aquashot.Recording;
using Xunit;

namespace Aquashot.Tests;

public class PauseTrackerTests
{
    private static PauseTracker Started()
    {
        var t = new PauseTracker();
        t.Start(0);
        return t;
    }

    [Fact]
    public void No_pauses_yields_one_full_span()
    {
        var kept = Started().KeptIntervals(10);
        kept.Should().ContainSingle();
        kept[0].Should().Be((0.0, 10.0));
    }

    [Fact]
    public void Single_pause_in_the_middle_drops_that_gap()
    {
        var t = Started();
        t.Pause(3);
        t.Resume(6);
        var kept = t.KeptIntervals(10);
        kept.Should().Equal(new[] { (0.0, 3.0), (6.0, 10.0) });
    }

    [Fact]
    public void Two_pauses_drop_both_gaps()
    {
        var t = Started();
        t.Pause(2); t.Resume(4);
        t.Pause(7); t.Resume(8);
        var kept = t.KeptIntervals(12);
        kept.Should().Equal(new[] { (0.0, 2.0), (4.0, 7.0), (8.0, 12.0) });
    }

    [Fact]
    public void Pause_still_open_at_stop_drops_the_trailing_span()
    {
        var t = Started();
        t.Pause(6); // never resumed
        var kept = t.KeptIntervals(10);
        kept.Should().ContainSingle();
        kept[0].Should().Be((0.0, 6.0));
    }

    [Fact]
    public void Pause_immediately_at_start_keeps_only_the_tail()
    {
        var t = Started();
        t.Pause(0); t.Resume(4);
        t.KeptIntervals(10).Should().Equal(new[] { (4.0, 10.0) });
    }

    [Fact]
    public void IsPaused_reflects_toggle_state()
    {
        var t = Started();
        t.IsPaused.Should().BeFalse();
        t.Pause(1); t.IsPaused.Should().BeTrue();
        t.Resume(2); t.IsPaused.Should().BeFalse();
    }

    [Fact]
    public void Double_pause_and_double_resume_are_idempotent()
    {
        var t = Started();
        t.Pause(2); t.Pause(3); // second pause ignored
        t.Resume(5); t.Resume(6); // second resume ignored
        t.KeptIntervals(10).Should().Equal(new[] { (0.0, 2.0), (5.0, 10.0) });
    }

    [Fact]
    public void Kept_intervals_clamp_to_total()
    {
        var t = Started();
        t.Pause(8); t.Resume(20); // resume beyond total
        t.KeptIntervals(10).Should().Equal(new[] { (0.0, 8.0) });
    }

    [Fact]
    public void Zero_total_yields_no_spans()
    {
        Started().KeptIntervals(0).Should().BeEmpty();
    }
}
