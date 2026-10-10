using System.IO;
using Aquashot.Capture;
using Aquashot.Output;
using Aquashot.Recording;
using Aquashot.Selection;
using Aquashot.Settings;
using FluentAssertions;

namespace Aquashot.Tests;

public class RecordingControllerTests
{
    private sealed class FakeRunner : IFFmpegRunner, IFFmpegSession
    {
        public bool FailStart { get; init; }
        public bool Stopped { get; private set; }
        public string? Intermediate { get; private set; }
        public int EncodeCalls { get; private set; }
        public int CaptureExitCode { get; set; }
        private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Exited => _exited.Task;
        public void Crash() { CaptureExitCode = 1; _exited.TrySetResult(); }

        public Task<FFmpegResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        {
            EncodeCalls++;
            if (args[^1].EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                File.WriteAllText(args[^1], "encoded video");
            return Task.FromResult(new FFmpegResult(0, ""));
        }

        public IFFmpegSession StartCapture(IReadOnlyList<string> args)
        {
            Intermediate = args[^1];
            File.WriteAllText(Intermediate, "capture");
            if (FailStart) throw new IOException("capture failed to start");
            return this;
        }

        public Task<FFmpegResult> StopAsync()
        {
            Stopped = true;
            _exited.TrySetResult();
            return Task.FromResult(new FFmpegResult(CaptureExitCode, CaptureExitCode == 0 ? "" : "device lost"));
        }
    }

    [Fact]
    public async Task Failed_start_returns_error_and_removes_temporary_files()
    {
        var runner = new FakeRunner { FailStart = true };
        var controller = new RecordingController(new AppSettings { EncoderOverride = "libx264" }, runner);
        string overlay = Path.GetTempFileName();
        try
        {
            var error = await controller.StartAsync(new PixelRect(0, 0, 320, 240), RecordFormats.Mp4, overlay);

            error.Should().Contain("capture failed to start");
            File.Exists(overlay).Should().BeFalse();
            File.Exists(runner.Intermediate).Should().BeFalse();
        }
        finally
        {
            File.Delete(overlay);
            if (runner.Intermediate != null) File.Delete(runner.Intermediate);
        }
    }

    [Fact]
    public async Task Invalid_output_folder_still_stops_capture()
    {
        string fileAsFolder = Path.GetTempFileName();
        var runner = new FakeRunner();
        var controller = new RecordingController(new AppSettings
        {
            EncoderOverride = "libx264", SaveFolder = fileAsFolder
        }, runner);
        try
        {
            (await controller.StartAsync(new PixelRect(0, 0, 320, 240), RecordFormats.Mp4)).Should().BeNull();

            await Assert.ThrowsAsync<IOException>(() => controller.StopCaptureAsync());

            runner.Stopped.Should().BeTrue();
            File.Exists(runner.Intermediate).Should().BeFalse();
        }
        finally
        {
            File.Delete(fileAsFolder);
            if (runner.Intermediate != null) File.Delete(runner.Intermediate);
        }
    }

