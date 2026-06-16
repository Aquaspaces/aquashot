using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Aquashot.Capture;

namespace Aquashot.Recording;

// Turns a recorded temp intermediate into the requested final files, enforcing the
// size budget (MP4 via bitrate; GIF via fps/scale with one shrink-retry).
public class RecordingEncoder
{
    private readonly IFFmpegRunner _runner;
    private readonly Func<string, long> _sizeOf;
    private readonly long _budget;

    public RecordingEncoder(IFFmpegRunner runner, Func<string, long>? sizeOf = null,
        long budgetBytes = SizeTargeter.DefaultBudgetBytes)
    {
        _runner = runner;
        _sizeOf = sizeOf ?? (p => new FileInfo(p).Length);
        _budget = budgetBytes;
    }

    public async Task<RecordResult> ProduceAsync(string intermediate, string encoder,
        RecordFormats formats, TimeSpan duration, int sourceWidth, int fps, string outBase)
    {
        var files = new List<string>();
        bool capForced = false;

        if (formats.HasFlag(RecordFormats.Mp4))
        {
            var mp4 = outBase + ".mp4";
            int kbps = SizeTargeter.BitrateKbps(duration, _budget);
            await _runner.RunAsync(FFmpegArgs.Mp4Transcode(intermediate, encoder, kbps, mp4));
            files.Add(mp4);
        }

        if (formats.HasFlag(RecordFormats.Gif))
        {
            var gif = outBase + ".gif";
            var plan = SizeTargeter.GifPlan(sourceWidth, fps);
            await RenderGif(intermediate, plan, gif);
            if (SizeTargeter.OverBudget(_sizeOf(gif), _budget))
            {
                capForced = true;
                await RenderGif(intermediate, SizeTargeter.Shrink(plan), gif);
            }
            files.Add(gif);
        }

        return new RecordResult(files, capForced);
    }

    private async Task RenderGif(string input, (int fps, int width) plan, string outGif)
    {
        var palette = Path.Combine(Path.GetTempPath(), "aqua-pal-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            await _runner.RunAsync(FFmpegArgs.GifPalettegen(input, plan.fps, plan.width, palette));
            await _runner.RunAsync(FFmpegArgs.GifPaletteuse(input, palette, plan.fps, plan.width, outGif));
        }
        finally { try { if (File.Exists(palette)) File.Delete(palette); } catch { } }
    }
}
