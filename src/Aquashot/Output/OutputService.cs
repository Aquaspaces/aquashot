using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        AppSettings settings, DateTime now, ClipboardMode clip = ClipboardMode.Image)
    {
        return SaveComposite(Compose(frame, cropVirtual, doc), settings, now, clip);
    }

    public string SaveComposite(BitmapSource image, AppSettings settings, DateTime now,
        ClipboardMode clip = ClipboardMode.Image)
    {
        if (image.CanFreeze && !image.IsFrozen) image.Freeze();

        Directory.CreateDirectory(settings.SaveFolder);
        string file = Path.Combine(settings.SaveFolder,
            FilenameGenerator.Generate(settings.FilenamePattern, settings.ImageFormat, now));
        File.WriteAllBytes(file, Encode(image, settings.ImageFormat));

        // Apply the clipboard action after the file is written so Path mode can copy the path.
        switch (clip)
        {
            case ClipboardMode.Image: SetClipboardImage(image); break;
            case ClipboardMode.File: CopyFileToClipboard(file); break;
            case ClipboardMode.Path: SetClipboardText(file); break;
            case ClipboardMode.None: break;
        }
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

    // Copy plain text (the saved file path) to the clipboard, with the same retry loop.
    private static void SetClipboardText(string text) => CopyPathToClipboard(text);

    // Copy a file path as plain text to the clipboard (reusable by recordings), with a retry loop.
    public static void CopyPathToClipboard(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { Clipboard.SetText(path); return; }
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

    // Like RecordingOutputBase but collision-proof: if any of {stem+ext} already exist for the
    // given exts, appends "-2", "-3", … to the stem until none collide. Returns the stem only.
    public static string UniqueRecordingOutputBase(AppSettings settings, DateTime now, params string[] exts)
    {
        var baseStem = RecordingOutputBase(settings, now);
        if (exts.Length == 0) return baseStem;

        bool Collides(string stem) =>
            exts.Any(ext => File.Exists(stem + "." + ext.TrimStart('.')));

        if (!Collides(baseStem)) return baseStem;
        for (int n = 2; ; n++)
        {
            var candidate = baseStem + "-" + n;
            if (!Collides(candidate)) return candidate;
        }
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
