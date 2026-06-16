using System;
using System.Windows;

namespace Aquashot.History;

// Maps source-image pixel rects to on-screen coordinates for an Image shown with
// Stretch=Uniform (letterboxed + centered). Powers the OCR text overlay positioning.
public static class RectMapper
{
    public static (double scale, double offsetX, double offsetY) UniformPlacement(
        double srcW, double srcH, double containerW, double containerH)
    {
        if (srcW <= 0 || srcH <= 0) return (1, 0, 0);
        double scale = Math.Min(containerW / srcW, containerH / srcH);
        double dispW = srcW * scale, dispH = srcH * scale;
        return (scale, (containerW - dispW) / 2, (containerH - dispH) / 2);
    }

    public static Rect MapRect(Rect boxPx, double scale, double offsetX, double offsetY)
        => new Rect(boxPx.X * scale + offsetX, boxPx.Y * scale + offsetY,
                    boxPx.Width * scale, boxPx.Height * scale);
}
