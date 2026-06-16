using FluentAssertions;
using Aquashot.ColorPicker;
using Xunit;

namespace Aquashot.Tests;

public class ColorHexTests
{
    [Theory]
    [InlineData(0, 0, 0, "#000000")]
    [InlineData(255, 255, 255, "#FFFFFF")]
    [InlineData(59, 130, 246, "#3B82F6")]
    [InlineData(16, 0, 171, "#1000AB")]
    public void Rgb_formats_uppercase_hex(byte r, byte g, byte b, string expected)
    {
        ColorHex.Rgb(r, g, b).Should().Be(expected);
    }
}
