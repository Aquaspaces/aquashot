using System;
using FluentAssertions;
using Aquashot.Recording;
using Xunit;

namespace Aquashot.Tests;

public class SizeTargeterTests
{
    private const long Budget = 50L * 1024 * 1024;

    [Fact]
    public void Bitrate_scales_inversely_with_duration()
    {
        int tenSec = SizeTargeter.BitrateKbps(TimeSpan.FromSeconds(10), Budget);
        int twentySec = SizeTargeter.BitrateKbps(TimeSpan.FromSeconds(20), Budget);
        twentySec.Should().BeLessThan(tenSec);
        tenSec.Should().BeGreaterThan(200);
    }

    [Fact]
    public void Bitrate_has_a_floor_for_long_recordings()
    {
        SizeTargeter.BitrateKbps(TimeSpan.FromHours(2), Budget).Should().BeGreaterThanOrEqualTo(200);
    }

    [Fact]
    public void Bitrate_clamps_tiny_durations()
    {
        SizeTargeter.BitrateKbps(TimeSpan.Zero, Budget).Should().BeGreaterThan(0);
    }

    [Fact]
    public void GifPlan_caps_fps_and_width()
    {
        var (fps, width) = SizeTargeter.GifPlan(1920, requestedFps: 60, maxFps: 20, maxWidth: 800);
        fps.Should().BeLessThanOrEqualTo(20);
        width.Should().BeLessThanOrEqualTo(800);
    }

    [Fact]
    public void GifPlan_keeps_small_sources_unchanged()
    {
        var (fps, width) = SizeTargeter.GifPlan(400, requestedFps: 12, maxFps: 20, maxWidth: 800);
        fps.Should().Be(12);
        width.Should().Be(400);
    }

    [Fact]
    public void GifPlan_honors_custom_caps()
    {
        var (fps, width) = SizeTargeter.GifPlan(1920, requestedFps: 60, maxFps: 15, maxWidth: 480);
        fps.Should().Be(15);
        width.Should().Be(480);
    }

    [Fact]
    public void BudgetBytes_zero_is_unlimited()
    {
        SizeTargeter.BudgetBytes(0).Should().Be(long.MaxValue);
        SizeTargeter.BudgetBytes(-5).Should().Be(long.MaxValue);
    }

    [Fact]
    public void BudgetBytes_converts_mb_to_bytes()
    {
        SizeTargeter.BudgetBytes(50).Should().Be(52428800L);
    }

    [Fact]
    public void Shrink_halves_and_floors()
    {
        var (fps, width) = SizeTargeter.Shrink((20, 800));
        fps.Should().Be(10);
        width.Should().Be(400);
        var (fps2, width2) = SizeTargeter.Shrink((8, 240));
        fps2.Should().BeGreaterThanOrEqualTo(8);
        width2.Should().BeGreaterThanOrEqualTo(240);
    }

    [Fact]
    public void Over_budget_detected()
    {
        SizeTargeter.OverBudget(Budget + 1, Budget).Should().BeTrue();
        SizeTargeter.OverBudget(Budget - 1, Budget).Should().BeFalse();
    }
}
