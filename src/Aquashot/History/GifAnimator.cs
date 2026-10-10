using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aquashot.History;

// Decodes a GIF into fully-composited frames + per-frame delays so a simple timer can play it
// back. Frame offsets and every disposal method are honoured (see GifCompositor), so optimised
// GIFs made of partial frames render correctly, not just full-frame ones.
public static class GifAnimator
{
    public sealed record Clip(IReadOnlyList<BitmapSource> Frames, IReadOnlyList<int> DelaysMs);

    public static Clip? Load(string path)
    {
        try
        {
            var decoder = new GifBitmapDecoder(new Uri(path), BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            int count = decoder.Frames.Count;
            if (count == 0) return null;
            if (count == 1) return new Clip(new[] { (BitmapSource)decoder.Frames[0] }, new[] { 0 });

            // Logical screen size, grown to contain every frame in case the header is too small.
            int w = ReadInt(decoder.Metadata, "/logscrdesc/Width");
            int h = ReadInt(decoder.Metadata, "/logscrdesc/Height");
            var parts = new GifCompositor.Frame[count];
            var delays = new int[count];
            for (int i = 0; i < count; i++)
            {
                var frame = decoder.Frames[i];
                var m = frame.Metadata as BitmapMetadata;
                int left = ReadInt(m, "/imgdesc/Left"), top = ReadInt(m, "/imgdesc/Top");
                int fw = frame.PixelWidth, fh = frame.PixelHeight;
                var bgra = new byte[fw * fh * 4];
                new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0).CopyPixels(bgra, fw * 4, 0);
                parts[i] = new GifCompositor.Frame(bgra, left, top, fw, fh, ReadInt(m, "/grctlext/Disposal"));
                int delay = ReadInt(m, "/grctlext/Delay"); // centiseconds
                delays[i] = delay <= 0 ? 100 : delay * 10; // browsers treat 0 as ~100ms
                w = Math.Max(w, left + fw);
                h = Math.Max(h, top + fh);
            }
            if (w <= 0 || h <= 0) return null;

            var frames = new List<BitmapSource>(count);
            foreach (var pixels in GifCompositor.Compose(w, h, parts))
            {
                var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
                bmp.Freeze();
                frames.Add(bmp);
            }
            return new Clip(frames, delays);
        }
        catch { return null; }
    }

    private static int ReadInt(BitmapMetadata? m, string query)
    {
        try { return m?.GetQuery(query) is { } v ? Convert.ToInt32(v) : 0; }
        catch { return 0; }
    }
}
