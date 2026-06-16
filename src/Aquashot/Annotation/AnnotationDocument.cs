using System.Linq;
using System.Windows;

namespace Aquashot.Annotation;

public class AnnotationDocument
{
    private readonly List<Shape> _shapes = new();
    private readonly Stack<Shape> _redo = new();
    private int _counter;

    public IReadOnlyList<Shape> Shapes => _shapes;

    public void Add(Shape shape)
    {
        _shapes.Add(shape);
        _redo.Clear();
    }

    public void Undo()
    {
        if (_shapes.Count == 0) return;
        var last = _shapes[^1];
        _shapes.RemoveAt(_shapes.Count - 1);
        _redo.Push(last);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _shapes.Add(_redo.Pop());
    }

    public int NextCounter() => ++_counter;

    // Topmost shape (last drawn) under the point, or -1. Crop-local pixel coords.
    public int HitTest(double x, double y)
    {
        for (int i = _shapes.Count - 1; i >= 0; i--)
            if (ShapeHit.Test(_shapes[i], x, y)) return i;
        return -1;
    }

    public void MoveAt(int index, double dx, double dy)
    {
        if (index < 0 || index >= _shapes.Count) return;
        _shapes[index] = Translate(_shapes[index], dx, dy);
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _shapes.Count) return;
        _shapes.RemoveAt(index);
        _redo.Clear();
    }

    public Rect? BoundsAt(int index) =>
        index >= 0 && index < _shapes.Count ? ShapeHit.Bounds(_shapes[index]) : null;

    public void TranslateAll(double dx, double dy)
    {
        for (int i = 0; i < _shapes.Count; i++) _shapes[i] = Translate(_shapes[i], dx, dy);
    }

    private static Shape Translate(Shape s, double dx, double dy) => s switch
    {
        RectShape r => r with { X = r.X + dx, Y = r.Y + dy },
        EllipseShape e => e with { X = e.X + dx, Y = e.Y + dy },
        LineShape l => l with { X1 = l.X1 + dx, Y1 = l.Y1 + dy, X2 = l.X2 + dx, Y2 = l.Y2 + dy },
        ArrowShape a => a with { X1 = a.X1 + dx, Y1 = a.Y1 + dy, X2 = a.X2 + dx, Y2 = a.Y2 + dy },
        PenShape p => p with { Points = p.Points.Select(pt => (pt.X + dx, pt.Y + dy)).ToList() },
        TextShape t => t with { X = t.X + dx, Y = t.Y + dy },
        CounterShape c => c with { X = c.X + dx, Y = c.Y + dy },
        _ => s
    };
}
