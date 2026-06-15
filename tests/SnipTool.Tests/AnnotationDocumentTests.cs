using FluentAssertions;
using SnipTool.Annotation;
using Xunit;

namespace SnipTool.Tests;

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
}
