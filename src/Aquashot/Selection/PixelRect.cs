namespace Aquashot.Selection;

public readonly record struct PixelRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Contains(double px, double py) => px >= X && px < Right && py >= Y && py < Bottom;
}
