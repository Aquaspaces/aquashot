using FluentAssertions;
using Aquashot.Annotation;
using Xunit;

namespace Aquashot.Tests;

public class ShapeHitTests
{
    private static HighlightShape Highlight() =>
        new(new (double, double)[] { (0, 0), (100, 0) }, "#FFFF00", 18, 0.4);

    [Fact]
    public void Highlight_HitsAlongStroke()
    {
        ShapeHit.Test(Highlight(), 50, 0).Should().BeTrue();   // on the line
        ShapeHit.Test(Highlight(), 50, 200).Should().BeFalse(); // far away
    }

    [Fact]
    public void Highlight_BoundsAreNonEmpty()
    {
        ShapeHit.Bounds(Highlight()).IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Spotlight_HitsNearEdgeNotInterior()
    {
        var sp = new SpotlightShape(20, 20, 100, 100, "#A6000000");
        ShapeHit.Test(sp, 20, 60).Should().BeTrue();    // on the left edge
        ShapeHit.Test(sp, 70, 70).Should().BeFalse();   // deep inside the lit rect
    }

    [Fact]
    public void Spotlight_BoundsCoverRect()
    {
        var sp = new SpotlightShape(10, 10, 40, 30, "#A6000000");
        var b = ShapeHit.Bounds(sp);
        b.IsEmpty.Should().BeFalse();
        b.Width.Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public void Blur_HitsInsideRect()
    {
        var b = new BlurShape(10, 10, 50, 50, 8);
        ShapeHit.Test(b, 30, 30).Should().BeTrue();
        ShapeHit.Test(b, 500, 500).Should().BeFalse();
    }

    [Fact]
    public void Pixelate_HitsInsideRect()
    {
        var p = new PixelateShape(10, 10, 50, 50, 12);
        ShapeHit.Test(p, 30, 30).Should().BeTrue();
    }
}
