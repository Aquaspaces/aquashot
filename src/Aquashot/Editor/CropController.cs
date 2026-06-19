using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;
using Aquashot.Annotation;
using Aquashot.Selection;

namespace Aquashot.Editor;

// Post-capture crop: clamp a crop rect to the image, crop the bitmap, and translate shapes so
// they stay over the same content after the origin moves. Pure so the geometry is unit-testable.
public static class CropController
{
    // Clamp a crop rect to the source pixel bounds, with a minimum 1x1 size.
    public static Int32Rect Clamp(int srcW, int srcH, PixelRect crop)
    {
        int x = Math.Clamp((int)Math.Round(crop.X), 0, Math.Max(0, srcW - 1));
        int y = Math.Clamp((int)Math.Round(crop.Y), 0, Math.Max(0, srcH - 1));
        int w = (int)Math.Round(crop.Width);
        int h = (int)Math.Round(crop.Height);
        if (w < 1) w = 1;
        if (h < 1) h = 1;
        if (x + w > srcW) w = srcW - x;
        if (y + h > srcH) h = srcH - y;
        w = Math.Max(1, w); h = Math.Max(1, h);
        return new Int32Rect(x, y, w, h);
    }

    // Crop the source to the (clamped) rect.
    public static BitmapSource Apply(BitmapSource src, PixelRect crop)
    {
        var r = Clamp(src.PixelWidth, src.PixelHeight, crop);
        var cropped = new CroppedBitmap(src, r);
        cropped.Freeze();
        return cropped;
    }

    // Translate every shape by (dx, dy). Used with dx=-cropX, dy=-cropY so shapes follow the crop.
    public static IReadOnlyList<Shape> TranslateShapes(IReadOnlyList<Shape> shapes, double dx, double dy)
    {
        var doc = new AnnotationDocument();
        foreach (var s in shapes) doc.Add(s);
        doc.TranslateAll(dx, dy);
        return new List<Shape>(doc.Shapes);
    }
}
