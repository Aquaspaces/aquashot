using System.Collections.Generic;
using System.Globalization;
using Aquashot.Selection;

namespace Aquashot.Recording;

// Which audio sources to mux. Names are EXACT dshow device names ("" => none for that slot).
public record AudioSpec(bool Mic, string? MicDevice, bool System, string? SystemDevice, int BitrateKbps = 160)
{
    public bool Any => (Mic && !string.IsNullOrEmpty(MicDevice)) || (System && !string.IsNullOrEmpty(SystemDevice));
    public static readonly AudioSpec None = new(false, null, false, null);
}

// Pure builders. Each returns an ArgumentList for ProcessStartInfo (no manual quoting).
public static class FFmpegArgs
{
    private static string I(double v) => ((int)System.Math.Round(v)).ToString(CultureInfo.InvariantCulture);

    // yuv420p / NVENC / QSV require even dimensions — round down to even (min 2).
    private static string Even(double v)
    {
        int n = (int)System.Math.Round(v);
        if ((n & 1) == 1) n--;
        return System.Math.Max(2, n).ToString(CultureInfo.InvariantCulture);
    }

    public static List<string> CaptureGdigrab(PixelRect region, int fps, string encoder, string outPath) =>
        CaptureGdigrab(region, fps, encoder, outPath, AudioSpec.None);

    // gdigrab video + 0..2 dshow audio inputs, AAC, with explicit stream maps. Two audio sources
    // are amixed into one stereo track. No audio -> identical args to the silent overload.
    public static List<string> CaptureGdigrab(PixelRect region, int fps, string encoder, string outPath, AudioSpec audio)
    {
        var a = new List<string>
        {
            "-y", "-f", "gdigrab", "-framerate", fps.ToString(), "-draw_mouse", "1",
            "-offset_x", I(region.X), "-offset_y", I(region.Y),
            "-video_size", $"{Even(region.Width)}x{Even(region.Height)}", "-i", "desktop",
        };
        int audioInputs = 0;
        if (audio.Mic && !string.IsNullOrEmpty(audio.MicDevice))
        { a.AddRange(new[] { "-f", "dshow", "-i", $"audio={audio.MicDevice}" }); audioInputs++; }
        if (audio.System && !string.IsNullOrEmpty(audio.SystemDevice))
        { a.AddRange(new[] { "-f", "dshow", "-i", $"audio={audio.SystemDevice}" }); audioInputs++; }

        a.AddRange(new[] { "-c:v", encoder, "-pix_fmt", "yuv420p" });
        if (audioInputs == 1)
            a.AddRange(new[] { "-map", "0:v", "-map", "1:a", "-c:a", "aac", "-b:a", $"{audio.BitrateKbps}k" });
        else if (audioInputs == 2)
            a.AddRange(new[]
            {
                "-filter_complex", "[1:a][2:a]amix=inputs=2:duration=longest[aout]",
                "-map", "0:v", "-map", "[aout]", "-c:a", "aac", "-b:a", $"{audio.BitrateKbps}k"
            });
        a.Add(outPath);
        return a;
    }

    // dshow device probe — prints available capture devices on stderr, then exits non-zero.
    public static List<string> ListDshowDevices() => new()
    {
        "-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"
    };

    // ddagrab is a lavfi source; crop the captured output to the region.
    public static List<string> CaptureDdagrab(PixelRect region, int fps, string encoder, string outPath) => new()
    {
        "-y", "-f", "lavfi", "-i",
        $"ddagrab=framerate={fps}:output_idx=0,crop={I(region.Width)}:{I(region.Height)}:{I(region.X)}:{I(region.Y)},hwdownload,format=bgra,format=yuv420p",
        "-c:v", encoder, "-pix_fmt", "yuv420p", outPath
    };

    // Optional overlayPng burns a transparent annotation image over the video (input 1).
    public static List<string> GifPalettegen(string input, int fps, int width, string palettePath,
        string? overlayPng = null, int colors = 256)
    {
        string scale = $"fps={fps},scale={width}:-1:flags=lanczos,palettegen=stats_mode=diff:max_colors={colors}";
        if (overlayPng == null)
            return new() { "-y", "-i", input, "-vf", scale, palettePath };
        return new()
        {
            "-y", "-i", input, "-i", overlayPng,
            "-filter_complex", $"[0:v][1:v]overlay=0:0,{scale}", palettePath
        };
    }

    public static List<string> GifPaletteuse(string input, string palettePath, int fps, int width, string outPath,
        string? overlayPng = null, string dither = "sierra2_4a")
    {
        if (overlayPng == null)
            return new()
            {
                "-y", "-i", input, "-i", palettePath,
                "-lavfi", $"fps={fps},scale={width}:-1:flags=lanczos[x];[x][1:v]paletteuse=dither={dither}",
                outPath
            };
        // inputs: 0=video, 1=overlay, 2=palette
        return new()
        {
            "-y", "-i", input, "-i", overlayPng, "-i", palettePath,
            "-filter_complex",
            $"[0:v][1:v]overlay=0:0,fps={fps},scale={width}:-1:flags=lanczos[x];[x][2:v]paletteuse=dither={dither}",
            outPath
        };
    }

