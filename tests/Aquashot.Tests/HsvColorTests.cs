using Aquashot.ColorPicker;
using FluentAssertions;
using Xunit;

public class HsvColorTests
{
    [Theory]
    [InlineData(255, 0, 0, 0)]
    [InlineData(0, 255, 0, 120)]
    [InlineData(0, 0, 255, 240)]
    public void FromRgb_primary_hues(byte r, byte g, byte b, double hue)
    {
        var hsv = HsvColor.FromRgb(r, g, b);
        hsv.H.Should().BeApproximately(hue, 0.001);
        hsv.S.Should().BeApproximately(1.0, 0.001);
        hsv.V.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void White_and_black()
    {
        HsvColor.FromRgb(255, 255, 255).V.Should().BeApproximately(1.0, 0.001);
        HsvColor.FromRgb(255, 255, 255).S.Should().BeApproximately(0.0, 0.001);
        HsvColor.FromRgb(0, 0, 0).V.Should().BeApproximately(0.0, 0.001);
    }

    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(18, 200, 77)]
    [InlineData(123, 45, 200)]
    [InlineData(255, 255, 255)]
    [InlineData(0, 0, 0)]
    public void Rgb_roundtrips_through_hsv(byte r, byte g, byte b)
    {
        var (rr, gg, bb) = HsvColor.FromRgb(r, g, b).ToRgb();
        rr.Should().Be(r); gg.Should().Be(g); bb.Should().Be(b);
    }

    [Fact]
    public void Hex_roundtrip()
    {
        HsvColor.FromHex("#7B2DC8").ToHex().Should().Be("#7B2DC8");
        HsvColor.FromHex("7b2dc8").ToHex().Should().Be("#7B2DC8");
    }
}
