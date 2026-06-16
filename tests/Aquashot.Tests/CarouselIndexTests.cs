using Aquashot.History;
using FluentAssertions;
using Xunit;

public class CarouselIndexTests
{
    [Theory]
    [InlineData(0, 3, 1, true, 1)]
    [InlineData(2, 3, 1, true, 0)]
    [InlineData(0, 3, -1, true, 2)]
    [InlineData(2, 3, 1, false, 2)]
    [InlineData(0, 3, -1, false, 0)]
    [InlineData(0, 1, 1, true, 0)]
    [InlineData(0, 0, 1, true, -1)]
    public void Step(int current, int count, int delta, bool wrap, int expected)
    {
        CarouselIndex.Step(current, count, delta, wrap).Should().Be(expected);
    }
}
