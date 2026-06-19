using FluentAssertions;
using Aquashot.Editor;
using Xunit;

namespace Aquashot.Tests;

public class MagnifierLoupeTests
{
    [Fact]
    public void SourceRect_SpanIsLoupeOverZoom()
    {
        // A 140px loupe at 2x samples a 70px source span and centres it on the pointer.
        var r = MagnifierLoupe.SourceRect(500, 500, 1920, 1080, 140, 2.0);
        r.Width.Should().Be(70);
        r.Height.Should().Be(70);
        r.X.Should().Be(500 - 35);
        r.Y.Should().Be(500 - 35);
    }

    [Fact]
    public void SourceRect_ClampsAtTopLeftEdge()
    {
        var r = MagnifierLoupe.SourceRect(0, 0, 1920, 1080, 140, 2.0);
        r.X.Should().Be(0);
        r.Y.Should().Be(0);
        r.Width.Should().Be(70);
    }

    [Fact]
    public void SourceRect_ClampsAtBottomRightEdge()
    {
        var r = MagnifierLoupe.SourceRect(1919, 1079, 1920, 1080, 140, 2.0);
        (r.X + r.Width).Should().Be(1920);
        (r.Y + r.Height).Should().Be(1080);
    }

    [Fact]
    public void SourceRect_SpanNeverExceedsImage()
    {
        // A huge loupe on a tiny image clamps the span to the smaller dimension.
        var r = MagnifierLoupe.SourceRect(5, 5, 20, 10, 400, 1.0);
        r.Width.Should().BeLessThanOrEqualTo(10);
        r.Height.Should().BeLessThanOrEqualTo(10);
        (r.X + r.Width).Should().BeLessThanOrEqualTo(20);
        (r.Y + r.Height).Should().BeLessThanOrEqualTo(10);
    }

    [Fact]
    public void SourceRect_HigherZoomSamplesSmallerSpan()
    {
        var low = MagnifierLoupe.SourceRect(500, 500, 1920, 1080, 140, 2.0);
        var high = MagnifierLoupe.SourceRect(500, 500, 1920, 1080, 140, 4.0);
        high.Width.Should().BeLessThan(low.Width); // more zoom => narrower source crop
    }

    [Fact]
    public void SourceRect_DegenerateInputsDoNotThrow()
    {
        var r = MagnifierLoupe.SourceRect(0, 0, 0, 0, 100, 0);
        r.Width.Should().BeGreaterThanOrEqualTo(1);
        r.Height.Should().BeGreaterThanOrEqualTo(1);
    }
}
