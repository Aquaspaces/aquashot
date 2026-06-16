using System.Windows;
using Aquashot.History;
using FluentAssertions;
using Xunit;

public class RectMapperTests
{
    [Fact]
    public void Uniform_centers_horizontally_when_container_is_wider()
    {
        var (scale, offX, offY) = RectMapper.UniformPlacement(100, 100, 200, 100);
        scale.Should().Be(1.0);
        offX.Should().Be(50.0);
        offY.Should().Be(0.0);
    }

    [Fact]
    public void Uniform_centers_vertically_when_container_is_taller()
    {
        var (scale, offX, offY) = RectMapper.UniformPlacement(100, 50, 100, 100);
        scale.Should().Be(1.0);
        offX.Should().Be(0.0);
        offY.Should().Be(25.0);
    }

    [Fact]
    public void Uniform_scales_down_to_fit()
    {
        var (scale, _, _) = RectMapper.UniformPlacement(200, 200, 100, 100);
        scale.Should().Be(0.5);
    }

    [Fact]
    public void MapRect_scales_and_offsets()
    {
        var r = RectMapper.MapRect(new Rect(10, 10, 20, 20), 2.0, 5, 7);
        r.Should().Be(new Rect(25, 27, 40, 40));
    }
}
