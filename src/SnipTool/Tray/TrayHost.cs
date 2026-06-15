using System;
using SnipTool.Capture;
using SnipTool.Input;
using SnipTool.Output;
using SnipTool.Overlay;
using SnipTool.Settings;
using NotifyIcon = System.Windows.Forms.NotifyIcon;
using ContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using ToolStripSeparator = System.Windows.Forms.ToolStripSeparator;
using ToolTipIcon = System.Windows.Forms.ToolTipIcon;
using SystemIcons = System.Drawing.SystemIcons;
using Application = System.Windows.Application;

namespace SnipTool.Tray;

public class TrayHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly HotkeyService _hotkey;
    private readonly SettingsStore _store;
    private readonly GraphicsCaptureService _capture = new();
    private readonly OutputService _output = new();
    private AppSettings _settings;
    private bool _capturing;

    public TrayHost()
    {
        _store = new SettingsStore(SettingsStore.DefaultPath());
        _settings = _store.Load();

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "Snip Tool"
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Capture region", null, (_, __) => StartCapture(OverlayWindow.OverlayMode.Region));
        menu.Items.Add("Capture window", null, (_, __) => StartCapture(OverlayWindow.OverlayMode.Window));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings…", null, (_, __) => OpenSettings());
        menu.Items.Add("Quit", null, (_, __) => Quit());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, __) => StartCapture(OverlayWindow.OverlayMode.Region);

        _hotkey = new HotkeyService();
        _hotkey.Pressed += () => StartCapture(OverlayWindow.OverlayMode.Region);
        if (!_hotkey.Register(_settings.Hotkey))
            _icon.ShowBalloonTip(3000, "Snip Tool",
                $"Could not register hotkey '{_settings.Hotkey}'. Open Settings to change it.",
                ToolTipIcon.Warning);

        if (HotkeyService.IsPrtScMappedToSnip())
            _icon.ShowBalloonTip(4000, "Snip Tool",
                "PrtSc is mapped to Windows Snipping Tool. Open Settings to disable it so PrtSc opens Snip Tool.",
                ToolTipIcon.Info);
    }

    private void StartCapture(OverlayWindow.OverlayMode mode)
    {
        if (_capturing) return;
        _capturing = true;
        OverlayController? ctrl = null;
        try
        {
            var frames = _capture.FreezeAll();
            ctrl = new OverlayController { Mode = mode };
            ctrl.Confirmed += (f, r, d) =>
            {
                _capturing = false;
                try
                {
                    var path = _output.Save(f, r, d, _settings, DateTime.Now);
                    _icon.ShowBalloonTip(2000, "Snip Tool", "Saved & copied: " + path, ToolTipIcon.Info);
                }
                catch (Exception ex)
                {
                    _icon.ShowBalloonTip(3000, "Snip Tool", "Save failed: " + ex.Message, ToolTipIcon.Error);
                }
            };
            ctrl.Cancelled += () => _capturing = false;
            ctrl.Show(frames);
        }
        catch (Exception ex)
        {
            ctrl?.Close();
            _capturing = false;
            _icon.ShowBalloonTip(3000, "Snip Tool", "Capture failed: " + ex.Message, ToolTipIcon.Error);
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
