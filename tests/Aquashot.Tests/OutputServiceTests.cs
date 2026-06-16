using System.Linq;
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

    [Fact]
    public void Encode_ProducesDecodablePngWithoutMetadataChunks()
    {
        var (bytes, decodedW, decodedH) = Sta(() =>
        {
            var data = new OutputService().Encode(Blank(40, 30), "png");
            using var ms = new System.IO.MemoryStream(data);
            var dec = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var f = dec.Frames[0];
            return (data, f.PixelWidth, f.PixelHeight);
        });

        decodedW.Should().Be(40);
        decodedH.Should().Be(30);
        var sig = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        bytes.Take(8).Should().Equal(sig);
        ContainsAscii(bytes, "tEXt").Should().BeFalse();
        ContainsAscii(bytes, "iTXt").Should().BeFalse();
        ContainsAscii(bytes, "tIME").Should().BeFalse();
    }

    [Fact]
    public void RecordingOutputPath_uses_pattern_and_extension()
    {
        var settings = new Aquashot.Settings.AppSettings { FilenamePattern = "Clip_{yyyy}" };
        var path = OutputService.RecordingOutputBase(settings, new System.DateTime(2026, 1, 2));
        path.Should().EndWith("Clip_2026");
        System.IO.Path.IsPathRooted(path).Should().BeTrue();
    }

    private static bool ContainsAscii(byte[] hay, string ascii)
    {
        var needle = System.Text.Encoding.ASCII.GetBytes(ascii);
        for (int i = 0; i + needle.Length <= hay.Length; i++)
        {
            bool ok = true;
            for (int j = 0; j < needle.Length; j++)
                if (hay[i + j] != needle[j]) { ok = false; break; }
            if (ok) return true;
        }
        return false;
    }
}
