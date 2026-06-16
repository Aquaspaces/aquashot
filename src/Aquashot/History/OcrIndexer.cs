using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Aquashot.History;

// Coordinates OCR work against the library. Serializes OCR so we never spawn unbounded
// background tasks. Enabled/disabled by the caller (settings gate).
public class OcrIndexer
{
    private static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".bmp" };
    private readonly CaptureLibrary _lib;
    private readonly IOcrService _ocr;
    private Task _tail = Task.CompletedTask;
    private readonly object _gate = new();

    public OcrIndexer(CaptureLibrary lib, IOcrService ocr) { _lib = lib; _ocr = ocr; }

    // Queue OCR for one file; returns a task that completes when this item is done.
    public Task EnqueueAsync(string path)
    {
        lock (_gate)
        {
            _tail = _tail.ContinueWith(async _ =>
            {
                var text = await _ocr.RecognizeAsync(path);
                _lib.SetOcr(path, text);
            }).Unwrap();
            return _tail;
        }
    }

    // Add any un-indexed images from the folder, then OCR every entry still pending.
    public async Task BackfillAsync(string saveFolder)
    {
        try
        {
            if (Directory.Exists(saveFolder))
            {
                var known = _lib.Entries.Select(e => e.Path)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.EnumerateFiles(saveFolder)
                             .Where(f => ImageExt.Contains(Path.GetExtension(f).ToLowerInvariant()))
                             .Where(f => !known.Contains(f)))
                    _lib.Add(file, File.GetLastWriteTime(file));
            }
        }
        catch { /* best-effort scan */ }

        foreach (var entry in _lib.Entries.Where(e => !e.OcrDone).ToList())
            await EnqueueAsync(entry.Path);
    }
}
