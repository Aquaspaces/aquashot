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
        File.WriteAllBytes(file, Encode(image, settings.ImageFormat));
        return file;
    }

    // Encodes to PNG/JPEG with no carried-over metadata (null thumbnail/metadata/colour
    // contexts), then strips any metadata segments the encoder still emits, so saved files
    // leak no text/timestamp/EXIF data.
    public byte[] Encode(BitmapSource image, string format)
    {
        BitmapEncoder encoder = format.ToLowerInvariant() is "jpg" or "jpeg"
            ? new JpegBitmapEncoder()
            : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image, null, null, null));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return MetadataStripper.Strip(ms.ToArray());
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

    // Folder + filename stem (no extension) for a recording, e.g. ...\Screenshots\Clip_2026
    public static string RecordingOutputBase(AppSettings settings, DateTime now)
    {
        Directory.CreateDirectory(settings.SaveFolder);
        // Reuse the screenshot stem generator but strip the placeholder extension it appends.
        var name = FilenameGenerator.Generate(settings.FilenamePattern, "tmp", now);
        if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return Path.Combine(settings.SaveFolder, name);
    }

    // Put the produced file on the clipboard as a file-drop (paste into Discord/Explorer).
    public static void CopyFileToClipboard(string path)
    {
        var paths = new System.Collections.Specialized.StringCollection { path };
        for (int attempt = 0; ; attempt++)
        {
            try { Clipboard.SetFileDropList(paths); return; }
            catch (ExternalException) when (attempt < 8) { Thread.Sleep(60); }
        }
    }
}
