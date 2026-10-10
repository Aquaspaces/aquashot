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
        ct.ThrowIfCancellationRequested();
        p.Start();
        p.BeginErrorReadLine();
        // Probe listings (notably -encoders) live on stdout; truncating them loses video
        // encoders near the top. Encoding progress stays bounded on stderr.
        var stdout = p.StandardOutput.ReadToEndAsync();
        try { await p.WaitForExitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* already exited */ }
            await p.WaitForExitAsync().ConfigureAwait(false);
            throw;
        }
        return new FFmpegResult(p.ExitCode, (await stdout.ConfigureAwait(false)) + ReadTail(sb));
    }

    public IFFmpegSession StartCapture(IReadOnlyList<string> args)
    {
        var p = new Process { StartInfo = Psi(args) };
        var sb = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Append(sb, e.Data); };
        try
        {
            p.Start();
            p.BeginErrorReadLine();
            p.BeginOutputReadLine(); // drain redirected stdout so a full pipe cannot stall capture
            return new Session(p, sb);
        }
        catch { p.Dispose(); throw; }
    }

    private static void Append(StringBuilder sb, string line)
    {
        lock (sb)
        {
            sb.Append(line).Append('\n');
            if (sb.Length > StderrTailChars * 2) sb.Remove(0, sb.Length - StderrTailChars);
        }
    }

    private static string ReadTail(StringBuilder sb) { lock (sb) return sb.ToString(); }

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
                await _p.StandardInput.WriteLineAsync("q").ConfigureAwait(false);
                await _p.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            catch { /* stdin may be closed if ffmpeg already exited */ }

            try
            {
                try { await _p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                catch (TimeoutException)
                {
                    try { _p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                    try { await _p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
                    catch (TimeoutException) { }
                }
                return new FFmpegResult(_p.HasExited ? _p.ExitCode : -1, ReadTail(_sb));
            }
            finally { _p.Dispose(); }
        }
    }
}
