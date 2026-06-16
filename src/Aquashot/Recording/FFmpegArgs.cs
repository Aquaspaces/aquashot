using System.Collections.Generic;
using System.Globalization;
using Aquashot.Selection;

namespace Aquashot.Recording;

// Pure builders. Each returns an ArgumentList for ProcessStartInfo (no manual quoting).
public static class FFmpegArgs
{
    private static string I(double v) => ((int)System.Math.Round(v)).ToString(CultureInfo.InvariantCulture);

    public static List<string> CaptureGdigrab(PixelRect region, int fps, string encoder, string outPath) => new()
    {
        "-y", "-f", "gdigrab", "-framerate", fps.ToString(), "-draw_mouse", "1",
        "-offset_x", I(region.X), "-offset_y", I(region.Y),
        "-video_size", $"{I(region.Width)}x{I(region.Height)}", "-i", "desktop",
        "-c:v", encoder, "-pix_fmt", "yuv420p", outPath
    };

    // ddagrab is a lavfi source; crop the captured output to the region.
    public static List<string> CaptureDdagrab(PixelRect region, int fps, string encoder, string outPath) => new()
    {
        "-y", "-f", "lavfi", "-i",
        $"ddagrab=framerate={fps}:output_idx=0,crop={I(region.Width)}:{I(region.Height)}:{I(region.X)}:{I(region.Y)},hwdownload,format=bgra,format=yuv420p",
        "-c:v", encoder, "-pix_fmt", "yuv420p", outPath
    };

    public static List<string> GifPalettegen(string input, int fps, int width, string palettePath) => new()
    {
        "-y", "-i", input,
        "-vf", $"fps={fps},scale={width}:-1:flags=lanczos,palettegen=stats_mode=diff",
        palettePath
    };

    public static List<string> GifPaletteuse(string input, string palettePath, int fps, int width, string outPath) => new()
    {
        "-y", "-i", input, "-i", palettePath,
        "-lavfi", $"fps={fps},scale={width}:-1:flags=lanczos[x];[x][1:v]paletteuse=dither=sierra2_4a",
        outPath
    };

    public static List<string> Mp4Transcode(string input, string encoder, int bitrateKbps, string outPath) => new()
    {
        "-y", "-i", input, "-c:v", encoder,
        "-b:v", $"{bitrateKbps}k", "-maxrate", $"{bitrateKbps * 3 / 2}k", "-bufsize", $"{bitrateKbps * 2}k",
        "-fs", "49M", "-pix_fmt", "yuv420p", "-movflags", "+faststart", outPath
    };

    public static List<string> EncodeProbe(string encoder) => new()
    {
        "-hide_banner", "-f", "lavfi", "-i", "color=c=black:s=128x128:d=0.2",
        "-frames:v", "5", "-c:v", encoder, "-f", "null", "-"
    };
}
