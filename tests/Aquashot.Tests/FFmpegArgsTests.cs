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

    [Fact]
    public void Gdigrab_without_audio_matches_silent_overload()
    {
        var region = new PixelRect(0, 0, 320, 240);
        var silent = FFmpegArgs.CaptureGdigrab(region, 30, "libx264", "o.mp4");
        var none = FFmpegArgs.CaptureGdigrab(region, 30, "libx264", "o.mp4", AudioSpec.None);
        none.Should().Equal(silent);
        Join(none).Should().NotContain("dshow");
        Join(none).Should().NotContain("-c:a");
    }

    [Fact]
    public void Gdigrab_with_mic_adds_dshow_input_and_aac_map()
    {
        var audio = new AudioSpec(true, "Microphone (Realtek)", false, null, 160);
        var args = FFmpegArgs.CaptureGdigrab(new PixelRect(0, 0, 640, 480), 30, "libx264", "o.mp4", audio);
        var s = Join(args);
        s.Should().Contain("-f dshow");
        s.Should().Contain("-i audio=Microphone (Realtek)");
        s.Should().Contain("-c:a aac");
        s.Should().Contain("-b:a 160k");
        s.Should().Contain("-map 0:v");
        s.Should().Contain("-map 1:a");
        s.Should().NotContain("amix");
    }

    [Fact]
    public void Gdigrab_with_two_sources_amixes_into_one_track()
    {
        var audio = new AudioSpec(true, "Mic", true, "Stereo Mix", 192);
        var args = FFmpegArgs.CaptureGdigrab(new PixelRect(0, 0, 640, 480), 30, "libx264", "o.mp4", audio);
        var s = Join(args);
        s.Should().Contain("-i audio=Mic");
        s.Should().Contain("-i audio=Stereo Mix");
        s.Should().Contain("amix=inputs=2:duration=longest[aout]");
        s.Should().Contain("-map [aout]");
        s.Should().Contain("-b:a 192k");
    }

    [Fact]
    public void Gdigrab_skips_audio_slots_with_blank_device_names()
    {
        // Mic enabled but no device name, system enabled with a device -> single audio input.
        var audio = new AudioSpec(true, null, true, "CABLE Output", 128);
        var s = Join(FFmpegArgs.CaptureGdigrab(new PixelRect(0, 0, 320, 240), 30, "libx264", "o.mp4", audio));
        s.Should().Contain("-i audio=CABLE Output");
        s.Should().Contain("-map 1:a");
        s.Should().NotContain("amix");
    }

    [Fact]
    public void AudioSpec_Any_reflects_resolvable_devices()
    {
        AudioSpec.None.Any.Should().BeFalse();
        new AudioSpec(true, null, false, null).Any.Should().BeFalse();        // enabled but no device
        new AudioSpec(true, "Mic", false, null).Any.Should().BeTrue();
        new AudioSpec(false, null, true, "Stereo Mix").Any.Should().BeTrue();
    }

    [Fact]
    public void ListDshowDevices_requests_the_device_list()
    {
        var s = Join(FFmpegArgs.ListDshowDevices());
        s.Should().Contain("-list_devices true");
        s.Should().Contain("-f dshow");
        s.Should().Contain("-i dummy");
    }

    [Fact]
    public void Mp4_transcode_includeAudio_maps_and_encodes_aac()
    {
        var args = FFmpegArgs.Mp4Transcode("mid.mp4", "libx264", 4000, "out.mp4", includeAudio: true);
        var s = Join(args);
        s.Should().Contain("-map 0:a?");
        s.Should().Contain("-c:a aac");
    }

    [Fact]
    public void Mp4_transcode_default_drops_audio()
    {
        var s = Join(FFmpegArgs.Mp4Transcode("mid.mp4", "libx264", 4000, "out.mp4"));
        s.Should().NotContain("-c:a");
        s.Should().NotContain("0:a?");
    }

    [Fact]
    public void Mp4_transcode_with_overlay_and_audio_maps_video_label_and_audio()
    {
        var s = Join(FFmpegArgs.Mp4Transcode("mid.mp4", "libx264", 4000, "out.mp4", "ann.png", 49, includeAudio: true));
        s.Should().Contain("overlay=0:0,format=yuv420p[outv]");
        s.Should().Contain("-map [outv]");
        s.Should().Contain("-map 0:a?");
        s.Should().Contain("-c:a aac");
    }

    // ---- Pause-drop (concat-kept) + trim ----

    [Fact]
    public void BuildKeepFilter_for_two_spans_trims_setpts_and_concats_video_only()
    {
        var f = FFmpegArgs.BuildKeepFilter(new[] { (0.0, 5.0), (8.0, 12.0) });
        f.Should().Contain("[0:v]trim=start=0:end=5,setpts=PTS-STARTPTS[v0]");
        f.Should().Contain("[0:v]trim=start=8:end=12,setpts=PTS-STARTPTS[v1]");
        f.Should().Contain("[v0][v1]concat=n=2:v=1:a=0[outv]");
        f.Should().NotContain("atrim");
    }

    [Fact]
    public void BuildKeepFilter_with_audio_interleaves_video_and_audio_labels()
    {
        var f = FFmpegArgs.BuildKeepFilter(new[] { (0.0, 5.0), (8.0, 12.0) }, includeAudio: true);
        f.Should().Contain("[0:a]atrim=start=0:end=5,asetpts=PTS-STARTPTS[a0]");
        f.Should().Contain("[v0][a0][v1][a1]concat=n=2:v=1:a=1[outv][outa]");
    }

    [Fact]
    public void Mp4ConcatKept_emits_filter_complex_and_maps_video()
    {
        var args = FFmpegArgs.Mp4ConcatKept("mid.mp4", "libx264", 4000, "out.mp4",
            new[] { (0.0, 5.0), (8.0, 12.0) });
        var s = Join(args);
        s.Should().Contain("-filter_complex");
        s.Should().Contain("trim=start=0:end=5");
        s.Should().Contain("setpts=PTS-STARTPTS");
        s.Should().Contain("concat=n=2");
        s.Should().Contain("-map [outv]");
        s.Should().Contain("-c:v libx264");
        args[^1].Should().Be("out.mp4");
    }

    [Fact]
    public void Mp4ConcatKept_with_overlay_overlays_after_concat()
    {
        var s = Join(FFmpegArgs.Mp4ConcatKept("mid.mp4", "libx264", 4000, "out.mp4",
            new[] { (0.0, 5.0) }, overlayPng: "ann.png"));
        s.Should().Contain("-i ann.png");
        s.Should().Contain("[outv][1:v]overlay=0:0,format=yuv420p[finalv]");
        s.Should().Contain("-map [finalv]");
    }

    [Fact]
    public void Mp4ConcatKept_with_audio_maps_audio_track()
    {
        var s = Join(FFmpegArgs.Mp4ConcatKept("mid.mp4", "libx264", 4000, "out.mp4",
            new[] { (0.0, 5.0) }, includeAudio: true));
        s.Should().Contain("[outa]");
        s.Should().Contain("-map [outa]");
        s.Should().Contain("-c:a aac");
    }

    [Fact]
    public void Mp4Trim_is_a_single_span_concat()
    {
        var args = FFmpegArgs.Mp4Trim("mid.mp4", "libx264", 4000, "out.mp4", 2.5, 7.5);
        var s = Join(args);
        s.Should().Contain("trim=start=2.5:end=7.5");
        s.Should().Contain("concat=n=1:v=1:a=0[outv]");
        s.Should().Contain("-map [outv]");
    }
}
