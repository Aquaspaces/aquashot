using System.Diagnostics;
using System.IO;
using System.Text;
using Aquashot.Capture;
using Aquashot.Recording;
using FluentAssertions;

namespace Aquashot.Tests;

public class FFmpegRunnerTests
{
    private static FFmpegRunner Runner() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell", "v1.0", "powershell.exe"));

    private static string[] Script(string script) => new[]
    {
        "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
        Convert.ToBase64String(Encoding.Unicode.GetBytes(script))
    };

    [Fact]
    public async Task Probe_preserves_encoder_names_before_large_stdout_listing()
    {
        var result = await Runner().RunAsync(Script(
            "[Console]::WriteLine(' V....D h264_nvenc NVIDIA encoder'); " +
            "[Console]::WriteLine(('x' * 20000)); [Console]::Error.WriteLine('probe finished')"));

        result.Ok.Should().BeTrue();
        HardwareEncoderDetector.ParseAvailable(result.StderrTail, EncoderLadder.Default)
            .Should().Contain("h264_nvenc");
        result.StderrTail.Should().Contain("probe finished");
    }

    [Fact]
    public async Task Capture_drains_stdout_before_graceful_stop()
    {
        var session = Runner().StartCapture(Script(
            "[Console]::WriteLine(('x' * 100000)); " +
            "if ([Console]::ReadLine() -ne 'q') { exit 2 }; [Console]::Error.WriteLine('stopped')"));

        var result = await session.StopAsync();

        result.Ok.Should().BeTrue();
        result.StderrTail.Should().Contain("stopped");
    }

    [Fact]
    public async Task Cancellation_terminates_the_started_process()
    {
        string pidFile = Path.GetTempFileName();
        using var cancel = new CancellationTokenSource();
        Process? child = null;
        var run = Runner().RunAsync(Script(
            $"[IO.File]::WriteAllText('{pidFile.Replace("'", "''")}', [string]$PID); Start-Sleep -Seconds 30"),
            cancel.Token);
        try
        {
            var deadline = Stopwatch.StartNew();
            string pid = "";
            while (pid.Length == 0 && deadline.Elapsed < TimeSpan.FromSeconds(10))
            {
                pid = await File.ReadAllTextAsync(pidFile);
                if (pid.Length == 0) await Task.Delay(20);
            }
            pid.Should().NotBeEmpty("the helper should have started");
            child = Process.GetProcessById(int.Parse(pid));
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            child.HasExited.Should().BeTrue();
        }
        finally
        {
            cancel.Cancel();
            try { await run; } catch (OperationCanceledException) { }
            if (child != null)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                child.Dispose();
            }
            File.Delete(pidFile);
        }
    }
}
