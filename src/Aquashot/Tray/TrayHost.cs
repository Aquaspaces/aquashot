using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    private Aquashot.Capture.FFmpegRunner? _ffmpeg;
    private Aquashot.Recording.HardwareEncoderDetector? _encoderDetector;
    private readonly RecentCaptures _recent;
    private readonly ToolStripMenuItem _recentMenu = new("Recent");
    private readonly Aquashot.Freeze.FreezeController _freeze = new();
    private readonly ToolStripMenuItem _freezeItem = new("Freeze desktop");

    public TrayHost()
    {
        _store = new SettingsStore(SettingsStore.DefaultPath());
        _settings = _store.Load();
        _recent = new RecentCaptures(Path.Combine(
            Path.GetDirectoryName(SettingsStore.DefaultPath())!, "recent.json"));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "Aquashot"
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Capture region", null, (_, __) => StartCapture(OverlayWindow.OverlayMode.Region));
        menu.Items.Add("Capture window", null, (_, __) => StartCapture(OverlayWindow.OverlayMode.Window));
        menu.Items.Add("Capture all monitors", null, (_, __) => CaptureAllMonitors());

        var delay = new ToolStripMenuItem("Delayed capture");
        delay.DropDownItems.Add("3 seconds", null, (_, __) => DelayThenCapture(3));
        delay.DropDownItems.Add("5 seconds", null, (_, __) => DelayThenCapture(5));
        delay.DropDownItems.Add("10 seconds", null, (_, __) => DelayThenCapture(10));
        menu.Items.Add(delay);

        menu.Items.Add("Pick color", null, (_, __) => StartColorPick());

        _freezeItem.Click += (_, __) => ToggleFreeze();
        _freeze.Resumed += () => _freezeItem.Text = "Freeze desktop";
        menu.Items.Add(_freezeItem);

        menu.Items.Add(_recentMenu);
        RebuildRecent();

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, __) => OpenSettings());
        menu.Items.Add("Quit", null, (_, __) => Quit());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, __) => StartCapture(OverlayWindow.OverlayMode.Region);

        _hotkey = new HotkeyService();
        _hotkey.Pressed += () => StartCapture(OverlayWindow.OverlayMode.Region);
        _hotkey.FreezePressed += ToggleFreeze;
        _hotkey.RegisterFreeze(_settings.FreezeHotkey);
        if (!_hotkey.Register(_settings.Hotkey))
            _icon.ShowBalloonTip(3000, "Aquashot",
                $"Could not register hotkey '{_settings.Hotkey}'. Open Settings to change it.",
                ToolTipIcon.Warning);

        if (HotkeyService.IsPrtScMappedToSnip())
            _icon.ShowBalloonTip(4000, "Aquashot",
                "PrtSc is mapped to Windows Snipping Tool. Open Settings to disable it so PrtSc opens Aquashot.",
                ToolTipIcon.Info);
    }

    // Freeze the desktop into a static, always-on-top snapshot (looks paused); toggle to resume.
    private void ToggleFreeze()
    {
        if (_freeze.IsActive) { _freeze.Resume(); return; }
        if (_busy) return;
        try
        {
            _freeze.Freeze(_capture.FreezeAll());
            _freezeItem.Text = "Unfreeze desktop";
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
        OverlayController? ctrl = null;
        try
        {
            var frames = _capture.FreezeAll();
            ctrl = new OverlayController { Mode = mode };
            ctrl.Confirmed += (f, r, d) =>
            {
                _busy = false;
                try
                {
                    var path = _output.Save(f, r, d, _settings, DateTime.Now);
                    Remember(path);
                    _icon.ShowBalloonTip(2000, "Aquashot", "Saved & copied: " + path, ToolTipIcon.Info);
                }
                catch (Exception ex)
                {
                    _icon.ShowBalloonTip(3000, "Aquashot", "Save failed: " + ex.Message, ToolTipIcon.Error);
                }
            };
            ctrl.Cancelled += () => _busy = false;
            ctrl.PinRequested += () => _busy = false; // pinned to screen; capture flow is done
            // Recording chosen in the capture toolbar: overlay closes, recording owns _busy.
            ctrl.RecordRequested += (f, r, fmt) => StartRecording(f, r, fmt);
            ctrl.Show(frames);
        }
        catch (Exception ex)
        {
            ctrl?.Close();
            _busy = false;
            _icon.ShowBalloonTip(3000, "Aquashot", "Capture failed: " + ex.Message, ToolTipIcon.Error);
        }
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
            _icon.ShowBalloonTip(2000, "Aquashot", "All monitors saved & copied: " + path, ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _icon.ShowBalloonTip(3000, "Aquashot", "Capture failed: " + ex.Message, ToolTipIcon.Error);
        }
        finally { _busy = false; }
    }

    // Started from the capture overlay when the user picks GIF/MP4 for the selected region.
    // _busy is already true (set by StartCapture); recording owns it until Finished.
    private void StartRecording(CapturedFrame frame, PixelRect region, Aquashot.Recording.RecordFormats formats)
    {
        try { _ffmpeg ??= new Aquashot.Capture.FFmpegRunner(Aquashot.Capture.FFmpegProvider.Default().EnsureExtracted()); }
        catch (Exception ex)
        {
            _busy = false;
            _icon.ShowBalloonTip(4000, "Aquashot",
                "Recording unavailable (ffmpeg not bundled): " + ex.Message, ToolTipIcon.Error);
            return;
        }
        _encoderDetector ??= new Aquashot.Recording.HardwareEncoderDetector(_ffmpeg);
        try
        {
            var rec = new Aquashot.Recording.RecordingController(_ffmpeg, _encoderDetector, _settings);
            rec.Finished += (result, error) =>
            {
                _busy = false;
                if (error != null)
                    _icon.ShowBalloonTip(3000, "Aquashot", "Record failed: " + error, ToolTipIcon.Error);
                else if (result != null)
                {
                    foreach (var file in result.Files) Remember(file);
                    var note = result.SizeCapForced ? " (reduced to fit 50 MB)" : "";
                    _icon.ShowBalloonTip(2500, "Aquashot",
                        "Saved & copied: " + string.Join(", ", result.Files.Select(Path.GetFileName)) + note,
                        ToolTipIcon.Info);
                }
            };
            rec.StartRegion(region, frame.Monitor, formats);
        }
        catch (Exception ex)
        {
            _busy = false;
            _icon.ShowBalloonTip(3000, "Aquashot", "Record failed: " + ex.Message, ToolTipIcon.Error);
        }
    }

    // Capture the same region again after a short delay — handy for grabbing menus,
    // tooltips, and other transient UI that vanishes when you click the tray.
    private async void DelayThenCapture(int seconds)
    {
        if (_busy) return;
        _icon.ShowBalloonTip(1000, "Aquashot", $"Capturing in {seconds}s…", ToolTipIcon.Info);
        await Task.Delay(seconds * 1000);
        StartCapture(OverlayWindow.OverlayMode.Region);
    }

    private void Remember(string path)
    {
        _recent.Add(path);
        RebuildRecent();
    }

    private void RebuildRecent()
    {
        _recentMenu.DropDownItems.Clear();
        if (_recent.Items.Count == 0)
        {
            var none = _recentMenu.DropDownItems.Add("(nothing captured yet)");
            none.Enabled = false;
            return;
        }
        foreach (var path in _recent.Items.Take(10))
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

    // Eyedropper: freeze the screen, hover to preview, click to copy the pixel's hex.
    private void StartColorPick()
    {
        if (_busy) return;
        _busy = true;
        Aquashot.ColorPicker.ColorPickerController? c = null;
        try
        {
            var frames = _capture.FreezeAll();
            c = new Aquashot.ColorPicker.ColorPickerController();
            c.Picked += hex =>
            {
                _busy = false;
                try { System.Windows.Clipboard.SetText(hex); } catch { /* clipboard may be locked */ }
                _icon.ShowBalloonTip(2000, "Aquashot", "Color copied: " + hex, ToolTipIcon.Info);
            };
            c.Cancelled += () => _busy = false;
            c.Show(frames);
        }
        catch (Exception ex)
        {
            c?.Close();
            _busy = false;
            _icon.ShowBalloonTip(3000, "Aquashot", "Color pick failed: " + ex.Message, ToolTipIcon.Error);
        }
    }

    private void OpenSettings()
    {
        var win = new SettingsWindow(_settings);
        if (win.ShowDialog() == true)
        {
            _settings = win.Result;
            _store.Save(_settings);
            _hotkey.Register(_settings.Hotkey);
            _hotkey.RegisterFreeze(_settings.FreezeHotkey);
        }
    }

    private void Quit()
    {
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _hotkey.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
    }
}
