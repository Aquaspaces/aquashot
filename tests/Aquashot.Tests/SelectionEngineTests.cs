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
}
