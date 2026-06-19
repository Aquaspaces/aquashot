using FluentAssertions;
using Aquashot.Selection;
using Xunit;

namespace Aquashot.Tests;

public class RegionCodecTests
{
    [Fact]
    public void EncodeDecode_RoundTrips()
    {
        var r = new PixelRect(100, 200, 640, 480);
        var ok = RegionCodec.TryDecode(RegionCodec.Encode(r), out var back);
        ok.Should().BeTrue();
        back.X.Should().Be(100);
        back.Y.Should().Be(200);
        back.Width.Should().Be(640);
        back.Height.Should().Be(480);
    }

    [Fact]
    public void Encode_RoundsToWholePixels()
    {
        RegionCodec.Encode(new PixelRect(10.4, 20.6, 30.5, 40.5)).Should().Be("10,21,30,40");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("1,2,3")]          // too few parts
    [InlineData("1,2,3,4,5")]      // too many parts
    [InlineData("a,b,c,d")]        // non-numeric
    [InlineData("0,0,0,10")]       // zero width
    [InlineData("0,0,10,0")]       // zero height
    public void TryDecode_RejectsMalformed(string? text)
    {
        RegionCodec.TryDecode(text, out _).Should().BeFalse();
    }

    [Fact]
    public void TryDecode_AcceptsNegativeOrigin()
    {
        // Multi-monitor virtual desktops can place a region at negative virtual coordinates.
        RegionCodec.TryDecode("-1920,0,800,600", out var r).Should().BeTrue();
        r.X.Should().Be(-1920);
        r.Width.Should().Be(800);
    }
}
