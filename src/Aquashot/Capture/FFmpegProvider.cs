using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace Aquashot.Capture;

// Extracts the bundled ffmpeg.exe to a cache dir, named by content hash so a new
// build's binary lands at a new path and stale ones are simply ignored.
public class FFmpegProvider
{
    private const string ResourceName = "Aquashot.Resources.ffmpeg.exe";
    private readonly Func<Stream> _open;
    private readonly string _cacheDir;

    public FFmpegProvider(Func<Stream> open, string cacheDir)
    {
        _open = open;
        _cacheDir = cacheDir;
    }

    // Production ctor: read from the embedded resource, cache under %LOCALAPPDATA%.
    public static FFmpegProvider Default()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Aquashot", "ffmpeg");
        return new FFmpegProvider(
            () => Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                  ?? throw new InvalidOperationException("Embedded ffmpeg.exe not found: " + ResourceName),
            dir);
    }

    public string EnsureExtracted()
    {
        Directory.CreateDirectory(_cacheDir);
        byte[] bytes;
        using (var s = _open()) { using var ms = new MemoryStream(); s.CopyTo(ms); bytes = ms.ToArray(); }

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).Substring(0, 12).ToLowerInvariant();
        var path = Path.Combine(_cacheDir, $"ffmpeg-{hash}.exe");
        if (File.Exists(path) && new FileInfo(path).Length == bytes.Length) return path;
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
