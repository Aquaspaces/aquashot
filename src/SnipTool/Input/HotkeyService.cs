using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;

namespace SnipTool.Input;

public class HotkeyService : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0x4A12;
    private const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private HwndSource? _source;
    private bool _registered;

    public event Action? Pressed;

    public HotkeyService()
    {
        var p = new HwndSourceParameters("SnipToolHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            ParentWindow = HWND_MESSAGE
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    public bool Register(string hotkey)
    {
        Unregister();
        var (mods, vk) = ParseHotkey(hotkey);
        if (vk == 0) return false;
        _registered = RegisterHotKey(_source!.Handle, HOTKEY_ID, mods | MOD_NOREPEAT, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _source != null)
        {
            UnregisterHotKey(_source.Handle, HOTKEY_ID);
            _registered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            Pressed?.Invoke();
            handled = true;
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
        Unregister();
        _source?.RemoveHook(WndProc);
        _source?.Dispose();
        _source = null;
    }
}
