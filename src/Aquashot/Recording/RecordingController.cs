using System;
using System.IO;
using System.Threading.Tasks;
using Aquashot.Capture;
using Aquashot.Output;
using Aquashot.Selection;
using Aquashot.Settings;

namespace Aquashot.Recording;

// Drives a recording of an already-selected region: shows a click-through border around
// the region plus an inline Record/Stop bar, captures the live screen via ffmpeg, then
// finalizes to the chosen format(s).
public class RecordingController
{
    private readonly IFFmpegRunner _runner;
    private readonly HardwareEncoderDetector _detector;
    private readonly AppSettings _settings;

    private BorderOverlay? _border;
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

    // Begin recording the given virtual-desktop region on the given monitor.
    public void StartRegion(PixelRect region, MonitorInfo monitor, RecordFormats formats)
    {
        _region = region; _scale = monitor.DpiScale; _formats = formats;
        // Pre-warm encoder detection (test-encode probe) now, so pressing Record starts
        // capture immediately instead of stalling ~1s. Cached.
        _ = _detector.DetectAsync(_settings.EncoderOverride == "Auto" ? null : _settings.EncoderOverride);

        _border = new BorderOverlay(region, monitor.Bounds, _scale);
        _border.Show();

        _bar = new RecordingControlBar();
        _bar.Place(BarLeftDip(monitor), BarTopDip(monitor));
        _bar.Cancelled += () => { CloseChrome(); Finished?.Invoke(null, null); };
        _bar.RecordStarted += OnRecordStarted;
        _bar.Stopped += () => _ = OnStoppedAsync();
        _bar.Show();
    }

    // Just below the selection, clamped above it if there's no room at the bottom.
    private double BarLeftDip(MonitorInfo m) => Math.Max(m.Bounds.X / _scale, _region.X / _scale);
    private double BarTopDip(MonitorInfo m)
    {
        double below = (_region.Y + _region.Height) / _scale + 12;
        double monBottom = (m.Bounds.Y + m.Bounds.Height) / _scale;
        return below + 64 > monBottom ? Math.Max(m.Bounds.Y / _scale, _region.Y / _scale - 64) : below;
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
        _bar?.BeginTimer(); // start the clock when capture actually begins
    }

    private async Task OnStoppedAsync()
    {
        _stopping = true;
        try
        {
            var duration = DateTime.UtcNow - _startedUtc;
            var capResult = _session == null ? null : await _session.StopAsync();
            CloseChrome();
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

    private void CloseChrome()
    {
        _bar?.Close();
        _border?.Close();
        _bar = null;
        _border = null;
    }

    private void Cleanup()
    {
        try { if (File.Exists(_intermediate)) File.Delete(_intermediate); } catch { }
        _session = null;
    }

    private static string Tail(string s) => s.Length <= 300 ? s : s[^300..];
}
