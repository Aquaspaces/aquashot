using FluentAssertions;
using Aquashot.Overlay;
using Xunit;

namespace Aquashot.Tests;

public class CountdownSequenceTests
{
    [Theory]
    [InlineData(3, 2)]
    [InlineData(2, 1)]
    [InlineData(1, 0)]
    public void Next_decrements_toward_zero(int current, int expected)
    {
        CountdownSequence.Next(current).Should().Be(expected);
    }

    [Fact]
    public void Next_reaching_zero_signals_done()
    {
        // A full 3-2-1 run: three ticks step 3 -> 0 (the 0 value is the "done" sentinel).
        int n = 3;
        n = CountdownSequence.Next(n); n.Should().Be(2);
        n = CountdownSequence.Next(n); n.Should().Be(1);
        n = CountdownSequence.Next(n); n.Should().Be(0); // done
    }
}
