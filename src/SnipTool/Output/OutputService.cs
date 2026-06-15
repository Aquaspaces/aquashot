using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
using SnipTool.Annotation;
using SnipTool.Capture;
using SnipTool.Selection;
using SnipTool.Settings;
using Clipboard = System.Windows.Clipboard;

namespace SnipTool.Output;

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
        var final = Compose(frame, cropVirtual, doc);
        Clipboard.SetImage(final);
        Directory.CreateDirectory(settings.SaveFolder);
        string file = Path.Combine(settings.SaveFolder,
            FilenameGenerator.Generate(settings.FilenamePattern, settings.ImageFormat, now));
        using var fs = File.Create(file);
        BitmapEncoder encoder = settings.ImageFormat.ToLowerInvariant() is "jpg" or "jpeg"
            ? new JpegBitmapEncoder()
            : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(final));
        encoder.Save(fs);
        return file;
    }
}
