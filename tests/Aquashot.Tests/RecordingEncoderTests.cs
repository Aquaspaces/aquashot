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
}
