using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aquashot.Annotation;
using Aquashot.Capture;
using Aquashot.History;
using Aquashot.Selection;
using Aquashot.Input;
using Aquashot.Output;
using Aquashot.Overlay;
using Aquashot.Settings;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using ToolStripSeparator = System.Windows.Forms.ToolStripSeparator;
using ToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;
using SystemIcons = System.Drawing.SystemIcons;
using Application = System.Windows.Application;

namespace Aquashot.Tray;

public class TrayHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly HotkeyService _hotkey;
    private readonly SettingsStore _store;
    private readonly GraphicsCaptureService _capture = new();
    private readonly OutputService _output = new();
    private AppSettings _settings;
    private bool _busy;
    private readonly CaptureLibrary _library;
    private readonly WindowsOcrService _ocrService = new();
    private string? _lastSaved;
    // WINDOW-TITLE: foreground window caption + app captured when a capture starts (by save time
    // the foreground is ours), threaded into the {window}/{app} filename tokens.
    private string _capWindowTitle = "";
    private string _capAppName = "";
    private readonly OcrIndexer _ocrIndexer;
    private HistoryWindow? _historyWindow;
    private readonly ToolStripMenuItem _recentMenu = new("Recent");
    private readonly Aquashot.Freeze.FreezeController _freeze = new();

    public TrayHost()
    {
        _store = new SettingsStore(SettingsStore.DefaultPath());
        _settings = _store.Load();
        var settingsDir = Path.GetDirectoryName(SettingsStore.DefaultPath())!;
        _library = new CaptureLibrary(Path.Combine(settingsDir, "library.json"),
            _settings.HistoryCap, Path.Combine(settingsDir, "recent.json"));
        _ocrIndexer = new OcrIndexer(_library, _ocrService);

        _icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = "Aquashot"
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Capture region", null, (_, __) => StartCapture(OverlayWindow.OverlayMode.Region));
        menu.Items.Add("Capture window", null, (_, __) => StartCapture(OverlayWindow.OverlayMode.Window));
        menu.Items.Add("Capture all monitors", null, (_, __) => CaptureAllMonitors());
        menu.Items.Add("Repeat last region", null, (_, __) => RepeatLastRegion());
        menu.Items.Add("Scrolling capture…", null, (_, __) => StartScrollingCapture());

        // Window pick / delayed capture / colour pick / freeze toggle now live inside the
        // region-capture toolbar (see InlineToolbar), not the tray menu.
        menu.Items.Add(_recentMenu);
        menu.Items.Add("History…", null, (_, __) => OpenHistory());
        RebuildRecent();

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, __) => OpenSettings());
        menu.Items.Add("Quit", null, (_, __) => Quit());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, __) => StartCapture(OverlayWindow.OverlayMode.Region);
        _icon.BalloonTipClicked += (_, __) => { if (_lastSaved != null) OpenHistory(_lastSaved); };

        _hotkey = new HotkeyService();
        _hotkey.ActionPressed += OnHotkeyAction;
        if (!RegisterHotkeys())
            _icon.ShowBalloonTip(3000, "Aquashot",
                $"Could not register hotkey '{_settings.Hotkey}'. Open Settings to change it.",
                ToolTipIcon.Warning);

        if (HotkeyService.IsPrtScMappedToSnip())
            _icon.ShowBalloonTip(4000, "Aquashot",
                "PrtSc is mapped to Windows Snipping Tool. Open Settings to disable it so PrtSc opens Aquashot.",
                ToolTipIcon.Info);
    }

    // Register every configured global hotkey (called on startup and after a settings save).
    // Returns whether the main capture hotkey registered successfully (a non-blank combo that took).
    private bool RegisterHotkeys()
    {
        var combos = new (HotkeyAction Action, string? Hotkey)[]
        {
            (HotkeyAction.Capture, _settings.Hotkey),
            (HotkeyAction.Freeze, _settings.FreezeHotkey),
            (HotkeyAction.ScrollingCapture, _settings.ScrollingCaptureHotkey),
            (HotkeyAction.RepeatLastRegion, _settings.RepeatLastRegionHotkey),
            (HotkeyAction.RecordRegion, _settings.RecordRegionHotkey),
            (HotkeyAction.CaptureWindow, _settings.CaptureWindowHotkey),
            (HotkeyAction.CaptureFullScreen, _settings.CaptureFullScreenHotkey),
        };

        bool capture = false;
        // Track which (mods,vk) combo each action wants so we can name cross-action conflicts: when a
        // non-blank combo fails to register because another action already holds it, warn the user.
        var seen = new Dictionary<(uint mods, uint vk), HotkeyAction>();
        var conflicts = new List<string>();
        foreach (var (action, hotkey) in combos)
        {
            bool ok = _hotkey.Register(action, hotkey);
            if (action == HotkeyAction.Capture) capture = ok;
            if (string.IsNullOrWhiteSpace(hotkey)) continue;
            var (mods, vk) = HotkeyService.ParseHotkey(hotkey!);
            if (vk == 0) continue; // unparseable combo, not a duplicate
            if (!ok && seen.TryGetValue((mods, vk), out var owner))
                conflicts.Add($"{action} conflicts with {owner} ({hotkey!.Trim()})");
            else if (ok)
                seen[(mods, vk)] = action;
        }

        if (conflicts.Count > 0)
            _icon.ShowBalloonTip(4000, "Aquashot",
                "Some hotkeys clash and were not all registered:\n" + string.Join("\n", conflicts) +
                "\nChange them in Settings.", ToolTipIcon.Warning);

        return string.IsNullOrWhiteSpace(_settings.Hotkey) || capture; // a blank main hotkey isn't an error
    }

    // Dispatch a pressed global hotkey to its action.
    private void OnHotkeyAction(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.Capture:
            case HotkeyAction.RecordRegion:   StartCapture(OverlayWindow.OverlayMode.Region); break;
            case HotkeyAction.CaptureWindow:  StartCapture(OverlayWindow.OverlayMode.Window); break;
            case HotkeyAction.CaptureFullScreen: CaptureAllMonitors(); break;
            case HotkeyAction.Freeze:         ToggleFreeze(); break;
            case HotkeyAction.ScrollingCapture: StartScrollingCapture(); break;
            case HotkeyAction.RepeatLastRegion: RepeatLastRegion(); break;
        }
    }

    // LAST-REGION: re-capture the last committed region immediately (no overlay) and save+copy it,
    // exactly like a normal region capture. No-op (with a hint) until a region has been captured.
    private void RepeatLastRegion()
    {
        if (_freeze.IsActive) _freeze.Resume();
        if (_busy) return;
        if (!Aquashot.Selection.RegionCodec.TryDecode(_settings.LastRegion, out var region))
        {
            _icon.ShowBalloonTip(2000, "Aquashot", "No region captured yet to repeat.", ToolTipIcon.Info);
            return;
        }
        _busy = true;
        try
        {
            var frames = _capture.FreezeAll();
            var frame = frames.FirstOrDefault(f => MonitorContains(f.Monitor.Bounds, region))
                        ?? frames.FirstOrDefault();
            if (frame == null) return;
            var path = _output.Save(frame, region, new AnnotationDocument(), _settings, DateTime.Now,
                ParseClipboardAction(_settings.DefaultClipboardAction),
                ForegroundWindow.Title(), ForegroundWindow.AppName());
            Remember(path);
            ShareAfterSave(path);
            _icon.ShowBalloonTip(2000, "Aquashot", "Repeated last region: " + path, ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _icon.ShowBalloonTip(3000, "Aquashot", "Repeat failed: " + ex.Message, ToolTipIcon.Error);
        }
        finally { _busy = false; }
    }

    // Freeze the desktop into a static, always-on-top snapshot (looks paused); toggle to resume.
    private void ToggleFreeze()
    {
        if (_freeze.IsActive) { _freeze.Resume(); return; }
        if (_busy) return;
        try
        {
            _freeze.Freeze(_capture.FreezeAll());
        }
        catch (Exception ex)
        {
            _icon.ShowBalloonTip(3000, "Aquashot", "Freeze failed: " + ex.Message, ToolTipIcon.Error);
        }
    }

    private void StartCapture(OverlayWindow.OverlayMode mode)
    {
        if (_freeze.IsActive) _freeze.Resume(); // capturing resumes a frozen desktop first
        if (_busy) return;
        _busy = true;
        // WINDOW-TITLE: snapshot the foreground window now, before our overlay steals focus, so the
        // {window}/{app} filename tokens describe what the user was looking at.
        _capWindowTitle = ForegroundWindow.Title();
        _capAppName = ForegroundWindow.AppName();
        OverlayController? ctrl = null;
        try
        {
            var frames = _capture.FreezeAll();
            ctrl = new OverlayController { Mode = mode };
            ctrl.DefaultClip = ParseClipboardAction(_settings.DefaultClipboardAction);
            ctrl.Settings = _settings;                            // highlighter/spotlight/redact options
            ctrl.Ocr = _settings.EnableOcr ? _ocrService : null;  // enable the auto-redact button when OCR is on
            ctrl.Refreeze = () => _capture.FreezeAll();           // re-snapshot for the freeze toggle
            ctrl.DelayedCapture += (region, secs) => DelayedRegionCapture(region, secs);
            // LAST-REGION: persist the committed selection so the repeat hotkey can replay it.
            ctrl.RegionCommitted += RememberLastRegion;
            ctrl.Confirmed += (f, r, d, clip) =>
            {
                // The overlay is already closed by OverlayController before this fires, but WPF
                // hasn't repainted yet — the synchronous Save (PNG encode + clipboard + disk) would
                // block the render and leave the dimmed overlay on screen. Defer the save to a
                // Background dispatcher pass so the close paints first; capture feels instant.
                // Keep _busy until the save completes so a fast second capture can't collide on the
                // same-second filename.
                Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(async () =>
                    {
                        try
                        {
                            // AUTO-REDACT: when enabled, OCR the composed crop and append blur/pixelate
                            // shapes over (matching) text lines before the flatten+save below.
                            if (_settings.AutoRedactEnabled && _settings.EnableOcr)
                                await ApplyAutoRedactAsync(f, r, d);

                            var path = _output.Save(f, r, d, _settings, DateTime.Now, clip,
                                _capWindowTitle, _capAppName);
                            Remember(path);
                            ShareAfterSave(path); // auto-upload + copy link when enabled
                            var note = clip switch
                            {
                                ClipboardMode.None => "Saved: " + path,
                                ClipboardMode.File => "Saved & file copied: " + path,
                                ClipboardMode.Path => "Saved & path copied: " + path,
                                _                  => "Saved & copied: " + path,
                            };
                            _icon.ShowBalloonTip(2000, "Aquashot", note, ToolTipIcon.Info);
                        }
                        catch (Exception ex)
                        {
                            _icon.ShowBalloonTip(3000, "Aquashot", "Save failed: " + ex.Message, ToolTipIcon.Error);
                        }
                        finally { _busy = false; }
                    }));
            };
            ctrl.Cancelled += () => _busy = false;
            ctrl.PinRequested += () => _busy = false; // pinned to screen; capture flow is done

            // Recording is driven inline by the capture toolbar. The slow encode now runs on a
            // background thread, so both callbacks marshal back to the UI thread before touching
            // _icon/_library/_busy.
            var recorder = new Aquashot.Recording.RecordingController(_settings);
            recorder.EncodingStarted += () =>
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _busy = false; // encoding off-screen; allow a new capture while it runs
                    _icon.ShowBalloonTip(1500, "Aquashot", "Encoding…", ToolTipIcon.Info);
                }));
            recorder.Finished += (result, error) =>
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _busy = false;
                    if (error != null)
                        _icon.ShowBalloonTip(3000, "Aquashot", error, ToolTipIcon.Error);
                    else if (result != null)
                    {
                        foreach (var file in result.Files) Remember(file);
                        // Auto-upload one artifact (prefer the MP4) so a Both-format record shares once.
                        var shareFile = result.Files.FirstOrDefault(f =>
                            f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) ?? result.Files.FirstOrDefault();
                        if (shareFile != null) ShareAfterSave(shareFile);
                        var note = result.SizeCapForced ? $" (reduced to fit {_settings.MaxGifSizeMb} MB)" : "";
                        _icon.ShowBalloonTip(2500, "Aquashot",
                            "Saved & copied: " + string.Join(", ", result.Files.Select(Path.GetFileName)) + note,
                            ToolTipIcon.Info);
                        // Non-fatal: tell the user if a requested audio source was silently skipped.
                        if (!string.IsNullOrEmpty(recorder.AudioWarning))
                            _icon.ShowBalloonTip(3000, "Aquashot", recorder.AudioWarning, ToolTipIcon.Warning);
                    }
                }));
            ctrl.Recorder = recorder;
            ctrl.Show(frames);
        }
        catch (Exception ex)
        {
            ctrl?.Close();
            _busy = false;
            _icon.ShowBalloonTip(3000, "Aquashot", "Capture failed: " + ex.Message, ToolTipIcon.Error);
        }
    }

    // AUTO-REDACT: compose the crop to a temp PNG, OCR it (lines in crop-local px), then append
    // blur/pixelate shapes to the document so they flatten into the saved image. Best-effort.
    private async Task ApplyAutoRedactAsync(CapturedFrame f, PixelRect r, AnnotationDocument d)
    {
        string? temp = null;
        try
        {
            var crop = _output.Compose(f, r, new AnnotationDocument()); // base pixels only, no shapes
            if (crop.CanFreeze && !crop.IsFrozen) crop.Freeze();
            temp = Path.Combine(Path.GetTempPath(), "aqua-redact-" + Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(temp, _output.Encode(crop, "png"));

            var lines = await _ocrService.RecognizeLinesAsync(temp);
            var chosen = Aquashot.Redaction.AutoRedactor.SelectLines(lines, _settings.RedactPatterns);
            var shapes = Aquashot.Redaction.AutoRedactor.BuildShapes(chosen, 0, 0,
                _settings.RedactStyle, _settings.RedactBlurRadius, _settings.RedactPixelateBlock);
            if (shapes.Count > 0) d.AddRange(shapes);
        }
        catch { /* never break a save */ }
        finally { if (temp != null) try { File.Delete(temp); } catch { } }
    }

    private void CaptureAllMonitors()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var frames = _capture.FreezeAll();
            var monitors = frames.Select(f => f.Monitor).ToList();
            var composite = DesktopStitcher.Stitch(frames, new VirtualDesktop(monitors).Bounds);
            var path = _output.SaveComposite(composite, _settings, DateTime.Now);
            Remember(path);
            ShareAfterSave(path);
            _icon.ShowBalloonTip(2000, "Aquashot", "All monitors saved & copied: " + path, ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _icon.ShowBalloonTip(3000, "Aquashot", "Capture failed: " + ex.Message, ToolTipIcon.Error);
        }
        finally { _busy = false; }
    }

    // Scrolling capture: pick a region, then scroll + grab + stitch into one tall image and save it.
    // The controller owns the region picker, the in-progress toast, and Esc-to-cancel; it runs on the
    // UI thread (the awaited settle delays keep the app responsive). _busy is held for the whole run.
    private async void StartScrollingCapture()
    {
        if (_freeze.IsActive) _freeze.Resume();
        if (_busy) return;
        _busy = true;
        try
        {
            var controller = new Aquashot.Capture.ScrollingCapture.ScrollingCaptureController(
                _capture, _settings,
                image => { var p = _output.SaveComposite(image, _settings, DateTime.Now); Remember(p); return p; });

            var result = await controller.RunAsync();
            switch (result.Kind)
            {
                case Aquashot.Capture.ScrollingCapture.ScrollResultKind.Saved:
                    _icon.ShowBalloonTip(2500, "Aquashot",
                        $"Scrolling capture saved ({result.Frames} frames): " + result.Path, ToolTipIcon.Info);
                    break;
                case Aquashot.Capture.ScrollingCapture.ScrollResultKind.NothingCaptured:
                    _icon.ShowBalloonTip(2000, "Aquashot", "Scrolling capture: nothing captured.", ToolTipIcon.Warning);
                    break;
                // Cancelled: stay quiet.
            }
        }
        catch (Exception ex)
        {
            _icon.ShowBalloonTip(3000, "Aquashot", "Scrolling capture failed: " + ex.Message, ToolTipIcon.Error);
        }
        finally { _busy = false; }
    }

    // Delayed capture from the toolbar: the overlay closed and handed us its region, so the
    // live desktop is visible again. Wait, then grab + save that exact region — handy for
    // menus, tooltips, and other transient UI you trigger during the countdown.
    private async void DelayedRegionCapture(PixelRect region, int seconds)
    {
        // _busy is still held from StartCapture; keep it until the deferred save finishes.
        _icon.ShowBalloonTip(1000, "Aquashot", $"Capturing in {seconds}s…", ToolTipIcon.Info);
        await Task.Delay(seconds * 1000);
        try
        {
            var frames = _capture.FreezeAll();
            var frame = frames.FirstOrDefault(f => MonitorContains(f.Monitor.Bounds, region))
                        ?? frames.FirstOrDefault();
            if (frame == null) return;
            var path = _output.Save(frame, region, new AnnotationDocument(), _settings, DateTime.Now,
                ClipboardMode.Image, _capWindowTitle, _capAppName);
            Remember(path);
            ShareAfterSave(path);
            _icon.ShowBalloonTip(2000, "Aquashot", "Saved & copied: " + path, ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _icon.ShowBalloonTip(3000, "Aquashot", "Delayed capture failed: " + ex.Message, ToolTipIcon.Error);
        }
        finally { _busy = false; }
    }

    // Parse the configured default clipboard action (case-insensitive), defaulting to Image.
    private static ClipboardMode ParseClipboardAction(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "file" => ClipboardMode.File,
        "path" => ClipboardMode.Path,
        "none" => ClipboardMode.None,
        _      => ClipboardMode.Image,
    };

    // True when a region's centre falls inside a monitor's bounds.
    private static bool MonitorContains(PixelRect monitor, PixelRect region)
    {
        double cx = region.X + region.Width / 2, cy = region.Y + region.Height / 2;
        return cx >= monitor.X && cx < monitor.Right && cy >= monitor.Y && cy < monitor.Bottom;
    }

    // LAST-REGION: store the committed selection (virtual px) so the repeat hotkey can replay it.
    private void RememberLastRegion(PixelRect region)
    {
        var encoded = Aquashot.Selection.RegionCodec.Encode(region);
        if (encoded == _settings.LastRegion) return;
        _settings = _settings with { LastRegion = encoded };
        _store.Save(_settings);
    }

    private void Remember(string path)
    {
        _lastSaved = path;
        _library.Add(path, DateTime.Now);
        if (_settings.EnableOcr) _ = _ocrIndexer.EnqueueAsync(path);
        RebuildRecent();
    }

    // SHARE HOOK: when auto-upload is on, upload the just-saved file in the background, then copy the
    // formatted share link to the clipboard and toast the result. Best-effort: never crashes a save,
    // surfaces failures via the tray balloon only. Images and recordings both flow through here.
    private void ShareAfterSave(string path)
    {
        if (!_settings.ShareAfterSave) return;
        var uploader = Aquashot.Share.ShareService.For(_settings);
        if (uploader == null) return;
        var settings = _settings; // snapshot for the background task
        _ = Task.Run(async () =>
        {
            var result = await uploader.UploadAsync(path, settings);
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (result.Ok && result.Url != null)
                {
                    var text = Aquashot.Share.ShareService.FormatCopy(
                        result.Url, settings.ShareCopyFormat, Path.GetFileName(path));
                    OutputService.CopyPathToClipboard(text);
                    _icon.ShowBalloonTip(2500, "Aquashot", "Link copied: " + result.Url, ToolTipIcon.Info);
                }
                else
                {
                    _icon.ShowBalloonTip(3000, "Aquashot",
                        "Upload failed: " + (result.Error ?? "unknown error"), ToolTipIcon.Warning);
                }
            }));
        });
    }

    private void OpenHistory(string? openPath = null)
    {
        if (_historyWindow is { IsLoaded: true })
        {
            _historyWindow.Activate();
            if (openPath != null) _historyWindow.OpenDetailFor(openPath);
            return;
        }
        _historyWindow = new HistoryWindow(_library, _ocrIndexer, _ocrService,
            _settings.SaveFolder, _settings.EnableOcr, _settings.HistoryThumbSize,
            size =>
            {
                _settings = _settings with { HistoryThumbSize = size };
                _store.Save(_settings);
            },
            // Re-annotate: persist the flattened bitmap as a NEW file, then index it like a capture.
            img => { var p = _output.SaveComposite(img, _settings, DateTime.Now); Remember(p); return p; },
            _settings); // highlighter/spotlight/redact options for the re-annotation editor
        _historyWindow.Show();
        if (openPath != null)
        {
            var p = openPath;
            _historyWindow.Loaded += (_, __) => _historyWindow!.OpenDetailFor(p);
        }
    }

    private void RebuildRecent()
    {
        _recentMenu.DropDownItems.Clear();
        if (_library.Entries.Count == 0)
        {
            var none = _recentMenu.DropDownItems.Add("(nothing captured yet)");
            none.Enabled = false;
            return;
        }
        foreach (var path in _library.Entries.Select(e => e.Path).Take(10))
        {
            var p = path; // capture per-iteration for the click handler
            _recentMenu.DropDownItems.Add(Path.GetFileName(p), null, (_, __) => OpenPath(p));
        }
        _recentMenu.DropDownItems.Add(new ToolStripSeparator());
        _recentMenu.DropDownItems.Add("Open save folder", null, (_, __) => OpenPath(_settings.SaveFolder));
    }

    private void OpenPath(string path)
    {
        try
        {
            if (Directory.Exists(path) || File.Exists(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
                _icon.ShowBalloonTip(2000, "Aquashot", "No longer exists: " + path, ToolTipIcon.Warning);
        }
        catch (Exception ex)
        {
            _icon.ShowBalloonTip(2000, "Aquashot", "Open failed: " + ex.Message, ToolTipIcon.Error);
        }
    }

    private void OpenSettings()
    {
        var win = new SettingsWindow(_settings);
        if (win.ShowDialog() == true)
        {
            // Preserve the remembered last-region across a save (SettingsWindow doesn't surface it).
            _settings = win.Result with { LastRegion = _settings.LastRegion };
            _store.Save(_settings);
            RegisterHotkeys();
            // Push the fresh settings into an already-open history window so it doesn't keep using a
            // stale snapshot (sharing/OCR/save-folder/re-annotate) until it's closed and re-opened.
            if (_historyWindow is { IsLoaded: true }) _historyWindow.ApplySettings(_settings);
        }
    }

    private void Quit()
    {
        Dispose();
        Application.Current.Shutdown();
    }

    // The app icon, loaded from the embedded WPF resource (same .ico as the exe icon).
    // Falls back to the system icon if the resource can't be loaded.
    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var info = Application.GetResourceStream(
                new Uri("pack://application:,,,/Resources/aquashot.ico"));
            if (info?.Stream is { } s)
                using (s) return new System.Drawing.Icon(s, new System.Drawing.Size(32, 32));
        }
        catch { /* fall through to system icon */ }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _hotkey.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
    }
}
