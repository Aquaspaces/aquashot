using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aquashot.Annotation;
using Aquashot.History;
using Aquashot.Output;
using Aquashot.Selection;
using Aquashot.Settings;
using FluentAssertions;
using Xunit;

namespace Aquashot.Tests;

public class CapturePipelineTests
{
    private sealed class FakeOcr : IOcrService
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public IReadOnlyList<OcrLine> Lines { get; init; } = Array.Empty<OcrLine>();

        public Task<string> RecognizeAsync(string imagePath) => Task.FromResult("");

        public Task<IReadOnlyList<OcrLine>> RecognizeLinesAsync(string imagePath)
        {
            Calls++;
            File.Exists(imagePath).Should().BeTrue("OCR reads the unannotated crop from a temp file");
            if (Fail) throw new InvalidOperationException("no OCR language installed");
            return Task.FromResult(Lines);
        }
    }

    private static T Sta<T>(Func<T> f)
    {
        T result = default!;
        Exception? error = null;
        var t = new Thread(() => { try { result = f(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw error;
        return result;
    }

    // Left half black, right half white, so a blur across the middle produces grey.
    private static BitmapSource Split(int w, int h)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w / 2.0, h));
            dc.DrawRectangle(Brushes.White, null, new Rect(w / 2.0, 0, w / 2.0, h));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private static byte[] PixelAt(string path, int x, int y)
    {
        var frame = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var px = new byte[4];
        bgra.CopyPixels(new Int32Rect(x, y, 1, 1), px, 4, 0);
        return px;
    }

    private static string TempFolder() =>
        Path.Combine(Path.GetTempPath(), "aqua-pipeline-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Whole_image_captures_are_auto_redacted_too()
    {
        // Regression: all-monitor, repeat, delayed, and scrolling captures used to skip auto-redact.
        var folder = TempFolder();
        var ocr = new FakeOcr { Lines = new[] { new OcrLine("secret", new Rect(30, 10, 40, 20)) } };
        var settings = new AppSettings
        {
            SaveFolder = folder, AutoRedactEnabled = true, RedactStyle = "Blur", RedactBlurRadius = 8,
            RedactPatterns = "secret",
            EnableOcr = false, // indexing off must not turn redaction off
        };
        try
        {
            var path = Sta(() => new CapturePipeline(new OutputService(), ocr)
                .SaveAsync(CaptureJob.Whole(Split(100, 40), ClipboardMode.None), settings, DateTime.Now)
                .GetAwaiter().GetResult());

            ocr.Calls.Should().Be(1);
            var mid = PixelAt(path, 50, 20);
            mid[2].Should().BeInRange(10, 245, "the text box straddling black/white was blurred");
            PixelAt(path, 5, 35)[2].Should().Be(0, "pixels outside the text box are untouched");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void Failed_auto_redaction_saves_nothing()
    {
        var folder = TempFolder();
        var settings = new AppSettings { SaveFolder = folder, AutoRedactEnabled = true };
        try
        {
            var act = () => Sta(() => new CapturePipeline(new OutputService(), new FakeOcr { Fail = true })
                .SaveAsync(CaptureJob.Whole(Split(20, 20), ClipboardMode.None), settings, DateTime.Now)
                .GetAwaiter().GetResult());

            act.Should().Throw<AutoRedactException>().WithMessage("*not saved*no OCR language*");
            (Directory.Exists(folder) ? Directory.GetFiles(folder) : Array.Empty<string>()).Should().BeEmpty();
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void Redaction_off_never_runs_ocr_and_keeps_annotations()
    {
        var folder = TempFolder();
        var ocr = new FakeOcr();
        var settings = new AppSettings { SaveFolder = folder, AutoRedactEnabled = false };
        try
        {
            var path = Sta(() =>
            {
                var source = Split(100, 40);
                var job = new CaptureJob(source, new PixelRect(10, 5, 60, 30),
                    new Shape[] { new RectShape(0, 0, 60, 30, "#FF0000", 4, Filled: true) }, ClipboardMode.None);
                return new CapturePipeline(new OutputService(), ocr)
                    .SaveAsync(job, settings, DateTime.Now).GetAwaiter().GetResult();
            });

            ocr.Calls.Should().Be(0);
            var size = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            size.PixelWidth.Should().Be(60);
            size.PixelHeight.Should().Be(30);
            var px = PixelAt(path, 30, 15);
            (px[2], px[1], px[0]).Should().Be(((byte)255, (byte)0, (byte)0), "the filled rect annotation was flattened in");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void Repeated_saves_in_the_same_second_never_overwrite()
    {
        var folder = TempFolder();
        var settings = new AppSettings { SaveFolder = folder };
        var now = new DateTime(2026, 10, 10, 12, 0, 0);
        try
        {
            var paths = Sta(() =>
            {
                var pipeline = new CapturePipeline(new OutputService(), new FakeOcr());
                return Enumerable.Range(0, 3).Select(_ => pipeline
                    .SaveAsync(CaptureJob.Whole(Split(10, 10), ClipboardMode.None), settings, now)
                    .GetAwaiter().GetResult()).ToList();
            });

            paths.Should().OnlyHaveUniqueItems();
            Directory.GetFiles(folder).Should().HaveCount(3);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
