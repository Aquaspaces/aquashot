using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Aquashot.Annotation;
using Aquashot.Capture;
using Aquashot.Output;
using Aquashot.Selection;
using Xunit;

namespace Aquashot.Tests;

public class OutputServiceTests
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

    private static BitmapSource Blank(int w, int h)
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
    public void Compose_CropsRelativeToMonitorOrigin()
    {
        var outp = Sta(() =>
        {
            var frame = new CapturedFrame(
                new MonitorInfo("m", new PixelRect(100, 0, 200, 200), 1.0),
                Blank(200, 200));
            var doc = new AnnotationDocument();
            doc.Add(new RectShape(5, 5, 10, 10, "#FF0000", 2));
            // virtual crop at x=150 maps to local x=50 on a monitor whose origin is x=100
            return new OutputService().Compose(frame, new PixelRect(150, 20, 40, 30), doc);
        });
        outp.PixelWidth.Should().Be(40);
        outp.PixelHeight.Should().Be(30);
    }
}
