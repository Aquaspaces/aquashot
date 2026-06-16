using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aquashot.History;

// Decodes a GIF into fully-composited frames + per-frame delays so a simple timer can play
// it back. Honours each frame's offset and disposal method (the common cases), so optimised
// GIFs (partial frames) render correctly, not just full-frame ones.
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

            // Logical screen size = the extent that contains every frame.
            int w = 0, h = 0;
            var meta = new (int left, int top, int delay, int disposal)[count];
            for (int i = 0; i < count; i++)
            {
                var m = decoder.Frames[i].Metadata as BitmapMetadata;
                int left = ReadInt(m, "/imgdesc/Left"), top = ReadInt(m, "/imgdesc/Top");
                int delay = ReadInt(m, "/grctlext/Delay");      // centiseconds
                int disposal = ReadInt(m, "/grctlext/Disposal");
                meta[i] = (left, top, delay, disposal);
                w = Math.Max(w, left + decoder.Frames[i].PixelWidth);
                h = Math.Max(h, top + decoder.Frames[i].PixelHeight);
            }
            if (w <= 0 || h <= 0) return null;

            var frames = new List<BitmapSource>(count);
            var delays = new List<int>(count);
            BitmapSource? canvas = null; // accumulated composite

            for (int i = 0; i < count; i++)
            {
                var (left, top, delay, disposal) = meta[i];
                var frame = decoder.Frames[i];

                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    if (canvas != null) dc.DrawImage(canvas, new Rect(0, 0, w, h));
                    dc.DrawImage(frame, new Rect(left, top, frame.PixelWidth, frame.PixelHeight));
                }
                var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(visual);
                rtb.Freeze();
                frames.Add(rtb);
                delays.Add(delay <= 0 ? 100 : delay * 10); // GIF browser floor ~100ms; centi->ms

                // Disposal 2 = restore to background: clear this frame's rect for the next composite.
                if (disposal == 2)
                {
                    var clearVisual = new DrawingVisual();
                    using (var dc = clearVisual.RenderOpen())
                    {
                        if (canvas != null) dc.DrawImage(canvas, new Rect(0, 0, w, h));
                        dc.PushClip(new RectangleGeometry(new Rect(left, top, frame.PixelWidth, frame.PixelHeight)));
                        dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(left, top, frame.PixelWidth, frame.PixelHeight));
                        dc.Pop();
                    }
                    var clr = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    clr.Render(clearVisual);
                    clr.Freeze();
                    canvas = clr;
                }
                else
                {
                    canvas = rtb; // disposal 0/1 (and 3 approximated): keep the composite
                }
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
