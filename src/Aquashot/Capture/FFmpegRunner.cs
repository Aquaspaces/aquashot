using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aquashot.Capture;

public class FFmpegRunner : IFFmpegRunner
{
    private readonly string _exePath;
    private const int StderrTailChars = 4000;

    public FFmpegRunner(string exePath) => _exePath = exePath;

    private ProcessStartInfo Psi(IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _exePath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    public async Task<FFmpegResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        using var p = new Process { StartInfo = Psi(args) };
        var sb = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Append(sb, e.Data); };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Append(sb, e.Data); };
        p.Start();
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        await p.WaitForExitAsync(ct);
        return new FFmpegResult(p.ExitCode, sb.ToString());
    }

    public IFFmpegSession StartCapture(IReadOnlyList<string> args)
    {
        var p = new Process { StartInfo = Psi(args) };
        var sb = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Append(sb, e.Data); };
        p.Start();
        p.BeginErrorReadLine();
        return new Session(p, sb);
    }

    private static void Append(StringBuilder sb, string line)
    {
        sb.Append(line).Append('\n');
        if (sb.Length > StderrTailChars * 2) sb.Remove(0, sb.Length - StderrTailChars);
    }

    private sealed class Session : IFFmpegSession
    {
        private readonly Process _p;
        private readonly StringBuilder _sb;
        public Session(Process p, StringBuilder sb) { _p = p; _sb = sb; }

        public async Task<FFmpegResult> StopAsync()
        {
            try
            {
                // Graceful: 'q' tells ffmpeg to finalize the file (write moov atom, etc.).
                await _p.StandardInput.WriteLineAsync("q");
                await _p.StandardInput.FlushAsync();
            }
            catch { /* stdin may be closed if ffmpeg already exited */ }

            if (!_p.WaitForExit(5000)) { try { _p.Kill(true); } catch { } _p.WaitForExit(2000); }
            var code = _p.HasExited ? _p.ExitCode : -1;
            _p.Dispose();
            return new FFmpegResult(code, _sb.ToString());
        }
    }
}
