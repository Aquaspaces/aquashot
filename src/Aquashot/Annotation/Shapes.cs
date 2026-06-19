namespace Aquashot.Annotation;

public abstract record Shape(string Color, double StrokeWidth);

public record RectShape(double X, double Y, double W, double H, string Color, double StrokeWidth, bool Filled = false)
    : Shape(Color, StrokeWidth);

public record EllipseShape(double X, double Y, double W, double H, string Color, double StrokeWidth, bool Filled = false)
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

// Translucent marker stroke (highlighter). Opacity 0..1; Color is an opaque hex, alpha applied at draw.
public record HighlightShape(IReadOnlyList<(double X, double Y)> Points, string Color, double StrokeWidth, double Opacity)
    : Shape(Color, StrokeWidth);

// Dims everything OUTSIDE the rect; DimColor is ARGB hex. Drawn last (over all other shapes).
public record SpotlightShape(double X, double Y, double W, double H, string DimColor)
    : Shape(DimColor, 0);

// Pixel-effect shapes: blur or pixelate the underlying image inside the rect. Need the source bitmap.
public record BlurShape(double X, double Y, double W, double H, double Radius)
    : Shape("#00000000", 0);

public record PixelateShape(double X, double Y, double W, double H, int Block)
    : Shape("#00000000", 0);
