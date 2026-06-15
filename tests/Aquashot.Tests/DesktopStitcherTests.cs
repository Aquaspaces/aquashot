using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using Aquashot.Capture;
using Aquashot.Selection;
using Xunit;

namespace Aquashot.Tests;

public class DesktopStitcherTests
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

    private static BitmapSource Blank(int w, int h, System.Windows.Media.Color c)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            dc.DrawRectangle(new SolidColorBrush(c), null, new Rect(0, 0, w, h));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    [Fact]
    public void Stitch_CoversUnionOfMonitors()
    {
        var outp = Sta(() =>
        {
            var a = new CapturedFrame(new MonitorInfo("a", new PixelRect(0, 0, 100, 80), 1.0), Blank(100, 80, Colors.Red));
            var b = new CapturedFrame(new MonitorInfo("b", new PixelRect(100, 0, 120, 90), 1.0), Blank(120, 90, Colors.Blue));
            var vbounds = new VirtualDesktop(new[] { a.Monitor, b.Monitor }).Bounds;
            return DesktopStitcher.Stitch(new[] { a, b }, vbounds);
        });
        outp.PixelWidth.Should().Be(220);
        outp.PixelHeight.Should().Be(90);
    }
}
