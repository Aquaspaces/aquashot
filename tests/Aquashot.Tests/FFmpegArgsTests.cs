using System.Collections.Generic;
using FluentAssertions;
using Aquashot.Recording;
using Aquashot.Selection;
using Xunit;

namespace Aquashot.Tests;

public class FFmpegArgsTests
{
    private static string Join(IReadOnlyList<string> a) => string.Join(" ", a);

    [Fact]
    public void Gdigrab_capture_has_offset_size_fps_and_encoder()
    {
        var args = FFmpegArgs.CaptureGdigrab(new PixelRect(100, 50, 640, 480), 30, "h264_nvenc", "C:\\t\\mid.mp4");
        var s = Join(args);
        s.Should().Contain("-f gdigrab");
        s.Should().Contain("-framerate 30");
        s.Should().Contain("-offset_x 100");
        s.Should().Contain("-offset_y 50");
        s.Should().Contain("-video_size 640x480");
        s.Should().Contain("-i desktop");
        s.Should().Contain("-c:v h264_nvenc");
        args[^1].Should().Be("C:\\t\\mid.mp4");
    }

    [Fact]
    public void Gdigrab_rounds_odd_dimensions_to_even()
    {
        // Odd width/height break yuv420p/NVENC (EINVAL → 0 frames) — must round down to even.
        var args = FFmpegArgs.CaptureGdigrab(new PixelRect(10, 20, 641, 481), 30, "h264_nvenc", "o.mp4");
        Join(args).Should().Contain("-video_size 640x480");
    }

    [Fact]
    public void Ddagrab_capture_uses_lavfi_source_and_crop()
    {
        var args = FFmpegArgs.CaptureDdagrab(new PixelRect(0, 0, 800, 600), 30, "hevc_nvenc", "C:\\t\\mid.mp4");
        var s = Join(args);
        s.Should().Contain("-f lavfi");
        s.Should().Contain("ddagrab");
        s.Should().Contain("-c:v hevc_nvenc");
    }

    [Fact]
    public void GifPass1_generates_palette_with_fps_and_scale()
    {
        var args = FFmpegArgs.GifPalettegen("C:\\t\\mid.mp4", 20, 800, "C:\\t\\pal.png");
        var s = Join(args);
        s.Should().Contain("fps=20");
        s.Should().Contain("scale=800:-1:flags=lanczos");
        s.Should().Contain("palettegen");
        args[^1].Should().Be("C:\\t\\pal.png");
    }

    [Fact]
    public void GifPass2_applies_palette_with_dither()
    {
        var args = FFmpegArgs.GifPaletteuse("C:\\t\\mid.mp4", "C:\\t\\pal.png", 20, 800, "C:\\t\\out.gif");
        var s = Join(args);
        s.Should().Contain("paletteuse");
        s.Should().Contain("dither=");
        args[^1].Should().Be("C:\\t\\out.gif");
    }

    [Fact]
    public void Mp4_transcode_sets_bitrate_and_filesize_cap()
    {
        var args = FFmpegArgs.Mp4Transcode("C:\\t\\mid.mp4", "h264_nvenc", 4000, "C:\\t\\out.mp4");
        var s = Join(args);
        s.Should().Contain("-c:v h264_nvenc");
        s.Should().Contain("-b:v 4000k");
        s.Should().Contain("-fs 49M");
        s.Should().Contain("-movflags +faststart");
        args[^1].Should().Be("C:\\t\\out.mp4");
    }

    [Fact]
    public void Mp4_transcode_honors_custom_filesize_cap()
    {
        var args = FFmpegArgs.Mp4Transcode("mid.mp4", "libx264", 4000, "out.mp4", maxFileSizeMb: 99);
        Join(args).Should().Contain("-fs 99M");
    }

    [Fact]
    public void GifPalettegen_threads_color_count_into_filter()
    {
        var args = FFmpegArgs.GifPalettegen("mid.mp4", 20, 800, "pal.png", colors: 64);
        Join(args).Should().Contain("palettegen=stats_mode=diff:max_colors=64");
    }

    [Fact]
    public void GifPaletteuse_threads_dither_mode_into_filter()
    {
        var args = FFmpegArgs.GifPaletteuse("mid.mp4", "pal.png", 20, 800, "out.gif", dither: "bayer");
        Join(args).Should().Contain("paletteuse=dither=bayer");
    }

    [Fact]
    public void Mp4_with_overlay_burns_annotation_png()
    {
        var args = FFmpegArgs.Mp4Transcode("mid.mp4", "h264_nvenc", 4000, "out.mp4", "ann.png");
        var s = Join(args);
        s.Should().Contain("-i mid.mp4");
        s.Should().Contain("-i ann.png");
        s.Should().Contain("overlay=0:0");
        s.Should().Contain("-c:v h264_nvenc");
        args[^1].Should().Be("out.mp4");
    }

    [Fact]
    public void Gif_passes_with_overlay_reference_annotation_then_palette()
    {
        var pass1 = FFmpegArgs.GifPalettegen("mid.mp4", 20, 800, "pal.png", "ann.png");
        Join(pass1).Should().Contain("overlay=0:0");
        Join(pass1).Should().Contain("palettegen");

        var pass2 = FFmpegArgs.GifPaletteuse("mid.mp4", "pal.png", 20, 800, "out.gif", "ann.png");
        var s2 = Join(pass2);
        s2.Should().Contain("-i mid.mp4");
        s2.Should().Contain("-i ann.png");
        s2.Should().Contain("[x][2:v]paletteuse"); // palette is input index 2 when overlay present
    }

    [Fact]
    public void Probe_encodes_a_few_synthetic_frames_to_null()
    {
        var args = FFmpegArgs.EncodeProbe("h264_nvenc");
        var s = Join(args);
        s.Should().Contain("lavfi");
        s.Should().Contain("-c:v h264_nvenc");
        s.Should().Contain("-f null");
    }
}
