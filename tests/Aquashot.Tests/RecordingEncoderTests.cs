using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Aquashot.Capture;
using Aquashot.Recording;
using Xunit;

namespace Aquashot.Tests;

public class RecordingEncoderTests
{
    private sealed class FakeRunner : IFFmpegRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = new();
        public Func<IReadOnlyList<string>, int> ExitFor = _ => 0;
        public Task<FFmpegResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        { Calls.Add(args); return Task.FromResult(new FFmpegResult(ExitFor(args), "")); }
        public IFFmpegSession StartCapture(IReadOnlyList<string> args) => throw new NotImplementedException();
    }

    private static bool Has(IReadOnlyList<string> a, string token) => a.Contains(token);

    [Fact]
    public async Task Mp4_only_runs_a_single_transcode()
    {
        var runner = new FakeRunner();
        var enc = new RecordingEncoder(runner, sizeOf: _ => 1_000_000);
        var files = await enc.ProduceAsync("mid.mp4", "h264_nvenc", RecordFormats.Mp4,
            TimeSpan.FromSeconds(10), sourceWidth: 1280, fps: 30, outBase: "out");
        runner.Calls.Should().HaveCount(1);
        runner.Calls[0].Should().Contain("-c:v");
        files.Files.Should().ContainSingle(f => f.EndsWith(".mp4"));
    }

    [Fact]
    public async Task Gif_runs_two_passes_palettegen_then_paletteuse()
    {
        var runner = new FakeRunner();
        var enc = new RecordingEncoder(runner, sizeOf: _ => 1_000_000);
        await enc.ProduceAsync("mid.mp4", "h264_nvenc", RecordFormats.Gif,
            TimeSpan.FromSeconds(5), sourceWidth: 1280, fps: 20, outBase: "out");
        runner.Calls.Should().HaveCount(2);
        Has(runner.Calls[0], "-y").Should().BeTrue();
        runner.Calls[0].Any(a => a.Contains("palettegen")).Should().BeTrue();
        runner.Calls[1].Any(a => a.Contains("paletteuse")).Should().BeTrue();
    }

    [Fact]
    public async Task Gif_over_budget_triggers_one_shrink_retry()
    {
        var runner = new FakeRunner();
        int gifCount = 0;
        long SizeOf(string path)
        {
            if (!path.EndsWith(".gif")) return 1_000_000;
            return (++gifCount == 1) ? 60L * 1024 * 1024 : 10L * 1024 * 1024;
        }
        var enc = new RecordingEncoder(runner, sizeOf: SizeOf);
        var res = await enc.ProduceAsync("mid.mp4", "h264_nvenc", RecordFormats.Gif,
            TimeSpan.FromSeconds(5), sourceWidth: 1280, fps: 20, outBase: "out");
        // 2 passes + 2 passes on retry = 4 calls
        runner.Calls.Should().HaveCount(4);
        res.SizeCapForced.Should().BeTrue();
    }

    [Fact]
    public async Task Both_formats_produce_two_files()
    {
        var runner = new FakeRunner();
        var enc = new RecordingEncoder(runner, sizeOf: _ => 1_000_000);
        var res = await enc.ProduceAsync("mid.mp4", "h264_nvenc", RecordFormats.Both,
            TimeSpan.FromSeconds(8), sourceWidth: 1280, fps: 30, outBase: "out");
        res.Files.Should().Contain(f => f.EndsWith(".mp4"));
        res.Files.Should().Contain(f => f.EndsWith(".gif"));
    }

    [Fact]
    public async Task Mp4_encode_failure_throws()
    {
        var runner = new FakeRunner { ExitFor = _ => 1 };
        var enc = new RecordingEncoder(runner, sizeOf: _ => 1_000_000);
        var act = async () => await enc.ProduceAsync("mid.mp4", "h264_nvenc", RecordFormats.Mp4,
            TimeSpan.FromSeconds(10), sourceWidth: 1280, fps: 30, outBase: "out");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Gif_options_thread_colors_and_dither_into_passes()
    {
        var runner = new FakeRunner();
        var enc = new RecordingEncoder(runner, sizeOf: _ => 1_000_000);
        var gif = new GifOptions(MaxFps: 15, MaxWidth: 480, Colors: 64, Dither: "bayer");
        await enc.ProduceAsync("mid.mp4", "h264_nvenc", RecordFormats.Gif,
            TimeSpan.FromSeconds(5), sourceWidth: 1920, fps: 30, outBase: "out", gif: gif);
        runner.Calls[0].Any(a => a.Contains("max_colors=64")).Should().BeTrue();
        runner.Calls[0].Any(a => a.Contains("scale=480:-1")).Should().BeTrue();
        runner.Calls[0].Any(a => a.Contains("fps=15")).Should().BeTrue();
        runner.Calls[1].Any(a => a.Contains("paletteuse=dither=bayer")).Should().BeTrue();
    }

    [Fact]
    public async Task Mp4_filesize_cap_derives_from_video_budget()
    {
        var runner = new FakeRunner();
        var enc = new RecordingEncoder(runner, sizeOf: _ => 1_000_000,
            videoBudgetBytes: 30L * 1024 * 1024);
        await enc.ProduceAsync("mid.mp4", "libx264", RecordFormats.Mp4,
            TimeSpan.FromSeconds(10), sourceWidth: 1280, fps: 30, outBase: "out");
        // 30 MB budget -> -fs 29M
        runner.Calls[0].Any(a => a == "29M").Should().BeTrue();
    }

    [Fact]
    public async Task Gif_budget_is_independent_of_video_budget()
    {
        var runner = new FakeRunner();
        int gifCount = 0;
        long SizeOf(string path)
        {
            if (!path.EndsWith(".gif")) return 1_000_000;
            return (++gifCount == 1) ? 20L * 1024 * 1024 : 5L * 1024 * 1024;
        }
        // Generous video budget but a tight 10 MB GIF budget forces the shrink retry.
        var enc = new RecordingEncoder(runner, sizeOf: SizeOf,
            videoBudgetBytes: SizeTargeter.DefaultBudgetBytes,
            gifBudgetBytes: 10L * 1024 * 1024);
        var res = await enc.ProduceAsync("mid.mp4", "h264_nvenc", RecordFormats.Gif,
            TimeSpan.FromSeconds(5), sourceWidth: 1280, fps: 20, outBase: "out");
        runner.Calls.Should().HaveCount(4);
        res.SizeCapForced.Should().BeTrue();
    }

    [Fact]
    public async Task Mp4_unlimited_budget_omits_filesize_cap_and_keeps_high_bitrate()
    {
        var runner = new FakeRunner();
        var enc = new RecordingEncoder(runner, sizeOf: _ => 1_000_000,
            videoBudgetBytes: SizeTargeter.BudgetBytes(0)); // 0 MB = unlimited -> long.MaxValue
        await enc.ProduceAsync("mid.mp4", "libx264", RecordFormats.Mp4,
            TimeSpan.FromSeconds(10), sourceWidth: 1280, fps: 30, outBase: "out");
        var args = runner.Calls[0];
        args.Should().NotContain("-fs"); // no hard size cap when unlimited
        int bi = args.ToList().IndexOf("-b:v");
        bi.Should().BeGreaterThanOrEqualTo(0);
        int kbps = int.Parse(args[bi + 1].TrimEnd('k'));
        kbps.Should().BeGreaterThan(10_000); // must not collapse to the 200 kbps floor on overflow
    }
}
