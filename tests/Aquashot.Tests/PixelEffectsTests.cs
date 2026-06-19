using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Aquashot.Redaction;
using Xunit;

namespace Aquashot.Tests;

public class PixelEffectsTests
{
    private static T Sta<T>(System.Func<T> f)
    {
        T result = default!;
        var t = new Thread(() => result = f());
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        return result;
    }

    // A checkerboard so pixelation has something to collapse.
    private static BitmapSource Checker(int w, int h)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            for (int y = 0; y < h; y += 4)
                for (int x = 0; x < w; x += 4)
                    dc.DrawRectangle((x + y) % 8 == 0 ? Brushes.Black : Brushes.White, null, new Rect(x, y, 4, 4));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    [Fact]
    public void Pixelate_PreservesPixelSize()
    {
        var outp = Sta(() => PixelEffects.Pixelate(Checker(40, 24), 8));
        outp.PixelWidth.Should().Be(40);
        outp.PixelHeight.Should().Be(24);
    }

    [Fact]
    public void Blur_PreservesPixelSize()
    {
        var outp = Sta(() => PixelEffects.Blur(Checker(40, 24), 6));
        outp.PixelWidth.Should().Be(40);
        outp.PixelHeight.Should().Be(24);
    }

    [Fact]
    public void Pixelate_ReducesDistinctColors()
    {
        // Mosaicing a fine checkerboard collapses many distinct colours into far fewer blocks.
        var (rawCount, pixCount) = Sta(() =>
        {
            var src = Checker(32, 32);
            var px = PixelEffects.Pixelate(src, 16);
            return (Distinct(src), Distinct(px));
        });
        pixCount.Should().BeLessThan(rawCount);
    }

    private static int Distinct(BitmapSource src)
    {
        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight;
        var buf = new byte[w * h * 4];
        conv.CopyPixels(buf, w * 4, 0);
        var set = new HashSet<int>();
        for (int i = 0; i < buf.Length; i += 4)
            set.Add(buf[i] | buf[i + 1] << 8 | buf[i + 2] << 16);
        return set.Count;
    }
}
