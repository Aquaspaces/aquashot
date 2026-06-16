using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aquashot.Capture;
using Aquashot.Output;
using Aquashot.Selection;
using Aquashot.Settings;

namespace Aquashot.Recording;

public class RecordingController
{
    private readonly ICaptureService _capture;
    private readonly IFFmpegRunner _runner;
    private readonly HardwareEncoderDetector _detector;
    private readonly AppSettings _settings;

    private RecordOverlay? _overlay;
    private RecordingControlBar? _bar;
    private IFFmpegSession? _session;
    private string _intermediate = "";
    private string _encoderName = "libx264";
    private PixelRect _region;
    private DateTime _startedUtc;

    // files (null on cancel/error), error (null on success/cancel)
    public event Action<RecordResult?, string?>? Finished;

    public RecordingController(ICaptureService capture, IFFmpegRunner runner,
        HardwareEncoderDetector detector, AppSettings settings)
    {
        _capture = capture; _runner = runner; _detector = detector; _settings = settings;
    }

    private static RecordFormats ParseFormats(string s) => s switch
    {
        "Mp4" => RecordFormats.Mp4,
        "Gif" => RecordFormats.Gif,
        _ => RecordFormats.Both,
    };

    public void Start()
    {
        var frame = _capture.FreezeAll().First();
        _overlay = new RecordOverlay(frame);
        _overlay.Cancelled += () => { _overlay?.Close(); Finished?.Invoke(null, null); };
        _overlay.RegionSelected += region =>
        {
            _overlay?.Close();
            _region = region;
            ShowBar(frame.Monitor.DpiScale, region);
        };
        _overlay.Show();
    }

    private void ShowBar(double scale, PixelRect region)
    {
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
        _encoderName = encoder;
        _intermediate = Path.Combine(Path.GetTempPath(), "aqua-rec-" + Guid.NewGuid().ToString("N") + ".mp4");
        _startedUtc = DateTime.UtcNow;
        // gdigrab is the universal path; ddagrab auto-selection is a future enhancement (see design).
        var args = FFmpegArgs.CaptureGdigrab(_region, _settings.RecordFps, encoder, _intermediate);
        _session = _runner.StartCapture(args);
    }

    private async Task OnStoppedAsync()
    {
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
                ParseFormats(_settings.RecordFormats), duration,
                (int)_region.Width, _settings.RecordFps, outBase);

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
