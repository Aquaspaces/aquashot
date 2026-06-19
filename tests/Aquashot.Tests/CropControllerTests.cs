using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Aquashot.Annotation;
using Aquashot.Editor;
using Aquashot.Selection;
using Xunit;

namespace Aquashot.Tests;

public class CropControllerTests
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

    private static BitmapSource Base(int w, int h)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    [Fact]
    public void Clamp_KeepsRectInsideBounds()
    {
        var r = CropController.Clamp(100, 80, new PixelRect(-10, -5, 200, 200));
        r.X.Should().Be(0);
        r.Y.Should().Be(0);
        r.Width.Should().Be(100);
        r.Height.Should().Be(80);
    }

    [Fact]
    public void Clamp_ClampsWidthHeightToRemainingSpace()
    {
        var r = CropController.Clamp(100, 100, new PixelRect(90, 90, 50, 50));
        r.X.Should().Be(90);
        r.Y.Should().Be(90);
        r.Width.Should().Be(10);
        r.Height.Should().Be(10);
    }

    [Fact]
    public void Clamp_NeverProducesZeroSize()
    {
        var r = CropController.Clamp(100, 100, new PixelRect(50, 50, 0, 0));
        r.Width.Should().BeGreaterThanOrEqualTo(1);
        r.Height.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Apply_ProducesBitmapOfCropSize()
    {
        var outp = Sta(() => CropController.Apply(Base(100, 100), new PixelRect(10, 20, 40, 30)));
        outp.PixelWidth.Should().Be(40);
        outp.PixelHeight.Should().Be(30);
    }

    [Fact]
    public void TranslateShapes_MovesShapesByOffset()
    {
        var shapes = new Shape[] { new RectShape(30, 40, 10, 10, "#FFFFFF", 2) };
        var moved = CropController.TranslateShapes(shapes, -30, -40);
        var r = (RectShape)moved[0];
        r.X.Should().Be(0);
        r.Y.Should().Be(0);
    }

    [Fact]
    public void CropThenTranslate_KeepsShapeVisuallyInPlace()
    {
        // A rect at image (35,35) inside a crop starting at (30,30) should land at crop-local (5,5).
        var crop = new PixelRect(30, 30, 50, 50);
        var clamped = CropController.Clamp(200, 200, crop);
        var shapes = new Shape[] { new RectShape(35, 35, 10, 10, "#FFFFFF", 2) };
        var moved = CropController.TranslateShapes(shapes, -clamped.X, -clamped.Y);
        var r = (RectShape)moved[0];
        r.X.Should().Be(5);
        r.Y.Should().Be(5);
    }
}
