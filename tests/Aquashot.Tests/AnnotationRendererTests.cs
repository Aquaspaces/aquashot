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

    private static BitmapSource BlankBase()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 200, 200));
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
    public void Flatten_WithBlurShape_StillProducesCorrectSize()
    {
        var outp = Sta(() =>
        {
            var renderer = new AnnotationRenderer();
            var shapes = new Shape[] { new BlurShape(2, 2, 40, 30, true, 1) };
            return renderer.Flatten(BlankBase(), new PixelRect(0, 0, 60, 50), shapes);
        });
        outp.PixelWidth.Should().Be(60);
        outp.PixelHeight.Should().Be(50);
    }
}
