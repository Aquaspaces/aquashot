using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aquashot.Capture;

namespace Aquashot.Recording;

public class HardwareEncoderDetector
{
    private readonly IFFmpegRunner _runner;
    private EncoderCandidate? _cached;

    public HardwareEncoderDetector(IFFmpegRunner runner) => _runner = runner;

    // Pure: which ladder names appear in `ffmpeg -encoders` output.
    public static HashSet<string> ParseAvailable(string encodersOutput, IReadOnlyList<EncoderCandidate> ladder)
    {
        var known = new HashSet<string>();
        foreach (var c in ladder) known.Add(c.Name);
        var found = new HashSet<string>();
        foreach (var raw in encodersOutput.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || !line.StartsWith("V")) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && known.Contains(parts[1])) found.Add(parts[1]);
        }
        return found;
    }

    // Pure: first ladder entry that is available AND probes ok.
    public static EncoderCandidate? Pick(
        IReadOnlyList<EncoderCandidate> ladder, ISet<string> available, Func<string, bool> probe)
    {
        foreach (var c in ladder)
            if (available.Contains(c.Name) && probe(c.Name)) return c;
        return null;
    }

    // Live: run `-encoders`, then probe candidates with a real 5-frame test encode. Cached.
    public async Task<EncoderCandidate?> DetectAsync(string? overrideName)
    {
        if (_cached != null) return _cached;

        if (!string.IsNullOrWhiteSpace(overrideName))
        {
            if ((await _runner.RunAsync(FFmpegArgs.EncodeProbe(overrideName!))).Ok)
                return _cached = new EncoderCandidate(overrideName!, VideoCodec.H264, true);
        }

        var enc = await _runner.RunAsync(new[] { "-hide_banner", "-encoders" });
        var available = ParseAvailable(enc.StderrTail, EncoderLadder.Default);
        // ffmpeg writes -encoders to stdout; FFmpegRunner captures both stdout+stderr into StderrTail.

        foreach (var c in EncoderLadder.Default)
        {
            if (!available.Contains(c.Name)) continue;
            if ((await _runner.RunAsync(FFmpegArgs.EncodeProbe(c.Name))).Ok)
                return _cached = c;
        }
        return null;
    }
}
