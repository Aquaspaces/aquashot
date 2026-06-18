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

    [Fact]
    public void UniqueRecordingOutputBase_avoids_a_colliding_existing_file()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aqua-rec-" + System.Guid.NewGuid().ToString("N"));
        var settings = new Aquashot.Settings.AppSettings { SaveFolder = dir, FilenamePattern = "Clip_{yyyy}" };
        try
        {
            var baseStem = OutputService.RecordingOutputBase(settings, new System.DateTime(2026, 1, 2));
            System.IO.File.WriteAllText(baseStem + ".mp4", "x"); // pre-create the colliding file

            var unique = OutputService.UniqueRecordingOutputBase(
                settings, new System.DateTime(2026, 1, 2), ".mp4", ".gif");

            unique.Should().NotBe(baseStem);
            unique.Should().Be(baseStem + "-2");
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData(ClipboardMode.Image)]
    [InlineData(ClipboardMode.None)]
    [InlineData(ClipboardMode.Path)]
    public void SaveComposite_writes_a_decodable_png_for_every_clipboard_mode(ClipboardMode clip)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aqua-clip-" + System.Guid.NewGuid().ToString("N"));
        var settings = new Aquashot.Settings.AppSettings { SaveFolder = dir, FilenamePattern = "Shot_{yyyy}", ImageFormat = "png" };
        try
        {
            var path = Sta(() => new OutputService().SaveComposite(Blank(20, 12), settings, new System.DateTime(2026, 1, 2), clip));

            path.Should().EndWith("Shot_2026.png");
            System.IO.File.Exists(path).Should().BeTrue();
            // The written file is identical regardless of clipboard mode (write-then-copy).
            var bytes = System.IO.File.ReadAllBytes(path);
            var sig = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            bytes.Take(8).Should().Equal(sig);
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SaveComposite_path_mode_puts_the_saved_path_on_the_clipboard_as_text()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aqua-clip-" + System.Guid.NewGuid().ToString("N"));
        var settings = new Aquashot.Settings.AppSettings { SaveFolder = dir, FilenamePattern = "Shot_{yyyy}", ImageFormat = "png" };
        try
        {
            var (path, clipText) = Sta(() =>
            {
                var p = new OutputService().SaveComposite(Blank(20, 12), settings, new System.DateTime(2026, 1, 2), ClipboardMode.Path);
                return (p, System.Windows.Clipboard.GetText());
            });
            clipText.Should().Be(path);
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { }
        }
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
