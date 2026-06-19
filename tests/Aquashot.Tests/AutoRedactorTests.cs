using System.Collections.Generic;
using System.Linq;
using System.Windows;
using FluentAssertions;
using Aquashot.Annotation;
using Aquashot.History;
using Aquashot.Redaction;
using Xunit;

namespace Aquashot.Tests;

public class AutoRedactorTests
{
    private static OcrLine Line(string text, double x, double y) =>
        new(text, new Rect(x, y, 80, 16));

    private static readonly IReadOnlyList<OcrLine> Sample = new[]
    {
        Line("hello world", 10, 10),
        Line("contact me@example.com today", 10, 40),
        Line("card 4111 1111 1111 1111", 10, 70),
    };

    [Fact]
    public void SelectLines_EmptyPatterns_ReturnsAll()
    {
        AutoRedactor.SelectLines(Sample, "").Should().HaveCount(3);
        AutoRedactor.SelectLines(Sample, "   ").Should().HaveCount(3);
    }

    [Fact]
    public void SelectLines_EmailPattern_MatchesOnlyEmailLine()
    {
        var chosen = AutoRedactor.SelectLines(Sample, @"\b[\w.+-]+@[\w-]+\.[\w.-]+\b");
        chosen.Should().ContainSingle();
        chosen[0].Text.Should().Contain("me@example.com");
    }

    [Fact]
    public void SelectLines_CreditCardPattern_MatchesCardLine()
    {
        var chosen = AutoRedactor.SelectLines(Sample, @"\b(?:\d[ -]*?){13,16}\b");
        chosen.Should().ContainSingle();
        chosen[0].Text.Should().Contain("4111");
    }

    [Fact]
    public void SelectLines_MultiplePatterns_MatchesAnyAndIgnoresInvalidRegex()
    {
        // "(" is an invalid regex and must be skipped, not throw; the email pattern still matches.
        var chosen = AutoRedactor.SelectLines(Sample, @"(;\b[\w.+-]+@[\w-]+\.[\w.-]+\b");
        chosen.Should().ContainSingle().Which.Text.Should().Contain("@");
    }

    [Fact]
    public void SelectLines_OnlyInvalidRegex_RedactsNothing()
    {
        // No usable patterns left after dropping the invalid one => nothing matches (not "all").
        AutoRedactor.SelectLines(Sample, "(").Should().BeEmpty();
    }

    [Fact]
    public void BuildShapes_OffsetsBoxesByCropOrigin()
    {
        var shapes = AutoRedactor.BuildShapes(new[] { Line("x", 100, 50) }, 30, 20, "Blur", 8, 12);
        shapes.Should().ContainSingle();
        var b = (BlurShape)shapes[0];
        // origin shifted by crop (-30,-20) and padded by 2 on each side
        b.X.Should().BeApproximately(100 - 30 - 2, 0.001);
        b.Y.Should().BeApproximately(50 - 20 - 2, 0.001);
    }

    [Fact]
    public void BuildShapes_StyleSelectsShapeType()
    {
        AutoRedactor.BuildShapes(new[] { Line("x", 0, 0) }, 0, 0, "Blur", 8, 12)
            .Single().Should().BeOfType<BlurShape>();
        AutoRedactor.BuildShapes(new[] { Line("x", 0, 0) }, 0, 0, "Pixelate", 8, 12)
            .Single().Should().BeOfType<PixelateShape>();
    }

    [Fact]
    public void BuildShapes_BlurUsesRadius_PixelateUsesBlock()
    {
        var blur = (BlurShape)AutoRedactor.BuildShapes(new[] { Line("x", 0, 0) }, 0, 0, "Blur", 9, 12).Single();
        blur.Radius.Should().Be(9);
        var pix = (PixelateShape)AutoRedactor.BuildShapes(new[] { Line("x", 0, 0) }, 0, 0, "Pixelate", 9, 14).Single();
        pix.Block.Should().Be(14);
    }
}
