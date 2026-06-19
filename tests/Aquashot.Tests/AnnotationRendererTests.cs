using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Aquashot.Annotation;
using Aquashot.Selection;
using Xunit;

namespace Aquashot.Tests;

public class AnnotationRendererTests
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

    private static BitmapSource BlankBase() => SolidBase(Brushes.White);

    private static BitmapSource SolidBase(Brush fill)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawRectangle(fill, null, new Rect(0, 0, 200, 200));
        var rtb = new RenderTargetBitmap(200, 200, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }


    [Fact]
    public void Flatten_ProducesBitmapOfCropSize()
    {
        var outp = Sta(() =>
        {
            var renderer = new AnnotationRenderer();
            var shapes = new Shape[]
            {
                new RectShape(5, 5, 20, 20, "#FF0000", 3),
                new ArrowShape(0, 0, 30, 30, "#00FF00", 3),
                new CounterShape(25, 25, 1, "#FF3B30", 3),
            };
            return renderer.Flatten(BlankBase(), new PixelRect(10, 10, 50, 40), shapes);
        });
        outp.PixelWidth.Should().Be(50);
        outp.PixelHeight.Should().Be(40);
    }

    [Fact]
    public void Flatten_WithFilledShapes_ProducesCorrectSize()
    {
        var outp = Sta(() =>
        {
            var renderer = new AnnotationRenderer();
            var shapes = new Shape[]
            {
                new RectShape(2, 2, 20, 20, "#FF0000", 3, Filled: true),
                new EllipseShape(5, 5, 15, 15, "#00FF00", 3, Filled: true),
            };
            return renderer.Flatten(BlankBase(), new PixelRect(0, 0, 40, 40), shapes);
        });
        outp.PixelWidth.Should().Be(40);
        outp.PixelHeight.Should().Be(40);
    }

    [Fact]
    public void Draw_DoesNotThrow_ForHighlightAndPixelEffectShapes()
    {
        var act = () => Sta<object?>(() =>
        {
            var renderer = new AnnotationRenderer();
            var shapes = new Shape[]
            {
                new HighlightShape(new (double, double)[] { (1, 1), (30, 30) }, "#FFFF00", 18, 0.4),
                new BlurShape(2, 2, 20, 20, 6),
                new PixelateShape(4, 4, 16, 16, 8),
            };
            return renderer.Flatten(BlankBase(), new PixelRect(0, 0, 40, 40), shapes);
        });
        act.Should().NotThrow();
    }

    [Fact]
    public void Spotlight_DimsOutsideRect_KeepsCenterClear()
    {
        var outp = Sta(() =>
        {
            var renderer = new AnnotationRenderer();
            // A spotlight over the center of a 200x200 white image dims the corners but not the rect.
            var shapes = new Shape[] { new SpotlightShape(70, 70, 60, 60, "#A6000000") };
            return renderer.Flatten(BlankBase(), new PixelRect(0, 0, 200, 200), shapes);
        });

        var conv = new FormatConvertedBitmap(outp, PixelFormats.Bgra32, null, 0);
        byte CenterLuma = Luma(conv, 100, 100); // inside the spotlight rect -> still white-ish
        byte CornerLuma = Luma(conv, 5, 5);      // outside -> dimmed
        CenterLuma.Should().BeGreaterThan(CornerLuma);
    }

    private static byte Luma(BitmapSource src, int x, int y)
    {
        var one = new CroppedBitmap(src, new Int32Rect(x, y, 1, 1));
        var px = new byte[4];
        one.CopyPixels(px, 4, 0);
        return (byte)((px[2] + px[1] + px[0]) / 3); // BGRA -> rough luma
    }
}
