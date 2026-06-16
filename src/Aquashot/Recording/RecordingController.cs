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

    // result (null on error), error (null on success)
    public event Action<RecordResult?, string?>? Finished;

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

    public async Task StopAsync()
    {
        _stopping = true;
        try
        {
            var duration = DateTime.UtcNow - _startedUtc;
            var capResult = _session == null ? null : await _session.StopAsync();
            if (capResult is { Ok: false })
            { Finished?.Invoke(null, "Capture failed: " + Tail(capResult.StderrTail)); Cleanup(); return; }

            var encoder = new RecordingEncoder(_runner!);
            var outBase = OutputService.RecordingOutputBase(_settings, DateTime.Now);
            var result = await encoder.ProduceAsync(_intermediate, _encoderName,
                _formats, duration, (int)_region.Width, _settings.RecordFps, outBase, _overlayPng);

            foreach (var f in result.Files) OutputService.CopyFileToClipboard(f); // last wins on clipboard
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
