using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Aquashot.Annotation;
using Aquashot.History;
using Aquashot.Redaction;
using Aquashot.Selection;
using Aquashot.Settings;

namespace Aquashot.Output;

// A finished still capture: base pixels, the crop within them (pixels of Source), the user's
// annotations in crop-local coordinates, and how to deliver it.
public sealed record CaptureJob(
    BitmapSource Source,
    PixelRect Crop,
    IReadOnlyList<Shape> Shapes,
    ClipboardMode Clip,
    string? WindowTitle = null,
    string? AppName = null)
{
    // A whole image with no annotations (all monitors, scrolling, repeat, delayed).
    public static CaptureJob Whole(BitmapSource image, ClipboardMode clip,
        string? windowTitle = null, string? appName = null) =>
        new(image, new PixelRect(0, 0, image.PixelWidth, image.PixelHeight), Array.Empty<Shape>(),
            clip, windowTitle, appName);
}

// Raised when automatic redaction was required but could not run. Nothing is saved, because
// saving unredacted text the user asked to hide is worse than losing the capture.
public sealed class AutoRedactException : Exception
{
    public AutoRedactException(Exception inner)
        : base("Automatic redaction failed, so the capture was not saved: " + inner.Message, inner) { }
}

// The one path every still capture takes: automatic redaction, flatten, write, clipboard.
// Region, window, monitor, all-monitor, repeat, delayed, and scrolling captures all use it, so a
// setting like auto-redact cannot be skipped by one capture mode. Call from the UI thread.
public sealed class CapturePipeline
{
    private readonly OutputService _output;
    private readonly IOcrService _ocr;
    private readonly AnnotationRenderer _renderer = new();

    public CapturePipeline(OutputService output, IOcrService ocr)
    {
        _output = output;
        _ocr = ocr;
    }

    public async Task<string> SaveAsync(CaptureJob job, AppSettings settings, DateTime now)
    {
        IReadOnlyList<Shape> shapes = job.Shapes;
        if (settings.AutoRedactEnabled)
        {
            IReadOnlyList<Shape> redactions;
            try { redactions = await RedactionShapesAsync(job.Source, job.Crop, settings); }
            catch (Exception ex) { throw new AutoRedactException(ex); }
            if (redactions.Count > 0) shapes = shapes.Concat(redactions).ToList();
        }

        // A whole, unannotated image (e.g. a tall scrolling capture) is saved as-is, skipping a
        // full re-render.
        bool whole = shapes.Count == 0 && job.Crop.X == 0 && job.Crop.Y == 0 &&
                     (int)job.Crop.Width == job.Source.PixelWidth && (int)job.Crop.Height == job.Source.PixelHeight;
        var image = whole ? job.Source : _renderer.Flatten(job.Source, job.Crop, shapes);
        if (image.CanFreeze && !image.IsFrozen) image.Freeze();

        // Encoding a large PNG takes long enough to stall the UI; the frozen bitmap can cross threads.
        var file = await Task.Run(() =>
            _output.WriteUnique(image, settings, now, job.WindowTitle, job.AppName));
        _output.ApplyClipboard(image, file, job.Clip); // clipboard needs the UI (STA) thread
        return file;
    }

    // OCR the unannotated crop and return blur/pixelate shapes over the lines the settings select.
    // Throws when OCR fails, so callers never mistake a failed scan for an image without text.
    public async Task<IReadOnlyList<Shape>> RedactionShapesAsync(BitmapSource source, PixelRect crop,
        AppSettings settings)
    {
        var baseCrop = _renderer.Flatten(source, crop, Array.Empty<Shape>());
        if (baseCrop.CanFreeze && !baseCrop.IsFrozen) baseCrop.Freeze();
        string temp = Path.Combine(Path.GetTempPath(), "aqua-redact-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(temp, _output.Encode(baseCrop, "png"));
            var lines = await _ocr.RecognizeLinesAsync(temp);
            var chosen = AutoRedactor.SelectLines(lines, settings.RedactPatterns);
            return AutoRedactor.BuildShapes(chosen, 0, 0,
                settings.RedactStyle, settings.RedactBlurRadius, settings.RedactPixelateBlock);
        }
        finally { try { File.Delete(temp); } catch { /* temp cleanup is best-effort */ } }
    }
}
