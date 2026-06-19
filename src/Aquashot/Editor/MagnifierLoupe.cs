using System.Windows;

namespace Aquashot.Editor;

// Pure geometry for the region-selection magnifier loupe: given a centre pixel and a zoom
// factor, work out the source rectangle (in source-image pixels) to crop and blow up into the
// loupe. Clamped to the image bounds so the crop never runs off an edge. Unit-testable.
public static class MagnifierLoupe
{
    // The source rect to sample for a loupe of diameter loupePx at magnification zoom, centred
    // on (px,py). The sampled span is loupePx/zoom pixels wide/tall (so it fills the loupe once
    // scaled up by zoom), clamped to [0,srcW)/[0,srcH).
    public static Int32Rect SourceRect(int px, int py, int srcW, int srcH, int loupePx, double zoom)
    {
        if (srcW < 1) srcW = 1;
        if (srcH < 1) srcH = 1;
        if (zoom <= 0) zoom = 1;

        int span = (int)Math.Round(loupePx / zoom);
        span = Math.Clamp(span, 1, Math.Min(srcW, srcH));

        int x = px - span / 2;
        int y = py - span / 2;
        x = Math.Clamp(x, 0, srcW - span);
        y = Math.Clamp(y, 0, srcH - span);
        return new Int32Rect(x, y, span, span);
    }
}
