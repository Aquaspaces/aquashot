using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aquashot.Capture.ScrollingCapture;

// Bridges WPF BitmapSource to/from the raw row-major BGRA byte buffers ScrollStitcher works on.
// Kept tiny and separate from the pure stitcher so the maths stays WPF-free and testable.
public static class BitmapBridge
{
    public const int Bgra = 4; // bytes per pixel for Bgra32

    // Copy a BitmapSource into a tightly-packed Bgra32 buffer (stride == width*4, no padding).
    public static byte[] ToBgra(BitmapSource src, out int width, out int height)
    {
        var bmp = src.Format == PixelFormats.Bgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        width = bmp.PixelWidth;
        height = bmp.PixelHeight;
        int stride = width * Bgra;
        var buf = new byte[stride * height];
        bmp.CopyPixels(buf, stride, 0);
        return buf;
    }

    // Build a frozen Bgra32 BitmapSource from a tightly-packed buffer.
    public static BitmapSource FromBgra(byte[] pixels, int width, int height)
    {
        int stride = width * Bgra;
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bmp.Freeze();
        return bmp;
    }
}
