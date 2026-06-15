using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aquashot.Selection;

namespace Aquashot.Capture;

public static class DesktopStitcher
{
    // Composites each monitor's frozen frame at its virtual-desktop offset (physical px),
    // producing one bitmap covering the whole virtual desktop. Handles mixed DPI because
    // each frame is already at its monitor's native physical resolution.
    public static BitmapSource Stitch(IReadOnlyList<CapturedFrame> frames, PixelRect virtualBounds)
    {
        int w = Math.Max(1, (int)virtualBounds.Width);
        int h = Math.Max(1, (int)virtualBounds.Height);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            foreach (var f in frames)
            {
                double x = f.Monitor.Bounds.X - virtualBounds.X;
                double y = f.Monitor.Bounds.Y - virtualBounds.Y;
                dc.DrawImage(f.Bitmap, new Rect(x, y, f.Monitor.Bounds.Width, f.Monitor.Bounds.Height));
            }
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
