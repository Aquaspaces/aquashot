using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Aquashot.Capture;

namespace Aquashot.Recording;

// One dshow audio capture device, flagged if it looks like a system-sound loopback.
public record AudioDevice(string Name, bool IsLoopback);

// Parses 'ffmpeg -list_devices true -f dshow -i dummy' stderr into audio device names, and
// (live) runs that probe via an IFFmpegRunner. Pure parsing so it's unit-testable.
public class AudioDeviceEnumerator
{
    // Names that signal a loopback / "what you hear" virtual device (Windows has no real
    // dshow loopback, so we detect Stereo Mix / virtual cables instead).
    private static readonly string[] LoopbackHints =
        { "loopback", "stereo mix", "what u hear", "what you hear", "voicemeeter out", "cable output" };

    // Quoted device name on a dshow log line tagged "(audio)". Newer ffmpeg also emits an
    // "Alternative name "..."" line per device — those carry no "(audio)" tag so are skipped.
    private static readonly Regex AudioLine =
        new("\"([^\"]+)\"\\s*\\(audio\\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // PURE: extract audio device names from the probe's stderr (dedup, in first-seen order).
    public static IReadOnlyList<AudioDevice> ParseAudioDevices(string stderr)
    {
        var list = new List<AudioDevice>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(stderr)) return list;

        foreach (Match m in AudioLine.Matches(stderr))
        {
            var name = m.Groups[1].Value.Trim();
            if (name.Length == 0 || !seen.Add(name)) continue;
            list.Add(new AudioDevice(name, IsLoopbackName(name)));
        }
        return list;
    }

    // PURE: first loopback-looking device, else null (no system-sound device available).
    public static string? DetectSystemLoopback(IReadOnlyList<AudioDevice> devices)
    {
        foreach (var d in devices)
            if (d.IsLoopback) return d.Name;
        return null;
    }

    private static bool IsLoopbackName(string name)
    {
        foreach (var h in LoopbackHints)
            if (name.Contains(h, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // Live probe: run ffmpeg's device list and parse the stderr tail. The probe exits non-zero
    // (it never opens "dummy"), so we ignore the exit code and just read what it printed.
    public async Task<IReadOnlyList<AudioDevice>> EnumerateAsync(IFFmpegRunner runner)
    {
        var r = await runner.RunAsync(FFmpegArgs.ListDshowDevices());
        return ParseAudioDevices(r.StderrTail);
    }
}
