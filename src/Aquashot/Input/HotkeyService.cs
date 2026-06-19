using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Aquashot.Input;

// The distinct global-hotkey actions Aquashot can register. Each maps to its own WM_HOTKEY id,
// so several independent combos can be live at once.
public enum HotkeyAction
{
    Capture,            // region capture (the legacy main hotkey)
    Freeze,             // toggle the freeze-desktop overlay
    ScrollingCapture,   // start a scrolling capture
    RepeatLastRegion,   // re-capture the last committed region immediately
    RecordRegion,       // open region capture (user picks Record)
    CaptureWindow,      // window-pick capture
    CaptureFullScreen,  // all-monitors capture
}

public class HotkeyService : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID_BASE = 0x4A12; // distinct id per action: base + (int)action
    private const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private HwndSource? _source;
    private readonly HashSet<HotkeyAction> _registered = new();

    // Fired with the specific action when its combo is pressed.
    public event Action<HotkeyAction>? ActionPressed;

    // Compatibility shims for existing callers (raised when the matching action fires).
    public event Action? Pressed;
    public event Action? FreezePressed;
    public event Action? ScrollingCapturePressed;

    public HotkeyService()
    {
        var p = new HwndSourceParameters("AquashotHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            ParentWindow = HWND_MESSAGE
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    private static int IdFor(HotkeyAction action) => HOTKEY_ID_BASE + (int)action;

    // Register (or re-register) a global hotkey for an action. Blank/invalid disables it.
    // Returns true only when a valid combo was successfully registered.
    public bool Register(HotkeyAction action, string? hotkey)
    {
        Unregister(action);
        if (string.IsNullOrWhiteSpace(hotkey)) return false;
        var (mods, vk) = ParseHotkey(hotkey);
        if (vk == 0) return false;
        if (RegisterHotKey(_source!.Handle, IdFor(action), mods | MOD_NOREPEAT, vk))
        {
            _registered.Add(action);
            return true;
        }
        return false;
    }

    public void Unregister(HotkeyAction action)
    {
        if (_registered.Remove(action) && _source != null)
            UnregisterHotKey(_source.Handle, IdFor(action));
    }

    // ---- Compatibility wrappers (keep existing TrayHost calls compiling) ----

    public bool Register(string hotkey) => Register(HotkeyAction.Capture, hotkey);
    public void Unregister() => Unregister(HotkeyAction.Capture);
    public bool RegisterFreeze(string hotkey) => Register(HotkeyAction.Freeze, hotkey);
    public void UnregisterFreeze() => Unregister(HotkeyAction.Freeze);
    public bool RegisterScrollingCapture(string hotkey) => Register(HotkeyAction.ScrollingCapture, hotkey);
    public void UnregisterScrollingCapture() => Unregister(HotkeyAction.ScrollingCapture);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            int idx = id - HOTKEY_ID_BASE;
            if (idx >= 0 && Enum.IsDefined(typeof(HotkeyAction), idx))
            {
                var action = (HotkeyAction)idx;
                ActionPressed?.Invoke(action);
                switch (action)
                {
                    case HotkeyAction.Capture: Pressed?.Invoke(); break;
                    case HotkeyAction.Freeze: FreezePressed?.Invoke(); break;
                    case HotkeyAction.ScrollingCapture: ScrollingCapturePressed?.Invoke(); break;
                }
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public static (uint mods, uint vk) ParseHotkey(string hotkey)
    {
        uint mods = 0, vk = 0;
        foreach (var partRaw in hotkey.Split('+'))
        {
            var part = partRaw.Trim();
            if (part.Length == 0) continue;
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= MOD_CONTROL; break;
                case "alt": mods |= MOD_ALT; break;
                case "shift": mods |= MOD_SHIFT; break;
                case "win": case "windows": mods |= MOD_WIN; break;
                default:
                    if (Enum.TryParse<Key>(part, true, out var key))
                        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        return (mods, vk);
    }

    private const string KeyboardKey = @"Control Panel\Keyboard";
    private const string SnipValue = "PrintScreenKeyForSnippingEnabled";

    public static bool IsPrtScMappedToSnip()
    {
        using var k = Registry.CurrentUser.OpenSubKey(KeyboardKey);
        var v = k?.GetValue(SnipValue);
        return v is int i && i == 1;
    }

    public static bool TryDisablePrtScSnipMapping()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(KeyboardKey, writable: true);
            if (k == null) return false;
            k.SetValue(SnipValue, 0, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        if (_source != null)
            foreach (var action in new List<HotkeyAction>(_registered))
                UnregisterHotKey(_source.Handle, IdFor(action));
        _registered.Clear();
        _source?.RemoveHook(WndProc);
        _source?.Dispose();
        _source = null;
    }
}