    [Fact]
    public async Task Ffmpeg_exiting_mid_recording_raises_CaptureEnded_and_reports_the_failure()
    {
        var runner = new FakeRunner();
        string folder = Path.Combine(Path.GetTempPath(), "aqua-rec-crash-" + Guid.NewGuid().ToString("N"));
        var controller = new RecordingController(
            new AppSettings { EncoderOverride = "libx264", SaveFolder = folder }, runner);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.CaptureEnded += () => ended.TrySetResult();
        string? error = null;
        controller.Finished += (_, message) => error = message;
        try
        {
            (await controller.StartAsync(new PixelRect(0, 0, 320, 240), RecordFormats.Mp4)).Should().BeNull();
            runner.Crash();
            await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
            controller.CaptureEndedEarly.Should().BeTrue();

            await controller.StopCaptureAsync();
            await controller.EncodeAndFinishAsync(ClipboardMode.None);
            error.Should().Contain("Capture failed").And.Contain("device lost");
        }
        finally
        {
            if (runner.Intermediate != null) File.Delete(runner.Intermediate);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Normal_stop_does_not_raise_CaptureEnded()
    {
        var runner = new FakeRunner();
        string folder = Path.Combine(Path.GetTempPath(), "aqua-rec-stop-" + Guid.NewGuid().ToString("N"));
        var controller = new RecordingController(
            new AppSettings { EncoderOverride = "libx264", SaveFolder = folder }, runner);
        bool ended = false;
        controller.CaptureEnded += () => ended = true;
        try
        {
            (await controller.StartAsync(new PixelRect(0, 0, 320, 240), RecordFormats.Mp4)).Should().BeNull();
            await controller.StopCaptureAsync();
            await Task.Delay(50);
            ended.Should().BeFalse();
            controller.CaptureEndedEarly.Should().BeFalse();
            await controller.EncodeAndFinishAsync(ClipboardMode.None);
        }
        finally
        {
            if (runner.Intermediate != null) File.Delete(runner.Intermediate);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Empty_trim_does_not_export_the_untrimmed_capture()
    {
        var runner = new FakeRunner();
        var controller = new RecordingController(new AppSettings(), runner)
        {
            Keep = Array.Empty<(double, double)>()
        };
        string? error = null;
        controller.Finished += (_, message) => error = message;

        await controller.EncodeAndFinishAsync(ClipboardMode.None);

        runner.EncodeCalls.Should().Be(0);
        error.Should().Contain("No recorded frames");
        controller.RecordedDuration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Pending_recordings_reserve_distinct_paths_without_empty_videos()
    {
        string folder = Path.Combine(Path.GetTempPath(), "aqua-recording-test-" + Guid.NewGuid().ToString("N"));
        var settings = new AppSettings { SaveFolder = folder };
        try
        {
            var now = DateTime.Now;
            string first = OutputService.ReserveRecordingOutputBase(settings, now, out var reservation1, ".mp4");
            using (reservation1)
            {
                string second = OutputService.ReserveRecordingOutputBase(settings, now, out var reservation2, ".mp4");
                using (reservation2)
                {
                    second.Should().NotBe(first);
                    File.Exists(first + ".mp4").Should().BeFalse();
                    File.Exists(second + ".mp4").Should().BeFalse();
                }
            }
            Directory.GetFiles(folder).Should().BeEmpty();
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    // Called by the shared WPF smoke test while its Application dispatcher is pumping.
    internal static async Task VerifyBackgroundClipboardDispatchAsync()
    {
        string folder = Path.Combine(Path.GetTempPath(), "aqua-clipboard-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var mode in new[] { ClipboardMode.File, ClipboardMode.Path, ClipboardMode.Image })
            {
                string? copiedPath = null;
                RecordResult? saved = null;
                string? error = null;
                var controller = new RecordingController(new AppSettings
                {
                    SaveFolder = folder, EncoderOverride = "libx264"
                }, new FakeRunner(), (path, clipboardMode) =>
                {
                    System.Windows.Application.Current.Dispatcher.CheckAccess().Should().BeTrue();
                    Thread.CurrentThread.GetApartmentState().Should().Be(ApartmentState.STA);
                    clipboardMode.Should().Be(mode);
                    copiedPath = path;
                });
                controller.Finished += (result, message) => { saved = result; error = message; };
                (await controller.StartAsync(new PixelRect(0, 0, 320, 240), RecordFormats.Mp4)).Should().BeNull();
                await controller.StopCaptureAsync();

                await Task.Run(() => controller.EncodeAndFinishAsync(mode));

                error.Should().BeNull();
                saved.Should().NotBeNull();
                copiedPath.Should().Be(saved!.Files.Single());
                File.Exists(copiedPath).Should().BeTrue();
            }
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }
}
