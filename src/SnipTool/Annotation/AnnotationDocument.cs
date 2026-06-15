namespace SnipTool.Annotation;

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
}
