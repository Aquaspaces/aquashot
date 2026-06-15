using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnipTool.Selection;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Pen = System.Windows.Media.Pen;
using Brushes = System.Windows.Media.Brushes;
using FlowDirection = System.Windows.FlowDirection;
using Point = System.Windows.Point;

namespace SnipTool.Annotation;

public class AnnotationRenderer
{
    public void Draw(DrawingContext dc, IReadOnlyList<Shape> shapes, BitmapSource? source = null)
    {
        foreach (var s in shapes)
        {
            switch (s)
            {
                case RectShape r:
                    dc.DrawRectangle(null, Pen(r.Color, r.StrokeWidth), new Rect(r.X, r.Y, r.W, r.H));
                    break;
                case EllipseShape el:
                    dc.DrawEllipse(null, Pen(el.Color, el.StrokeWidth),
                        new Point(el.X + el.W / 2, el.Y + el.H / 2), el.W / 2, el.H / 2);
                    break;
                case LineShape l:
                    dc.DrawLine(Pen(l.Color, l.StrokeWidth), new Point(l.X1, l.Y1), new Point(l.X2, l.Y2));
                    break;
                case ArrowShape a:
                    DrawArrow(dc, a);
                    break;
                case PenShape p:
                    DrawPen(dc, p);
                    break;
                case TextShape t:
                    DrawText(dc, t);
                    break;
                case CounterShape c:
                    DrawCounter(dc, c);
                    break;
                case BlurShape b when source != null:
                    DrawBlur(dc, b, source);
                    break;
            }
        }
    }

    public BitmapSource Flatten(BitmapSource baseImage, PixelRect crop, IReadOnlyList<Shape> shapes)
    {
        int cx = (int)crop.X, cy = (int)crop.Y, cw = (int)crop.Width, ch = (int)crop.Height;
        var cropped = new CroppedBitmap(baseImage, new Int32Rect(cx, cy, cw, ch));
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(cropped, new Rect(0, 0, cw, ch));
            Draw(dc, shapes, cropped);
        }
        var rtb = new RenderTargetBitmap(cw, ch, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private static Color ParseColor(string s) => (Color)ColorConverter.ConvertFromString(s)!;

    private static Pen Pen(string color, double w)
    {
        var p = new Pen(new SolidColorBrush(ParseColor(color)), w);
        p.Freeze();
        return p;
    }

    private void DrawArrow(DrawingContext dc, ArrowShape a)
    {
        var pen = Pen(a.Color, a.StrokeWidth);
        var p1 = new Point(a.X1, a.Y1); var p2 = new Point(a.X2, a.Y2);
        dc.DrawLine(pen, p1, p2);
        double angle = Math.Atan2(a.Y2 - a.Y1, a.X2 - a.X1);
        double head = Math.Max(10, a.StrokeWidth * 4);
        double spread = Math.PI / 7;
        var b1 = new Point(a.X2 - head * Math.Cos(angle - spread), a.Y2 - head * Math.Sin(angle - spread));
        var b2 = new Point(a.X2 - head * Math.Cos(angle + spread), a.Y2 - head * Math.Sin(angle + spread));
        var brush = new SolidColorBrush(ParseColor(a.Color)); brush.Freeze();
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(p2, true, true);
            ctx.LineTo(b1, true, false);
            ctx.LineTo(b2, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(brush, null, geo);
    }

    private void DrawPen(DrawingContext dc, PenShape p)
    {
        if (p.Points.Count < 2) return;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(p.Points[0].X, p.Points[0].Y), false, false);
            for (int i = 1; i < p.Points.Count; i++)
                ctx.LineTo(new Point(p.Points[i].X, p.Points[i].Y), true, true);
        }
        geo.Freeze();
        dc.DrawGeometry(null, Pen(p.Color, p.StrokeWidth), geo);
    }

    private void DrawText(DrawingContext dc, TextShape t)
    {
        var ft = new FormattedText(t.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), Math.Max(12, t.StrokeWidth * 6),
            new SolidColorBrush(ParseColor(t.Color)), 1.0);
        dc.DrawText(ft, new Point(t.X, t.Y));
    }

    private void DrawCounter(DrawingContext dc, CounterShape c)
    {
        double r = Math.Max(12, c.StrokeWidth * 5);
        var brush = new SolidColorBrush(ParseColor(c.Color)); brush.Freeze();
        var center = new Point(c.X, c.Y);
        dc.DrawEllipse(brush, null, center, r, r);
        var ft = new FormattedText(c.Number.ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), r, Brushes.White, 1.0);
        dc.DrawText(ft, new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
    }

    private void DrawBlur(DrawingContext dc, BlurShape b, BitmapSource source)
    {
        var region = new PixelRect(b.X, b.Y, b.W, b.H);
        double block = Math.Max(6, b.W / 12);
        foreach (var blk in BlurRegion.PixelateBlocks(region, block))
        {
            var color = AverageColor(source, blk);
            var brush = new SolidColorBrush(color); brush.Freeze();
            dc.DrawRectangle(brush, null, new Rect(blk.X, blk.Y, blk.Width, blk.Height));
        }
    }

    private static Color AverageColor(BitmapSource src, PixelRect area)
    {
        int x = (int)area.X, y = (int)area.Y;
        int w = Math.Max(1, (int)area.Width), h = Math.Max(1, (int)area.Height);
        x = Math.Clamp(x, 0, src.PixelWidth - 1);
        y = Math.Clamp(y, 0, src.PixelHeight - 1);
        w = Math.Min(w, src.PixelWidth - x);
        h = Math.Min(h, src.PixelHeight - y);
        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int stride = w * 4;
        var px = new byte[h * stride];
        conv.CopyPixels(new Int32Rect(x, y, w, h), px, stride, 0);
        long bsum = 0, gsum = 0, rsum = 0; int n = w * h;
        for (int i = 0; i < px.Length; i += 4) { bsum += px[i]; gsum += px[i + 1]; rsum += px[i + 2]; }
        return Color.FromRgb((byte)(rsum / n), (byte)(gsum / n), (byte)(bsum / n));
    }
}
