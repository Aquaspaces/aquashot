using System.Linq;
using FluentAssertions;
using Aquashot.Annotation;
using Xunit;

namespace Aquashot.Tests;

public class AnnotationDocumentTests
{
    private static RectShape Rect() => new(10, 10, 50, 50, "#FF0000", 3);

    [Fact]
    public void Add_AppendsShape()
    {
        var doc = new AnnotationDocument();
        doc.Add(Rect());
        doc.Shapes.Should().HaveCount(1);
    }

    [Fact]
    public void Undo_RemovesLastShape()
    {
        var doc = new AnnotationDocument();
        doc.Add(Rect());
        doc.Undo();
        doc.Shapes.Should().BeEmpty();
    }

    [Fact]
    public void Redo_RestoresUndoneShape()
    {
        var doc = new AnnotationDocument();
        doc.Add(Rect());
        doc.Undo();
        doc.Redo();
        doc.Shapes.Should().HaveCount(1);
    }

    [Fact]
    public void Add_AfterUndo_ClearsRedoStack()
    {
        var doc = new AnnotationDocument();
        doc.Add(Rect());
        doc.Undo();
        doc.Add(Rect());
        doc.Redo(); // nothing to redo
        doc.Shapes.Should().HaveCount(1);
    }

    [Fact]
    public void NextCounter_IncrementsPerCall()
    {
        var doc = new AnnotationDocument();
        doc.NextCounter().Should().Be(1);
        doc.NextCounter().Should().Be(2);
    }

    [Fact]
    public void HitTest_ReturnsTopmostShapeUnderPoint()
    {
        var doc = new AnnotationDocument();
        doc.Add(new RectShape(0, 0, 100, 100, "#FF0000", 3));   // index 0
        doc.Add(new RectShape(10, 10, 30, 30, "#00FF00", 3));   // index 1, on top
        doc.HitTest(20, 20).Should().Be(1);   // overlap → topmost wins
        doc.HitTest(90, 90).Should().Be(0);   // only the big one
    }

    [Fact]
    public void HitTest_ReturnsMinusOneWhenNothingHit()
    {
        var doc = new AnnotationDocument();
        doc.Add(new RectShape(0, 0, 10, 10, "#FF0000", 3));
        doc.HitTest(500, 500).Should().Be(-1);
    }

    [Fact]
    public void MoveAt_TranslatesShape()
    {
        var doc = new AnnotationDocument();
        doc.Add(new RectShape(10, 10, 50, 50, "#FF0000", 3));
        doc.MoveAt(0, 5, -3);
        var r = (RectShape)doc.Shapes[0];
        r.X.Should().Be(15);
        r.Y.Should().Be(7);
    }

    [Fact]
    public void RemoveAt_DeletesShape()
    {
        var doc = new AnnotationDocument();
        doc.Add(Rect());
        doc.Add(Rect());
        doc.RemoveAt(0);
        doc.Shapes.Should().HaveCount(1);
    }

    [Fact]
    public void HitTest_LineUsesProximityNotBoundingBox()
    {
        var doc = new AnnotationDocument();
        doc.Add(new LineShape(0, 0, 100, 100, "#FF0000", 3));
        doc.HitTest(50, 50).Should().Be(0);    // on the line
        doc.HitTest(90, 10).Should().Be(-1);   // inside bbox corner but far from the line
    }

    [Fact]
    public void TranslateAll_MovesHighlightPointsAndSpotlightRect()
    {
        var doc = new AnnotationDocument();
        doc.Add(new HighlightShape(new (double, double)[] { (0, 0), (10, 0) }, "#FFFF00", 18, 0.4));
        doc.Add(new SpotlightShape(5, 5, 20, 20, "#A6000000"));
        doc.Add(new BlurShape(1, 1, 10, 10, 8));
        doc.TranslateAll(3, -2);

        var h = (HighlightShape)doc.Shapes[0];
        h.Points[0].Should().Be((3, -2));
        var sp = (SpotlightShape)doc.Shapes[1];
        sp.X.Should().Be(8); sp.Y.Should().Be(3);
        var b = (BlurShape)doc.Shapes[2];
        b.X.Should().Be(4); b.Y.Should().Be(-1);
    }

    [Fact]
    public void RemoveAllOfType_KeepsOnlyOneSpotlight()
    {
        var doc = new AnnotationDocument();
        doc.Add(new SpotlightShape(0, 0, 10, 10, "#A6000000"));
        doc.Add(new RectShape(0, 0, 5, 5, "#FF0000", 2));
        doc.RemoveAllOfType<SpotlightShape>().Should().Be(1);
        doc.Add(new SpotlightShape(1, 1, 10, 10, "#A6000000"));
        doc.Shapes.OfType<SpotlightShape>().Should().ContainSingle();
        doc.Shapes.Should().HaveCount(2); // the rect survived
    }

    [Fact]
    public void ReplaceAll_SwapsTheEntireList()
    {
        var doc = new AnnotationDocument();
        doc.Add(Rect());
        doc.ReplaceAll(new Shape[] { new RectShape(1, 1, 2, 2, "#00FF00", 1), new RectShape(3, 3, 2, 2, "#00FF00", 1) });
        doc.Shapes.Should().HaveCount(2);
    }
}
