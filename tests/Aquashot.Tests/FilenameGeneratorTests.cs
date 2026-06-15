using FluentAssertions;
using Aquashot.Output;
using Xunit;

namespace Aquashot.Tests;

public class FilenameGeneratorTests
{
    private static readonly DateTime Ts = new(2026, 6, 15, 14, 30, 22);

    [Fact]
    public void Generate_SubstitutesDatePattern()
    {
        var name = FilenameGenerator.Generate("Screenshot_{yyyy-MM-dd_HHmmss}", "png", Ts);
        name.Should().Be("Screenshot_2026-06-15_143022.png");
    }

    [Fact]
    public void Generate_AppendsExtensionWhenMissing()
    {
        FilenameGenerator.Generate("shot", "png", Ts).Should().Be("shot.png");
    }

    [Fact]
    public void Generate_DoesNotDoubleExtension()
    {
        FilenameGenerator.Generate("shot.png", "png", Ts).Should().Be("shot.png");
    }
}
