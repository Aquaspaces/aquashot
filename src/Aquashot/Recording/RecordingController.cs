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
    private string? _trimmed;        // pre-trimmed temp (pause-drop / trim) fed to the encoder
    private string? _overlayPng;
    private string _encoderName = "libx264";
    private PixelRect _region;
    private RecordFormats _formats = RecordFormats.Mp4;
    private DateTime _startedUtc;
    private bool _stopping;

    private TimeSpan _captureDuration;
    private string _outBase = "";
    private FFmpegResult? _capResult;
    private bool _hasAudio;          // a live audio track was captured -> carry it through the encode

    // Non-fatal diagnostic: set when a requested audio source couldn't be resolved (so the recording
    // proceeds without it). Read by the host after Finished so the user knows audio was skipped.
    private string? _audioWarning;
    public string? AudioWarning => _audioWarning;

    // PAUSE: gdigrab can't truly pause, so we record continuously and DROP paused spans at encode.
    private readonly PauseTracker _pause = new();

    // Kept [start,end] spans (in capture-second space) that survive the encode. Null until stop,
    // when it's computed from the pause tracker; the trim dialog may overwrite it (intersected).
    public IReadOnlyList<(double Start, double End)>? Keep { get; set; }

    public bool IsPaused => _pause.IsPaused;

    // The recorded (kept) clip length after stop — what the trim dialog scrubs over. Falls back to
    // the full capture duration when there were no pauses.
    public TimeSpan RecordedDuration =>
        Keep is { Count: > 0 } k ? TimeSpan.FromSeconds(KeptSeconds(k)) : _captureDuration;

    // Apply a user trim over the RECORDED timeline (0..RecordedDuration): map [start,end] back onto
    // the kept spans so paused gaps stay dropped, then store the resulting spans as Keep.
    public void SetTrim(double startSec, double endSec)
    {
        var basis = Keep is { Count: > 0 } k ? k : new[] { (0.0, _captureDuration.TotalSeconds) };
        Keep = IntersectTrim(basis, startSec, endSec);
    }

    // result (null on error), error (null on success)
    public event Action<RecordResult?, string?>? Finished;

    // Fired when the (slow) background encode begins — lets the host free its busy flag and
    // show an "Encoding…" notice while the user can already start another capture.
    public event Action? EncodingStarted;

    public RecordingController(AppSettings settings) => _settings = settings;

    private double NowSec => (DateTime.UtcNow - _startedUtc).TotalSeconds;

    // Recorded (un-paused) elapsed time — the on-screen timer should exclude paused spans.
    public TimeSpan Elapsed
    {
        get
        {
            double now = NowSec;
            double paused = _pausedAccumSec + (_pause.IsPaused ? now - _pauseBeganSec : 0);
            return TimeSpan.FromSeconds(Math.Max(0, now - paused));
        }
    }

    private double _pausedAccumSec; // total paused wall-seconds (closed intervals)
    private double _pauseBeganSec;  // when the current pause began (capture-seconds)

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
        _audioWarning = null;

        var encoder = (await _detector!.DetectAsync(
            _settings.EncoderOverride == "Auto" ? null : _settings.EncoderOverride))?.Name ?? "libx264";
        if (_stopping) return null;
        _encoderName = encoder;
        _intermediate = Path.Combine(Path.GetTempPath(), "aqua-rec-" + Guid.NewGuid().ToString("N") + ".mp4");

        // Resolve the audio sources (auto-detecting a system-loopback device when none is named).
        var audio = await ResolveAudioAsync();
        _hasAudio = audio.Any;

        _startedUtc = DateTime.UtcNow;
        _pause.Start(0);
        _pausedAccumSec = 0;
        Keep = null;
        _session = _runner!.StartCapture(
            FFmpegArgs.CaptureGdigrab(region, _settings.RecordFps, encoder, _intermediate, audio));
        return null;
    }

    // Toggle pause/resume. gdigrab keeps running; we only record the paused span (dropped at encode)
    // and accumulate paused time so Elapsed reports recorded duration. Returns the new paused state.
    public bool PauseToggle()
    {
        double now = NowSec;
        if (_pause.IsPaused)
        {
            _pause.Resume(now);
            _pausedAccumSec += now - _pauseBeganSec;
        }
        else
        {
            _pause.Pause(now);
            _pauseBeganSec = now;
        }
        return _pause.IsPaused;
    }

    // Build the AudioSpec from settings. A blank system-audio device triggers a one-shot dshow
    // probe to find a loopback ("Stereo Mix"/virtual cable); GIF-only output keeps audio off the
    // intermediate anyway (GIF drops it), but capturing it is harmless and cheap to ignore.
    private async Task<AudioSpec> ResolveAudioAsync()
    {
        bool mic = _settings.RecordMic;
        bool sys = _settings.RecordSystemAudio;
        if (!mic && !sys) return AudioSpec.None;

        string? micDev = string.IsNullOrWhiteSpace(_settings.MicDeviceName) ? null : _settings.MicDeviceName.Trim();
        string? sysDev = string.IsNullOrWhiteSpace(_settings.SystemAudioDeviceName) ? null : _settings.SystemAudioDeviceName.Trim();

        // Mic with no named device: the dshow default mic isn't addressable by ffmpeg, so pick the
        // first non-loopback audio device the probe reports.
        if (mic && micDev == null) micDev = await PickDeviceAsync(loopback: false);
        if (sys && sysDev == null) sysDev = await PickDeviceAsync(loopback: true);

        // Drop any slot we couldn't resolve to a concrete device (AudioSpec.Any then reflects reality),
        // but record a non-fatal warning so the user learns audio was silently skipped.
        if (mic && micDev == null) _audioWarning = "Microphone audio was requested but no device could be resolved.";
        if (sys && sysDev == null) _audioWarning = "System audio was requested but no loopback device could be resolved.";
        return new AudioSpec(mic && micDev != null, micDev, sys && sysDev != null, sysDev, _settings.AudioBitrateKbps);
    }

    private async Task<string?> PickDeviceAsync(bool loopback)
    {
        try
        {
            var devices = await new AudioDeviceEnumerator().EnumerateAsync(_runner!);
            if (loopback) return AudioDeviceEnumerator.DetectSystemLoopback(devices);
            foreach (var d in devices) if (!d.IsLoopback) return d.Name;
            return null;
        }
        catch (Exception ex)
        {
            // A failed probe means we couldn't find the device; record it (ResolveAudioAsync turns a
            // null result into the user-visible warning) and degrade to no audio for that slot.
            System.Diagnostics.Debug.WriteLine($"[Aquashot] audio device probe failed (loopback={loopback}): {ex}");
            return null;
        }
    }

    // FAST: stop the gdigrab session and snapshot everything the background encode needs, so
    // the overlay can close immediately. Does NOT encode (see EncodeAndFinishAsync).
    public async Task StopCaptureAsync()
    {
        _stopping = true;
        double total = NowSec;
        if (_pause.IsPaused) _pause.Resume(total); // close any open pause at stop
        _captureDuration = TimeSpan.FromSeconds(total);

        // Default the kept spans from the pause tracker. A later SetTrim (the trim dialog) may
        // overwrite Keep before EncodeAndFinishAsync runs. Null Keep => no pauses, no trim.
        var kept = _pause.KeptIntervals(total);
        Keep = HasGaps(kept, total) ? kept : null;
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

            // PAUSE/TRIM: when there are paused gaps or a trim range, first re-mux the intermediate
            // down to the kept spans (burning in the annotation overlay + audio there). The size-cap
            // encoder + GIF passes then run on the trimmed file with no overlay (already burned in),
            // so both MP4 and GIF code paths stay intact.
            string source = _intermediate;
            string? overlayForEncode = _overlayPng;
            TimeSpan encodeDuration = _captureDuration;
            if (Keep is { Count: > 0 } keep)
            {
                _trimmed = Path.Combine(Path.GetTempPath(), "aqua-trim-" + Guid.NewGuid().ToString("N") + ".mp4");
                // Bitrate the trim intermediate for the KEPT duration, not the full raw wall time — a
                // long-paused capture would otherwise starve the intermediate of bits before re-encode.
                int kbps = SizeTargeter.BitrateKbps(
                    TimeSpan.FromSeconds(KeptSeconds(keep)), SizeTargeter.BudgetBytes(_settings.MaxVideoSizeMb));
                var args = FFmpegArgs.Mp4ConcatKept(_intermediate, _encoderName, kbps, _trimmed,
                    keep, _overlayPng, maxFileSizeMb: null, includeAudio: _hasAudio);
                var tr = await _runner!.RunAsync(args);
                if (!tr.Ok) { Finished?.Invoke(null, "Trim/concat failed: " + Tail(tr.StderrTail)); return; }
                source = _trimmed;
                overlayForEncode = null; // overlay already burned into the trimmed clip
                encodeDuration = TimeSpan.FromSeconds(KeptSeconds(keep));
            }

            var encoder = new RecordingEncoder(_runner!,
                videoBudgetBytes: SizeTargeter.BudgetBytes(_settings.MaxVideoSizeMb),
                gifBudgetBytes:   SizeTargeter.BudgetBytes(_settings.MaxGifSizeMb));
            var result = await encoder.ProduceAsync(source, _encoderName, _formats, encodeDuration,
                (int)_region.Width, _settings.RecordFps, _outBase, overlayForEncode,
                new GifOptions(_settings.GifMaxFps, _settings.GifMaxWidth, _settings.GifColors, _settings.GifDither),
                includeAudio: _hasAudio);

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
        try { if (_trimmed != null && File.Exists(_trimmed)) File.Delete(_trimmed); } catch { }
        try { if (_overlayPng != null && File.Exists(_overlayPng)) File.Delete(_overlayPng); } catch { }
        _overlayPng = null;
        _trimmed = null;
        _session = null;
    }

    private static string Tail(string s) => s.Length <= 300 ? s : s[^300..];

    // Pure helpers (unit-tested via PauseTrimMathTests).

    // Total kept (recorded) seconds across the spans.
    public static double KeptSeconds(IReadOnlyList<(double Start, double End)> keep)
    {
        double t = 0;
        foreach (var (s, e) in keep) t += Math.Max(0, e - s);
        return t;
    }

    // True when the kept spans don't cover the whole [0,total] timeline (i.e. there's a real gap
    // to drop). A single span equal to [0,total] means "nothing to trim" -> the plain encode path.
    public static bool HasGaps(IReadOnlyList<(double Start, double End)> keep, double total)
    {
        if (total <= 0) return false;
        return keep.Count != 1 || keep[0].Start > 1e-6 || keep[0].End < total - 1e-6;
    }

    // Map a trim window [startSec,endSec] expressed on the RECORDED (gap-free) timeline back onto
    // the kept spans, so the dropped pause gaps remain dropped. Walks the spans accumulating their
    // recorded length and slices the portion that falls within [startSec,endSec].
    public static IReadOnlyList<(double Start, double End)> IntersectTrim(
        IReadOnlyList<(double Start, double End)> keep, double startSec, double endSec)
    {
        var result = new List<(double Start, double End)>();
        if (endSec <= startSec) return result;
        double acc = 0; // recorded-time cursor at the start of the current span
        foreach (var (s, e) in keep)
        {
            double len = Math.Max(0, e - s);
            double spanStart = acc, spanEnd = acc + len;
            double lo = Math.Max(startSec, spanStart);
            double hi = Math.Min(endSec, spanEnd);
            if (hi > lo) result.Add((s + (lo - spanStart), s + (hi - spanStart)));
            acc = spanEnd;
        }
        return result;
    }
}
