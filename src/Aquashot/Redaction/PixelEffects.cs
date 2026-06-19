using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Aquashot.Redaction;

// Pure bitmap effects used by auto-redact (blur / pixelate a region). Each returns a NEW
// bitmap the same pixel size as the input, so callers can DrawImage it back over the rect.
public static class PixelEffects
{
    // Mosaic: downscale nearest-neighbour to ~1px per block, then upscale back. Block clamps to >=1
    // and to the image size so a single-pixel block can't divide by zero.
    public static BitmapSource Pixelate(BitmapSource src, int block)
    {
        int w = Math.Max(1, src.PixelWidth), h = Math.Max(1, src.PixelHeight);
        int b = Math.Clamp(block, 1, Math.Max(w, h));
        int sw = Math.Max(1, (int)Math.Ceiling(w / (double)b));
        int sh = Math.Max(1, (int)Math.Ceiling(h / (double)b));

        var down = Scale(src, sw, sh);
        var up = Scale(down, w, h);
        up.Freeze();
        return up;
    }

    // Gaussian blur via a BlurEffect on a DrawingVisual; survives RenderTargetBitmap.
    public static BitmapSource Blur(BitmapSource src, double radius)
    {
        int w = Math.Max(1, src.PixelWidth), h = Math.Max(1, src.PixelHeight);
        double r = Math.Max(0, radius);
        var dv = new DrawingVisual { Effect = new BlurEffect { Radius = r, KernelType = KernelType.Gaussian } };
        using (var dc = dv.RenderOpen())
            dc.DrawImage(src, new Rect(0, 0, w, h));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    // Nearest-neighbour resample to an exact pixel size.
    private static BitmapSource Scale(BitmapSource src, int w, int h)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.NearestNeighbor);
            dc.DrawImage(src, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
