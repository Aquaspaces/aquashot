using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Aquashot.Capture;

namespace Aquashot.Recording;

// GIF quality/size knobs: fps + width caps, palette size, and dithering mode.
public record GifOptions(int MaxFps, int MaxWidth, int Colors, string Dither)
{
    public static GifOptions Default => new(20, 800, 256, "sierra2_4a");
}

// Turns a recorded temp intermediate into the requested final files, enforcing the
// size budget (MP4 via bitrate; GIF via fps/scale with one shrink-retry).
public class RecordingEncoder
{
    private readonly IFFmpegRunner _runner;
    private readonly Func<string, long> _sizeOf;
    private readonly long _videoBudget;
    private readonly long _gifBudget;

    public RecordingEncoder(IFFmpegRunner runner, Func<string, long>? sizeOf = null,
        long videoBudgetBytes = SizeTargeter.DefaultBudgetBytes,
        long gifBudgetBytes = SizeTargeter.DefaultBudgetBytes)
    {
        _runner = runner;
        _sizeOf = sizeOf ?? (p => new FileInfo(p).Length);
        _videoBudget = videoBudgetBytes;
        _gifBudget = gifBudgetBytes;
    }

    public async Task<RecordResult> ProduceAsync(string intermediate, string encoder,
        RecordFormats formats, TimeSpan duration, int sourceWidth, int fps, string outBase,
        string? overlayPng = null, GifOptions? gif = null, bool includeAudio = false)
    {
        gif ??= GifOptions.Default;
        var files = new List<string>();
        bool capForced = false;

        if (formats.HasFlag(RecordFormats.Mp4))
        {
            var mp4 = outBase + ".mp4";
            int kbps = SizeTargeter.BitrateKbps(duration, _videoBudget);
            // Unlimited budget -> no -fs hard cap; otherwise leave ~1 MB headroom under the budget.
            int? fsMb = _videoBudget >= long.MaxValue ? null : Math.Max(1, (int)(_videoBudget / (1024 * 1024)) - 1);
            var r = await _runner.RunAsync(FFmpegArgs.Mp4Transcode(intermediate, encoder, kbps, mp4, overlayPng, fsMb, includeAudio));
            if (!r.Ok) throw new InvalidOperationException("MP4 encode failed: " + r.StderrTail);
            files.Add(mp4);
        }

        if (formats.HasFlag(RecordFormats.Gif))
        {
            var gifPath = outBase + ".gif";
            var plan = SizeTargeter.GifPlan(sourceWidth, fps, gif.MaxFps, gif.MaxWidth);
            await RenderGif(intermediate, plan, gifPath, overlayPng, gif);
            if (SizeTargeter.OverBudget(_sizeOf(gifPath), _gifBudget))
            {
                capForced = true;
                await RenderGif(intermediate, SizeTargeter.Shrink(plan), gifPath, overlayPng, gif);
            }
            files.Add(gifPath);
        }

        return new RecordResult(files, capForced);
    }

    private async Task RenderGif(string input, (int fps, int width) plan, string outGif, string? overlayPng, GifOptions gif)
    {
        var palette = Path.Combine(Path.GetTempPath(), "aqua-pal-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var p1 = await _runner.RunAsync(FFmpegArgs.GifPalettegen(input, plan.fps, plan.width, palette, overlayPng, gif.Colors));
            if (!p1.Ok) throw new InvalidOperationException("GIF palettegen failed: " + p1.StderrTail);
            var p2 = await _runner.RunAsync(FFmpegArgs.GifPaletteuse(input, palette, plan.fps, plan.width, outGif, overlayPng, gif.Dither));
            if (!p2.Ok) throw new InvalidOperationException("GIF paletteuse failed: " + p2.StderrTail);
        }
        finally { try { if (File.Exists(palette)) File.Delete(palette); } catch { } }
    }
}
