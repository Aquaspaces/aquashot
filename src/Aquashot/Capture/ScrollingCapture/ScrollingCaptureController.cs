using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Aquashot.Input;
using Aquashot.Selection;
using Aquashot.Settings;

namespace Aquashot.Capture.ScrollingCapture;

// Outcome of a scrolling-capture run, surfaced to the caller for a toast.
public enum ScrollResultKind { Saved, Cancelled, NothingCaptured }
public record ScrollCaptureResult(ScrollResultKind Kind, string? Path = null, int Frames = 0);

// Orchestrates a tall scrolling capture: pick a region, then repeatedly grab the region, detect
// the vertical overlap against the previous frame (ScrollStitcher), scroll the wheel down, and
// wait for the page to settle — until no new content arrives or ScrollMaxFrames is hit. Assembles
// the kept rows into one tall image and hands it to the supplied save callback. Esc cancels at any
// point (global hook + the progress toast). All WPF/window work runs on the UI thread; the wait
// between scrolls is an awaited delay so the UI stays responsive.
public sealed class ScrollingCaptureController
{
    private readonly GraphicsCaptureService _capture;
    private readonly AppSettings _settings;
    private readonly Func<BitmapSource, string> _save; // returns the saved file path

    // Cancellation source for one run: cancelled by the progress toast or the global Esc hook (both
    // from arbitrary threads — CTS.Cancel is thread-safe), and observed by the loop and Task.Delay so
    // a long settle wait aborts promptly instead of ignoring Esc for up to several seconds.
    private CancellationTokenSource? _cts;
    private bool IsCancelled => _cts?.IsCancellationRequested == true;

    public ScrollingCaptureController(GraphicsCaptureService capture, AppSettings settings,
        Func<BitmapSource, string> save)
    {
        _capture = capture;
        _settings = settings;
        _save = save;
    }

    // Run the whole flow: region pick -> scroll loop -> stitch -> save. Returns what happened so the
    // tray can show the right balloon. Must be invoked on the UI thread.
    public async Task<ScrollCaptureResult> RunAsync()
    {
        var monitors = _capture.GetMonitors();
        if (monitors.Count == 0) return new ScrollCaptureResult(ScrollResultKind.NothingCaptured);
        var desktop = new VirtualDesktop(monitors);

        // 1) Pick the region.
        var picker = new ScrollRegionPicker(desktop.Bounds, PrimaryScale(monitors));
        if (picker.ShowDialog() != true) return new ScrollCaptureResult(ScrollResultKind.Cancelled);
        var region = picker.Region;
        var monitor = monitors.FirstOrDefault(m => m.Bounds.Contains(
            region.X + region.Width / 2, region.Y + region.Height / 2)) ?? monitors[0];

        // 2) Run the scroll/grab loop with a progress toast and a global Esc hook.
        using var cts = new CancellationTokenSource();
        _cts = cts;
        var progress = new ScrollProgressWindow();
        progress.PositionFor(monitor.Bounds, monitor.DpiScale);
        progress.Cancelled += () => { try { cts.Cancel(); } catch { } };
        // The Esc hook fires on the low-level hook thread; CTS.Cancel is thread-safe.
        using var esc = new GlobalEscHook(() => { try { cts.Cancel(); } catch { } });
        progress.Show();

        BitmapSource tall;
        int frameCount;
        try
        {
            (tall, frameCount) = await CaptureLoopAsync(region, msg => progress.SetStatus(msg), cts.Token);
        }
        finally
        {
            progress.Close();
            _cts = null;
        }

        // Cancelled with nothing grabbed -> nothing to save. A cancel mid-scroll still saves the
        // rows captured so far (a partial tall shot is usually what the user wanted).
        if (cts.IsCancellationRequested && frameCount == 0) return new ScrollCaptureResult(ScrollResultKind.Cancelled);
        if (frameCount == 0) return new ScrollCaptureResult(ScrollResultKind.NothingCaptured);

        // 3) Save the assembled image (even a single frame is a valid result).
        var path = _save(tall);
        return new ScrollCaptureResult(ScrollResultKind.Saved, path, frameCount);
    }

    // The grab/scroll/stitch loop. Returns the assembled tall bitmap and the number of frames used.
    private async Task<(BitmapSource image, int frames)> CaptureLoopAsync(
        PixelRect region, Action<string> status, CancellationToken token)
    {
        var scroller = new WheelScroller();
        var captured = new List<(byte[] frame, int newRows)>();

        int step = Math.Max(60, _settings.ScrollStepPx);
        int settle = Math.Clamp(_settings.ScrollSettleMs, 0, 5000);
        int maxFrames = Math.Clamp(_settings.ScrollMaxFrames, 1, 500);
        int searchBand = Math.Max(40, step / 2 + 80); // expect ~step rows of movement, plus slack

        int cx = (int)(region.X + region.Width / 2);
        int cy = (int)(region.Y + region.Height / 2);
        scroller.MoveCursor(cx, cy);

        byte[]? prev = null;
        int width = 0, height = 0;

        for (int i = 0; i < maxFrames; i++)
        {
            if (token.IsCancellationRequested) break;
            status($"Grabbing frame {i + 1}…");

            var shot = _capture.GrabRegion(region);
            var buf = BitmapBridge.ToBgra(shot, out int w, out int h);

            if (prev == null)
            {
                width = w; height = h;
                captured.Add((buf, h)); // first frame: all rows are new
            }
            else
            {
                // Dimensions can wobble by a pixel if the region clamps; bail to stitch what we have.
                if (w != width || h != height) break;
                int newRows = ScrollStitcher.FindVerticalOverlap(prev, buf, width, height,
                    BitmapBridge.Bgra, searchBand);
                if (newRows <= 0) break;            // page didn't move -> reached the bottom
                captured.Add((buf, newRows));
            }
            prev = buf;

            if (token.IsCancellationRequested) break;

            // Scroll down and let the page settle before the next grab. The delay observes the token
            // so Esc during a long settle aborts promptly instead of being ignored for seconds.
            scroller.MoveCursor(cx, cy);
            if (!scroller.ScrollDown(step)) // SendInput blocked (e.g. UIPI) -> the page won't move
                status("Scroll was blocked (try running as administrator)…");
            try { await Task.Delay(settle, token).ConfigureAwait(true); }
            catch (OperationCanceledException) { break; }
        }

        if (captured.Count == 0) return (null!, 0);

        var (pixels, totalH) = ScrollStitcher.Assemble(captured, width, BitmapBridge.Bgra, height);
        var tall = BitmapBridge.FromBgra(pixels, width, totalH);
        return (tall, captured.Count);
    }

    private static double PrimaryScale(IReadOnlyList<MonitorInfo> monitors)
    {
        var primary = monitors.FirstOrDefault(m => m.Bounds.X == 0 && m.Bounds.Y == 0) ?? monitors[0];
        return primary.DpiScale <= 0 ? 1 : primary.DpiScale;
    }
}
