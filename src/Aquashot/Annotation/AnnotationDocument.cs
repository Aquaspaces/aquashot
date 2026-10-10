using System.Linq;
using System.Windows;

namespace Aquashot.Annotation;

// The editable annotation state. Every edit records a snapshot of the previous state, so undo/redo
// covers adds, moves, deletions, spotlight replacement, and (via SetBase) the editor's crop.
// Shapes are immutable records, so a snapshot is just a copy of the list.
public class AnnotationDocument
{
    private sealed record State(Shape[] Shapes, object? Base);

    private readonly List<Shape> _shapes = new();
    private readonly Stack<State> _undo = new();
    private readonly Stack<State> _redo = new();
    private int _counter;
    private bool _inGesture;
    private bool _gestureRecorded;

    public IReadOnlyList<Shape> Shapes => _shapes;

    // Caller-owned background for the shapes (the editor stores its cropped bitmap here), so undoing
    // a crop restores the image along with the shape coordinates. Null when unused.
    public object? Base { get; private set; }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Add(Shape shape)
    {
        Record();
        _shapes.Add(shape);
    }

    // Add several shapes as one undo step. Used by auto-redact.
    public void AddRange(IEnumerable<Shape> shapes)
    {
        var list = shapes.ToList();
        if (list.Count == 0) return;
        Record();
        _shapes.AddRange(list);
    }

    // Add a shape that must be the only one of its type (e.g. a single spotlight), as one undo step.
    public void AddReplacing<T>(T shape) where T : Shape
    {
        Record();
        _shapes.RemoveAll(s => s is T);
        _shapes.Add(shape);
    }

    // Replace the entire shape list as one undo step.
    public void ReplaceAll(IEnumerable<Shape> shapes)
    {
        Record();
        _shapes.Clear();
        _shapes.AddRange(shapes);
    }

    // Replace the shapes and the background together (the editor's crop), as one undo step.
    public void SetBase(object? newBase, IEnumerable<Shape> shapes)
    {
        Record();
        Base = newBase;
        _shapes.Clear();
        _shapes.AddRange(shapes);
    }

    // Initial background; not an undoable edit.
    public void InitBase(object? value) => Base = value;

    // Drop every shape of a given type. Returns how many were removed.
    public int RemoveAllOfType<T>() where T : Shape
    {
        if (!_shapes.Any(s => s is T)) return 0;
        Record();
        return _shapes.RemoveAll(s => s is T);
    }

    // A drag spans many MoveAt calls; group them so one undo reverts the whole drag. A gesture that
    // never changes anything leaves no undo entry, and any other edit ends it (a lost mouse-up can't
    // merge later edits into the drag).
    public void BeginGesture()
    {
        _inGesture = true;
        _gestureRecorded = false;
    }

    public void EndGesture() => _inGesture = false;

    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(Capture());
        Restore(_undo.Pop());
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(Capture());
        Restore(_redo.Pop());
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
        if (index < 0 || index >= _shapes.Count || (dx == 0 && dy == 0)) return;
        Record(move: true);
        _shapes[index] = Translate(_shapes[index], dx, dy);
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _shapes.Count) return;
        Record();
        _shapes.RemoveAt(index);
    }

    public Rect? BoundsAt(int index) =>
        index >= 0 && index < _shapes.Count ? ShapeHit.Bounds(_shapes[index]) : null;

    // Re-base every coordinate (the capture region's origin moved). This is not an edit: history is
    // translated too, so undo after a resize restores shapes in the same on-screen place.
    public void TranslateAll(double dx, double dy)
    {
        if (dx == 0 && dy == 0) return;
        for (int i = 0; i < _shapes.Count; i++) _shapes[i] = Translate(_shapes[i], dx, dy);
        TranslateStack(_undo, dx, dy);
        TranslateStack(_redo, dx, dy);
    }

    private void Record(bool move = false)
    {
        if (!move) _inGesture = false;
        if (_inGesture)
        {
            if (_gestureRecorded) return;
            _gestureRecorded = true;
        }
        _undo.Push(Capture());
        _redo.Clear();
    }

    private State Capture() => new(_shapes.ToArray(), Base);

    private void Restore(State s)
    {
        _shapes.Clear();
        _shapes.AddRange(s.Shapes);
        Base = s.Base;
        // Continue numbering after the highest marker still on the canvas.
        _counter = _shapes.OfType<CounterShape>().Select(c => c.Number).DefaultIfEmpty(0).Max();
    }

    private static void TranslateStack(Stack<State> stack, double dx, double dy)
    {
        if (stack.Count == 0) return;
        var states = stack.ToArray(); // top first
        stack.Clear();
        for (int i = states.Length - 1; i >= 0; i--)
        {
            var st = states[i];
            stack.Push(st with { Shapes = st.Shapes.Select(sh => Translate(sh, dx, dy)).ToArray() });
        }
    }

    internal static Shape Translate(Shape s, double dx, double dy) => s switch
    {
        RectShape r => r with { X = r.X + dx, Y = r.Y + dy },
        EllipseShape e => e with { X = e.X + dx, Y = e.Y + dy },
        LineShape l => l with { X1 = l.X1 + dx, Y1 = l.Y1 + dy, X2 = l.X2 + dx, Y2 = l.Y2 + dy },
        ArrowShape a => a with { X1 = a.X1 + dx, Y1 = a.Y1 + dy, X2 = a.X2 + dx, Y2 = a.Y2 + dy },
        PenShape p => p with { Points = p.Points.Select(pt => (pt.X + dx, pt.Y + dy)).ToList() },
        HighlightShape h => h with { Points = h.Points.Select(pt => (pt.X + dx, pt.Y + dy)).ToList() },
        SpotlightShape sp => sp with { X = sp.X + dx, Y = sp.Y + dy },
        BlurShape b => b with { X = b.X + dx, Y = b.Y + dy },
        PixelateShape px => px with { X = px.X + dx, Y = px.Y + dy },
        TextShape t => t with { X = t.X + dx, Y = t.Y + dy },
        CounterShape c => c with { X = c.X + dx, Y = c.Y + dy },
        _ => s
    };
}