    // maxFileSizeMb caps the output via ffmpeg -fs; pass null to omit the hard size cap (unlimited).
    // includeAudio carries the intermediate's audio track through the re-encode (copied as AAC);
    // when overlaying an annotation PNG the explicit video map keeps input 0's optional audio.
    public static List<string> Mp4Transcode(string input, string encoder, int bitrateKbps, string outPath,
        string? overlayPng = null, int? maxFileSizeMb = 49, bool includeAudio = false)
    {
        var args = new List<string> { "-y", "-i", input };
        if (overlayPng == null)
        {
            args.Add("-c:v"); args.Add(encoder);
            args.AddRange(new[] { "-pix_fmt", "yuv420p" });
        }
        else
        {
            args.AddRange(new[] { "-i", overlayPng, "-filter_complex", "[0:v][1:v]overlay=0:0,format=yuv420p[outv]" });
            args.AddRange(new[] { "-map", "[outv]" });
            args.Add("-c:v"); args.Add(encoder);
        }
        if (includeAudio)
        {
            // 0:a? — optional, so a silent intermediate doesn't fail the map.
            args.AddRange(new[] { "-map", "0:a?", "-c:a", "aac" });
        }
        args.AddRange(new[]
        {
            "-b:v", $"{bitrateKbps}k", "-maxrate", $"{bitrateKbps * 3 / 2}k", "-bufsize", $"{bitrateKbps * 2}k",
        });
        if (maxFileSizeMb is int mb) { args.Add("-fs"); args.Add($"{mb}M"); }
        args.AddRange(new[] { "-movflags", "+faststart", outPath });
        return args;
    }

    public static List<string> EncodeProbe(string encoder) => new()
    {
        "-hide_banner", "-f", "lavfi", "-i", "color=c=black:s=128x128:d=0.2",
        "-frames:v", "5", "-c:v", encoder, "-f", "null", "-"
    };

    // ---- Pause/resume (drop paused spans) + trim-after-stop ----

    private static string S(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    // Build a filtergraph that concatenates ONLY the kept [start,end] spans (paused gaps dropped).
    // Video only when includeAudio is false; with audio each span is also atrim'd and the two are
    // concat'd with a=1. Output labels are [outv] (and [outa] when includeAudio). Caller maps them.
    public static string BuildKeepFilter(IReadOnlyList<(double Start, double End)> keep, bool includeAudio = false)
    {
        var sb = new System.Text.StringBuilder();
        int n = keep.Count;
        // Per-span trims: video always, audio too when requested.
        for (int i = 0; i < n; i++)
        {
            var (st, en) = keep[i];
            sb.Append($"[0:v]trim=start={S(st)}:end={S(en)},setpts=PTS-STARTPTS[v{i}];");
            if (includeAudio)
                sb.Append($"[0:a]atrim=start={S(st)}:end={S(en)},asetpts=PTS-STARTPTS[a{i}];");
        }
        // concat consumes labels in segment order: [v0][v1]… for video-only, or interleaved
        // [v0][a0][v1][a1]… when both streams are present.
        for (int i = 0; i < n; i++)
        {
            sb.Append($"[v{i}]");
            if (includeAudio) sb.Append($"[a{i}]");
        }
        sb.Append(includeAudio
            ? $"concat=n={n}:v=1:a=1[outv][outa]"
            : $"concat=n={n}:v=1:a=0[outv]");
        return sb.ToString();
    }

    // Re-encode the intermediate keeping only the given spans (pause-drop / trim). When overlayPng
    // is supplied it's overlaid onto the concatenated video. includeAudio carries the audio track.
    // This produces a SELF-CONTAINED clip; the size-cap re-encode (RecordingEncoder) runs after.
    public static List<string> Mp4ConcatKept(string input, string encoder, int bitrateKbps, string outPath,
        IReadOnlyList<(double Start, double End)> keep, string? overlayPng = null,
        int? maxFileSizeMb = null, bool includeAudio = false)
    {
        var args = new List<string> { "-y", "-i", input };
        if (overlayPng != null) args.AddRange(new[] { "-i", overlayPng });

        string keepFilter = BuildKeepFilter(keep, includeAudio);
        string videoLabel;
        if (overlayPng != null)
        {
            // Concat first, then overlay the annotation PNG (input 1) onto the joined video.
            args.AddRange(new[] { "-filter_complex", keepFilter + ";[outv][1:v]overlay=0:0,format=yuv420p[finalv]" });
            videoLabel = "[finalv]";
        }
        else
        {
            args.AddRange(new[] { "-filter_complex", keepFilter });
            videoLabel = "[outv]";
        }

        args.AddRange(new[] { "-map", videoLabel });
        if (includeAudio) args.AddRange(new[] { "-map", "[outa]", "-c:a", "aac", "-b:a", "160k" });
        args.Add("-c:v"); args.Add(encoder);
        if (overlayPng == null) args.AddRange(new[] { "-pix_fmt", "yuv420p" });
        args.AddRange(new[]
        {
            "-b:v", $"{bitrateKbps}k", "-maxrate", $"{bitrateKbps * 3 / 2}k", "-bufsize", $"{bitrateKbps * 2}k",
        });
        if (maxFileSizeMb is int mb) { args.Add("-fs"); args.Add($"{mb}M"); }
        args.AddRange(new[] { "-movflags", "+faststart", outPath });
        return args;
    }

    // Trim-after-stop is the single-kept-span case of Mp4ConcatKept.
    public static List<string> Mp4Trim(string input, string encoder, int bitrateKbps, string outPath,
        double start, double end, string? overlayPng = null, int? maxFileSizeMb = null, bool includeAudio = false) =>
        Mp4ConcatKept(input, encoder, bitrateKbps, outPath,
            new[] { (start, end) }, overlayPng, maxFileSizeMb, includeAudio);
}
