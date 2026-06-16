using System;
using System.IO;
using System.Threading.Tasks;
using Aquashot.Capture;
using Aquashot.Output;
using Aquashot.Selection;
using Aquashot.Settings;

namespace Aquashot.Recording;

// Drives a recording of an already-selected region: shows the Record/Stop bar over the
// region, captures the live screen via ffmpeg, then finalizes to the chosen format(s).
public class RecordingController
{
    private readonly IFFmpegRunner _runner;
    private readonly HardwareEncoderDetector _detector;
    private readonly AppSettings _settings;

    private RecordingControlBar? _bar;
    private IFFmpegSession? _session;
    private string _intermediate = "";
    private string _encoderName = "libx264";
    private PixelRect _region;
    private double _scale = 1.0;
    private RecordFormats _formats = RecordFormats.Mp4;
    private DateTime _startedUtc;
    private bool _stopping;

    // result (null on cancel/error), error (null on success/cancel)
    public event Action<RecordResult?, string?>? Finished;

    public RecordingController(IFFmpegRunner runner, HardwareEncoderDetector detector, AppSettings settings)
    {
        _runner = runner; _detector = detector; _settings = settings;
    }

    // Begin recording the given virtual-desktop region (scale = its monitor's DPI scale).
    public void StartRegion(PixelRect region, double scale, RecordFormats formats)
    {
        _region = region; _scale = scale; _formats = formats;
        _bar = new RecordingControlBar();
        _bar.PlaceAbove(region.X / scale, region.Y / scale);
        _bar.Cancelled += () => { _bar?.Close(); Finished?.Invoke(null, null); };
        _bar.RecordStarted += OnRecordStarted;
        _bar.Stopped += () => _ = OnStoppedAsync();
        _bar.Show();
    }

    private async void OnRecordStarted()
    {
        var encoder = (await _detector.DetectAsync(
            _settings.EncoderOverride == "Auto" ? null : _settings.EncoderOverride))?.Name ?? "libx264";
        if (_stopping) return; // user already pressed Stop during detection — don't start an orphan capture
        _encoderName = encoder;
        _intermediate = Path.Combine(Path.GetTempPath(), "aqua-rec-" + Guid.NewGuid().ToString("N") + ".mp4");
        _startedUtc = DateTime.UtcNow;
        // gdigrab is the universal path; ddagrab auto-selection is a future enhancement (see design).
        var args = FFmpegArgs.CaptureGdigrab(_region, _settings.RecordFps, encoder, _intermediate);
        _session = _runner.StartCapture(args);
    }

    private async Task OnStoppedAsync()
    {
        _stopping = true;
        try
        {
            var duration = DateTime.UtcNow - _startedUtc;
            var capResult = _session == null ? null : await _session.StopAsync();
            _bar?.Close();
            if (capResult is { Ok: false })
            { Finished?.Invoke(null, "Capture failed: " + Tail(capResult.StderrTail)); Cleanup(); return; }

            var encoder = new RecordingEncoder(_runner);
            var outBase = OutputService.RecordingOutputBase(_settings, DateTime.Now);
            var result = await encoder.ProduceAsync(_intermediate, _encoderName,
                _formats, duration, (int)_region.Width, _settings.RecordFps, outBase);

            foreach (var f in result.Files) OutputService.CopyFileToClipboard(f); // last wins on clipboard
            Finished?.Invoke(result, null);
        }
        catch (Exception ex) { Finished?.Invoke(null, ex.Message); }
        finally { Cleanup(); }
    }

    private void Cleanup()
    {
        try { if (File.Exists(_intermediate)) File.Delete(_intermediate); } catch { }
        _session = null;
    }

    private static string Tail(string s) => s.Length <= 300 ? s : s[^300..];
}
