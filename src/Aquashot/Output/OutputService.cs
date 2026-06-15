using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media.Imaging;
using Aquashot.Annotation;
using Aquashot.Capture;
using Aquashot.Selection;
using Aquashot.Settings;
using Clipboard = System.Windows.Clipboard;

namespace Aquashot.Output;

public class OutputService
{
    private readonly AnnotationRenderer _renderer = new();

    public BitmapSource Compose(CapturedFrame frame, PixelRect cropVirtual, AnnotationDocument doc)
    {
        var localCrop = new PixelRect(
            cropVirtual.X - frame.Monitor.Bounds.X,
            cropVirtual.Y - frame.Monitor.Bounds.Y,
            cropVirtual.Width, cropVirtual.Height);
        return _renderer.Flatten(frame.Bitmap, localCrop, doc.Shapes);
    }

    public string Save(CapturedFrame frame, PixelRect cropVirtual, AnnotationDocument doc,
        AppSettings settings, DateTime now)
    {
        return SaveComposite(Compose(frame, cropVirtual, doc), settings, now);
    }

    public string SaveComposite(BitmapSource image, AppSettings settings, DateTime now)
    {
        if (image.CanFreeze && !image.IsFrozen) image.Freeze();
        SetClipboardImage(image);

        Directory.CreateDirectory(settings.SaveFolder);
        string file = Path.Combine(settings.SaveFolder,
            FilenameGenerator.Generate(settings.FilenamePattern, settings.ImageFormat, now));
        using var fs = File.Create(file);
        BitmapEncoder encoder = settings.ImageFormat.ToLowerInvariant() is "jpg" or "jpeg"
            ? new JpegBitmapEncoder()
            : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(fs);
        return file;
    }

    // The Windows clipboard can be transiently locked by another process; retry a few times.
    private static void SetClipboardImage(BitmapSource image)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { Clipboard.SetImage(image); return; }
            catch (ExternalException) when (attempt < 8) { Thread.Sleep(60); }
        }
    }
}
