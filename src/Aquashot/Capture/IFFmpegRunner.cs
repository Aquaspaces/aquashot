using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Aquashot.Capture;

public record FFmpegResult(int ExitCode, string StderrTail)
{
    public bool Ok => ExitCode == 0;
}

// A live recording: stop it (graceful) and await the final result.
public interface IFFmpegSession
{
    Task<FFmpegResult> StopAsync();
}

public interface IFFmpegRunner
{
    // Run to completion (probe, transcode, palette passes). Blocks until exit.
    Task<FFmpegResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default);

    // Start a long-running capture; returns a session you stop later.
    IFFmpegSession StartCapture(IReadOnlyList<string> args);
}
