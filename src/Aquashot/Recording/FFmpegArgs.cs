using System.Collections.Generic;
using System.Globalization;
using Aquashot.Selection;

namespace Aquashot.Recording;

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

    public static List<string> CaptureGdigrab(PixelRect region, int fps, string encoder, string outPath) => new()
    {
        "-y", "-f", "gdigrab", "-framerate", fps.ToString(), "-draw_mouse", "1",
        "-offset_x", I(region.X), "-offset_y", I(region.Y),
        "-video_size", $"{Even(region.Width)}x{Even(region.Height)}", "-i", "desktop",
        "-c:v", encoder, "-pix_fmt", "yuv420p", outPath
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
    public static List<string> Mp4Transcode(string input, string encoder, int bitrateKbps, string outPath,
        string? overlayPng = null, int? maxFileSizeMb = 49)
    {
        var args = new List<string> { "-y", "-i", input };
        if (overlayPng == null)
        {
            args.Add("-c:v"); args.Add(encoder);
            args.AddRange(new[] { "-pix_fmt", "yuv420p" });
        }
        else
        {
            args.AddRange(new[] { "-i", overlayPng, "-filter_complex", "[0:v][1:v]overlay=0:0,format=yuv420p" });
            args.Add("-c:v"); args.Add(encoder);
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
}
