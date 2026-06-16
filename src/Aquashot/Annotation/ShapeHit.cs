using System;
using System.Windows;

namespace Aquashot.Annotation;

// Hit-testing and bounding boxes for annotation shapes, in crop-local pixel space.
// Used to pick/select an existing shape under the cursor and to draw its selection box.
public static class ShapeHit
{
    public static bool Test(Shape s, double x, double y) => s switch
    {
        RectShape r => Inside(BBox(r.X, r.Y, r.W, r.H), x, y, Tol(r.StrokeWidth)),
        EllipseShape e => Inside(BBox(e.X, e.Y, e.W, e.H), x, y, Tol(e.StrokeWidth)),
        LineShape l => DistSeg(x, y, l.X1, l.Y1, l.X2, l.Y2) <= Tol(l.StrokeWidth),
        ArrowShape a => DistSeg(x, y, a.X1, a.Y1, a.X2, a.Y2) <= Tol(a.StrokeWidth),
        PenShape p => PenHit(p, x, y),
        TextShape t => TextBox(t).Contains(x, y),
        CounterShape c => Dist(x, y, c.X, c.Y) <= CounterR(c) + 3,
        _ => false
    };

    public static Rect Bounds(Shape s) => s switch
    {
        RectShape r => Pad(BBox(r.X, r.Y, r.W, r.H)),
        EllipseShape e => Pad(BBox(e.X, e.Y, e.W, e.H)),
        LineShape l => Pad(SegBox(l.X1, l.Y1, l.X2, l.Y2)),
        ArrowShape a => Pad(SegBox(a.X1, a.Y1, a.X2, a.Y2)),
        PenShape p => Pad(PenBox(p)),
        TextShape t => Pad(TextBox(t)),
        CounterShape c => Pad(new Rect(c.X - CounterR(c), c.Y - CounterR(c), 2 * CounterR(c), 2 * CounterR(c))),
        _ => Rect.Empty
    };

    private static double CounterR(CounterShape c) => Math.Max(12, c.StrokeWidth * 5);
    private static double Tol(double w) => Math.Max(6, w / 2 + 4);

    private static Rect BBox(double x, double y, double w, double h) =>
        new(Math.Min(x, x + w), Math.Min(y, y + h), Math.Abs(w), Math.Abs(h));

    private static Rect SegBox(double x1, double y1, double x2, double y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    private static bool Inside(Rect b, double x, double y, double tol)
    {
        b.Inflate(tol, tol);
        return b.Contains(x, y);
    }

    private static Rect Pad(Rect r)
    {
        if (r.IsEmpty) return r;
        r.Inflate(4, 4);
        return r;
    }

    private static double Dist(double x1, double y1, double x2, double y2) =>
        Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));

    private static double DistSeg(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay;
        double len2 = dx * dx + dy * dy;
        if (len2 <= 0) return Dist(px, py, ax, ay);
        double t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0, 1);
        return Dist(px, py, ax + t * dx, ay + t * dy);
    }

    private static bool PenHit(PenShape p, double x, double y)
    {
        var pts = p.Points;
        double tol = Tol(p.StrokeWidth);
        for (int i = 1; i < pts.Count; i++)
            if (DistSeg(x, y, pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y) <= tol) return true;
        return pts.Count == 1 && Dist(x, y, pts[0].X, pts[0].Y) <= tol;
    }

    private static Rect PenBox(PenShape p)
    {
        if (p.Points.Count == 0) return Rect.Empty;
        double minx = double.MaxValue, miny = double.MaxValue, maxx = double.MinValue, maxy = double.MinValue;
        foreach (var pt in p.Points)
        {
            minx = Math.Min(minx, pt.X); miny = Math.Min(miny, pt.Y);
            maxx = Math.Max(maxx, pt.X); maxy = Math.Max(maxy, pt.Y);
        }
        return new Rect(minx, miny, maxx - minx, maxy - miny);
    }

    private static Rect TextBox(TextShape t)
    {
        double fs = Math.Max(12, t.StrokeWidth * 6);
        double w = Math.Max(8, t.Text.Length * fs * 0.6);
        double h = fs * 1.3;
        return new Rect(t.X, t.Y, w, h);
    }
}
