using System;
using System.IO;
using System.Threading.Tasks;
using Aquashot.Capture;
using Aquashot.Output;
using Aquashot.Selection;
using Aquashot.Settings;

namespace Aquashot.Recording;

// Headless recording engine driven by the capture overlay's toolbar (no windows of its
// own). Captures the live region via gdigrab — anything the overlay paints inside the
// region (annotations) is captured too; the toolbar sits outside the region so it isn't.
public class RecordingController
{
    private readonly AppSettings _settings;
    private IFFmpegRunner? _runner;
    private HardwareEncoderDetector? _detector;

    private IFFmpegSession? _session;
    private string _intermediate = "";
    private string? _overlayPng;
    private string _encoderName = "libx264";
    private PixelRect _region;
    private RecordFormats _formats = RecordFormats.Mp4;
    private DateTime _startedUtc;
    private bool _stopping;

    private TimeSpan _captureDuration;
    private string _outBase = "";
    private FFmpegResult? _capResult;

    // result (null on error), error (null on success)
    public event Action<RecordResult?, string?>? Finished;

    // Fired when the (slow) background encode begins — lets the host free its busy flag and
    // show an "Encoding…" notice while the user can already start another capture.
    public event Action? EncodingStarted;

    public RecordingController(AppSettings settings) => _settings = settings;

    public TimeSpan Elapsed => DateTime.UtcNow - _startedUtc;

    private bool EnsureFfmpeg(out string? error)
    {
        error = null;
        try
        {
            _runner ??= new FFmpegRunner(FFmpegProvider.Default().EnsureExtracted());
            _detector ??= new HardwareEncoderDetector(_runner);
            return true;
        }
        catch (Exception ex)
        {
            error = "Recording unavailable (ffmpeg not bundled): " + ex.Message;
            return false;
        }
    }

    // Warm encoder detection ahead of the Record click so capture starts instantly.
    public void Prewarm()
    {
        if (EnsureFfmpeg(out _))
            _ = _detector!.DetectAsync(_settings.EncoderOverride == "Auto" ? null : _settings.EncoderOverride);
    }

    // Start capturing the region. overlayPng (optional) is a transparent annotation image
    // burned over the output. Returns null on success, or an error message.
    public async Task<string?> StartAsync(PixelRect region, RecordFormats formats, string? overlayPng = null)
    {
        if (!EnsureFfmpeg(out var err)) return err;
        _region = region;
        _formats = formats;
        _overlayPng = overlayPng;
        _stopping = false;

        var encoder = (await _detector!.DetectAsync(
            _settings.EncoderOverride == "Auto" ? null : _settings.EncoderOverride))?.Name ?? "libx264";
        if (_stopping) return null;
        _encoderName = encoder;
        _intermediate = Path.Combine(Path.GetTempPath(), "aqua-rec-" + Guid.NewGuid().ToString("N") + ".mp4");
        _startedUtc = DateTime.UtcNow;
        _session = _runner!.StartCapture(FFmpegArgs.CaptureGdigrab(region, _settings.RecordFps, encoder, _intermediate));
        return null;
    }

    // FAST: stop the gdigrab session and snapshot everything the background encode needs, so
    // the overlay can close immediately. Does NOT encode (see EncodeAndFinishAsync).
    public async Task StopCaptureAsync()
    {
        _stopping = true;
        _captureDuration = DateTime.UtcNow - _startedUtc;
        // Two recordings finishing in the same second would otherwise overwrite each other (the
        // encode runs in the background, so another capture can start meanwhile) — pick a unique stem.
        var exts = new List<string>();
        if (_formats.HasFlag(RecordFormats.Mp4)) exts.Add(".mp4");
        if (_formats.HasFlag(RecordFormats.Gif)) exts.Add(".gif");
        _outBase = OutputService.UniqueRecordingOutputBase(_settings, DateTime.Now, exts.ToArray());
        _capResult = _session == null ? null : await _session.StopAsync();
        _session = null;
    }

    // SLOW: runs in the background after the overlay is gone. Encodes the intermediate into
    // the final files, applies the chosen clipboard action, then fires Finished. Recordings have
    // no bitmap, so ClipboardMode.Image is treated the same as File (a file-drop).
    public async Task EncodeAndFinishAsync(ClipboardMode clip)
    {
        EncodingStarted?.Invoke();
        try
        {
            if (_capResult is { Ok: false })
            { Finished?.Invoke(null, "Capture failed: " + Tail(_capResult.StderrTail)); return; }

            var encoder = new RecordingEncoder(_runner!,
                videoBudgetBytes: SizeTargeter.BudgetBytes(_settings.MaxVideoSizeMb),
                gifBudgetBytes:   SizeTargeter.BudgetBytes(_settings.MaxGifSizeMb));
            var result = await encoder.ProduceAsync(_intermediate, _encoderName, _formats, _captureDuration,
                (int)_region.Width, _settings.RecordFps, _outBase, _overlayPng,
                new GifOptions(_settings.GifMaxFps, _settings.GifMaxWidth, _settings.GifColors, _settings.GifDither));

            // Apply the clipboard action to the produced files (last file wins on the clipboard).
            var last = result.Files.Count > 0 ? result.Files[^1] : null;
            if (last != null)
            {
                switch (clip)
                {
                    case ClipboardMode.Image: // no bitmap for a recording — copy the file instead
                    case ClipboardMode.File: OutputService.CopyFileToClipboard(last); break;
                    case ClipboardMode.Path: OutputService.CopyPathToClipboard(last); break;
                    case ClipboardMode.None: break;
                }
            }
            Finished?.Invoke(result, null);
        }
        catch (Exception ex) { Finished?.Invoke(null, ex.Message); }
        finally { Cleanup(); }
    }

    private void Cleanup()
    {
        try { if (File.Exists(_intermediate)) File.Delete(_intermediate); } catch { }
        try { if (_overlayPng != null && File.Exists(_overlayPng)) File.Delete(_overlayPng); } catch { }
        _overlayPng = null;
        _session = null;
    }

    private static string Tail(string s) => s.Length <= 300 ? s : s[^300..];
}
