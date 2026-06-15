using FluentAssertions;
using SnipTool.Annotation;
using SnipTool.Selection;
using Xunit;

namespace SnipTool.Tests;

public class BlurRegionTests
{
    [Fact]
    public void PixelateBlocks_TilesRegionByBlockSize()
    {
        var blocks = BlurRegion.PixelateBlocks(new PixelRect(0, 0, 20, 20), 10);
        blocks.Should().HaveCount(4);
        blocks.Should().Contain(new PixelRect(10, 10, 10, 10));
    }

    [Fact]
    public void PixelateBlocks_ClipsPartialEdgeBlocks()
    {
        var blocks = BlurRegion.PixelateBlocks(new PixelRect(0, 0, 15, 10), 10);
        blocks.Should().Contain(new PixelRect(10, 0, 5, 10));
    }
}
