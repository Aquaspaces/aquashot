using FluentAssertions;
using Aquashot.Recording.InputHud;
using Aquashot.Selection;
using Xunit;

namespace Aquashot.Tests;

public class HudGeometryTests
{
    [Fact]
    public void InRegion_true_inside_false_outside()
    {
        var r = new PixelRect(100, 200, 400, 300); // covers x:100..500, y:200..500
        HudGeometry.InRegion(r, 150, 250).Should().BeTrue();
        HudGeometry.InRegion(r, 100, 200).Should().BeTrue();   // top-left corner is inside
        HudGeometry.InRegion(r, 500, 250).Should().BeFalse();  // right edge is exclusive
        HudGeometry.InRegion(r, 50, 250).Should().BeFalse();   // left of region
        HudGeometry.InRegion(r, 150, 600).Should().BeFalse();  // below region
    }

    [Fact]
    public void ToLocalDip_subtracts_origin_then_divides_by_scale()
    {
        var r = new PixelRect(100, 200, 400, 300);
        var (x, y) = HudGeometry.ToLocalDip(r, 300, 500, 2.0);
        // (300-100)/2 = 100 ; (500-200)/2 = 150
        x.Should().Be(100);
        y.Should().Be(150);
    }

    [Fact]
    public void ToLocalDip_identity_at_scale_one_and_origin()
    {
        var r = new PixelRect(0, 0, 800, 600);
        var (x, y) = HudGeometry.ToLocalDip(r, 320, 240, 1.0);
        x.Should().Be(320);
        y.Should().Be(240);
    }

    [Fact]
    public void WindowDipRect_maps_region_into_dip_space()
    {
        var r = new PixelRect(200, 400, 600, 300);
        var (left, top, w, h) = HudGeometry.WindowDipRect(r, 2.0);
        left.Should().Be(100);
        top.Should().Be(200);
        w.Should().Be(300);
        h.Should().Be(150);
    }
}
