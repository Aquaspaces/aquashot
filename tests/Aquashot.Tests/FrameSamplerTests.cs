using Aquashot.ColorPicker;
using FluentAssertions;
using Xunit;

public class FrameSamplerTests
{
    [Fact]
    public void Scales_device_independent_point_to_pixel()
    {
        FrameSampler.PointToPixel(1.5, 100, 200, 3000, 2000).Should().Be((150, 300));
    }

    [Fact]
    public void Clamps_into_bounds()
    {
        FrameSampler.PointToPixel(1.0, -5, 99999, 1920, 1080).Should().Be((0, 1079));
    }
}
