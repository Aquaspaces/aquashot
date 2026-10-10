using FluentAssertions;
using Aquashot.Selection;
using Xunit;

namespace Aquashot.Tests;

public class SelectionEngineTests
{
    [Fact]
    public void Normalize_HandlesDragInAnyDirection()
    {
        SelectionEngine.Normalize(100, 100, 30, 20)
            .Should().Be(new PixelRect(30, 20, 70, 80));
    }

    [Fact]
    public void Normalize_ZeroAreaDragYieldsZeroRect()
    {
        SelectionEngine.Normalize(50, 50, 50, 50)
            .Should().Be(new PixelRect(50, 50, 0, 0));
    }

    [Fact]
    public void Clamp_RestrictsRectToVirtualBounds()
    {
        var bounds = new PixelRect(0, 0, 1920, 1080);
        SelectionEngine.Clamp(new PixelRect(1900, 1000, 200, 200), bounds)
            .Should().Be(new PixelRect(1900, 1000, 20, 80));
    }

    [Theory]
    [InlineData(1915, 1075, 3, 3)]    // bottom-right corner
    [InlineData(-5, -5, 7, 7)]        // top-left corner, partly off-screen
    [InlineData(1919, 0, 1, 1)]       // top-right corner
    [InlineData(0, 1079, 1, 1)]       // bottom-left corner
    public void ClampWithMinSize_GrowsTinyEdgeSelectionsInward(double x, double y, double w, double h)
    {
        var bounds = new PixelRect(0, 0, 1920, 1080);
        var r = SelectionEngine.ClampWithMinSize(new PixelRect(x, y, w, h), bounds, 10);
        r.Width.Should().Be(10);
        r.Height.Should().Be(10);
        r.X.Should().BeGreaterThanOrEqualTo(bounds.X);
        r.Y.Should().BeGreaterThanOrEqualTo(bounds.Y);
        r.Right.Should().BeLessThanOrEqualTo(bounds.Right);
        r.Bottom.Should().BeLessThanOrEqualTo(bounds.Bottom);
    }

    [Fact]
    public void ClampWithMinSize_WorksOnMonitorsLeftOfPrimary()
    {
        var bounds = new PixelRect(-2560, -200, 2560, 1440);
        var r = SelectionEngine.ClampWithMinSize(new PixelRect(-3, 1236, 2, 2), bounds, 10);
        r.Should().Be(new PixelRect(-10, 1230, 10, 10));
    }

    [Fact]
    public void ClampWithMinSize_KeepsLargeSelectionsUnchanged()
    {
        var bounds = new PixelRect(0, 0, 1920, 1080);
        SelectionEngine.ClampWithMinSize(new PixelRect(100, 100, 300, 200), bounds, 10)
            .Should().Be(new PixelRect(100, 100, 300, 200));
    }
}
