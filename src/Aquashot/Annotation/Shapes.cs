namespace Aquashot.Annotation;

public abstract record Shape(string Color, double StrokeWidth);

public record RectShape(double X, double Y, double W, double H, string Color, double StrokeWidth)
    : Shape(Color, StrokeWidth);

public record EllipseShape(double X, double Y, double W, double H, string Color, double StrokeWidth)
    : Shape(Color, StrokeWidth);

public record LineShape(double X1, double Y1, double X2, double Y2, string Color, double StrokeWidth)
    : Shape(Color, StrokeWidth);

public record ArrowShape(double X1, double Y1, double X2, double Y2, string Color, double StrokeWidth)
    : Shape(Color, StrokeWidth);

public record PenShape(IReadOnlyList<(double X, double Y)> Points, string Color, double StrokeWidth)
    : Shape(Color, StrokeWidth);

public record TextShape(double X, double Y, string Text, string Color, double StrokeWidth)
    : Shape(Color, StrokeWidth);

public record CounterShape(double X, double Y, int Number, string Color, double StrokeWidth)
    : Shape(Color, StrokeWidth);

public record BlurShape(double X, double Y, double W, double H, bool Pixelate, double StrokeWidth)
    : Shape("#000000", StrokeWidth);
