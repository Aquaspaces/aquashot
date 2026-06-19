using Aquashot.Selection;

namespace Aquashot.Recording.InputHud;

// Pure coordinate maths for the input HUD, kept WPF-free so it can be unit-tested. The HUD window
// is positioned over the recorded region (virtual px) but laid out in DIP, so screen-space click
// points must be mapped into the window's local DIP space.
public static class HudGeometry
{
    // Is a screen (virtual px) point inside the recorded region? Only clicks here get a ring.
    public static bool InRegion(PixelRect region, int screenVx, int screenVy) =>
        region.Contains(screenVx, screenVy);

    // Map a screen (virtual px) click point to the HUD window's local DIP coordinates: subtract the
    // region origin to get region-local pixels, then divide by the DPI scale to get DIPs.
    public static (double x, double y) ToLocalDip(PixelRect region, int screenVx, int screenVy, double dpiScale)
    {
        double lx = (screenVx - region.X) / dpiScale;
        double ly = (screenVy - region.Y) / dpiScale;
        return (lx, ly);
    }

    // The HUD window's DIP rect (left, top, width, height) for a recorded region on a monitor whose
    // bounds origin is (monX, monY). The window covers exactly the region.
    public static (double left, double top, double width, double height) WindowDipRect(
        PixelRect region, double dpiScale)
    {
        return (region.X / dpiScale, region.Y / dpiScale, region.Width / dpiScale, region.Height / dpiScale);
    }
}
