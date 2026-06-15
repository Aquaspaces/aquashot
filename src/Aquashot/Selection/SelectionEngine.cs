namespace Aquashot.Selection;

public static class SelectionEngine
{
    public static PixelRect Normalize(double startX, double startY, double endX, double endY)
    {
        double x = Math.Min(startX, endX);
        double y = Math.Min(startY, endY);
        return new PixelRect(x, y, Math.Abs(endX - startX), Math.Abs(endY - startY));
    }

    public static PixelRect Clamp(PixelRect r, PixelRect bounds)
    {
        double x = Math.Max(r.X, bounds.X);
        double y = Math.Max(r.Y, bounds.Y);
        double right = Math.Min(r.Right, bounds.Right);
        double bottom = Math.Min(r.Bottom, bounds.Bottom);
        return new PixelRect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }
}
