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

    [Fact]
    public void Generate_SubstitutesWindowAndAppTokens()
    {
        var name = FilenameGenerator.Generate("{app}_{window}", "png", Ts, "My Document", "notepad");
        name.Should().Be("notepad_My Document.png");
    }

    [Fact]
    public void Generate_TokensAndDatePatternCoexist()
    {
        var name = FilenameGenerator.Generate("{app}_{yyyy-MM-dd}", "png", Ts, null, "notepad");
        name.Should().Be("notepad_2026-06-15.png");
    }

    [Fact]
    public void Generate_SanitizesIllegalCharsInTokens()
    {
        var name = FilenameGenerator.Generate("{window}", "png", Ts, "a/b:c*d?\"e<f>g|h", null);
        name.Should().Be("a_b_c_d__e_f_g_h.png"); // each illegal char -> '_'
        name.Should().NotContainAny("/", ":", "*", "?", "\"", "<", ">", "|");
    }

    [Fact]
    public void Generate_AbsentTokenDataYieldsEmpty()
    {
        FilenameGenerator.Generate("shot{window}", "png", Ts).Should().Be("shot.png");
        FilenameGenerator.Generate("shot{app}", "png", Ts, "", "").Should().Be("shot.png");
    }

    [Fact]
    public void Generate_TokensAreCaseInsensitive()
    {
        FilenameGenerator.Generate("{App}-{WINDOW}", "png", Ts, "Win", "App")
            .Should().Be("App-Win.png");
    }

    [Fact]
    public void Sanitize_CollapsesWhitespaceAndTrims()
    {
        FilenameGenerator.Sanitize("  a   b\tc  ").Should().Be("a b c");
    }

    [Fact]
    public void Sanitize_CapsLengthAtSixty()
    {
        FilenameGenerator.Sanitize(new string('x', 200)).Length.Should().Be(60);
    }

    [Fact]
    public void Sanitize_NullOrBlankYieldsEmpty()
    {
        FilenameGenerator.Sanitize(null).Should().BeEmpty();
        FilenameGenerator.Sanitize("   ").Should().BeEmpty();
    }
}
