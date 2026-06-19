using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Aquashot.History;

// Decodes + caches frozen thumbnail BitmapImages off the UI thread, keyed by path + mtime + decode
// width. Keystroke filtering and the filmstrip reuse already-decoded images so a search never
// re-reads the disk for a tile it has already seen.
public sealed class ThumbnailCache
{
    private readonly ConcurrentDictionary<string, BitmapImage> _cache = new();

    private static string Key(string path, long mtime, int decodeWidth) => $"{path}|{mtime}|{decodeWidth}";

    // Synchronous cache hit only (no decode). Returns null if not yet decoded.
    public BitmapImage? TryGet(string path, int decodeWidth)
    {
        try
        {
            var mtime = File.GetLastWriteTimeUtc(path).Ticks;
            return _cache.TryGetValue(Key(path, mtime, decodeWidth), out var bi) ? bi : null;
        }
        catch { return null; }
    }

    // Decode (or reuse) a frozen thumbnail off the UI thread. DecodePixelWidth keeps it cheap.
    public Task<BitmapImage?> GetAsync(string path, int decodeWidth)
    {
        long mtime;
        try { mtime = File.GetLastWriteTimeUtc(path).Ticks; }
        catch { return Task.FromResult<BitmapImage?>(null); }

        var key = Key(path, mtime, decodeWidth);
        if (_cache.TryGetValue(key, out var hit)) return Task.FromResult<BitmapImage?>(hit);

        return Task.Run(() =>
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(path);
                bi.DecodePixelWidth = decodeWidth;
                bi.CacheOption = BitmapCacheOption.OnLoad; // read fully so a later Delete won't fail
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.EndInit();
                bi.Freeze();
                _cache[key] = bi;
                return (BitmapImage?)bi;
            }
            catch { return null; }
        });
    }
}
