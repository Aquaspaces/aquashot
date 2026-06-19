using System;
using System.Runtime.InteropServices;

namespace Aquashot.Recording.InputHud;

// Low-level keyboard hook (WH_KEYBOARD_LL) that fires a formatted caption ("Ctrl+S", "Enter", …)
// at each key-DOWN, so the HUD can show recent keystrokes during recording. Mirrors GlobalEscHook:
// the delegate is pinned in a field and the hook always chains (never swallows), so the recorded
// app still receives every key. Modifier state is read from GetKeyState so combos render correctly.
public sealed class LowLevelKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    private readonly Action<string> _onCaption;
    private readonly HOOKPROC _proc; // kept in a field so the delegate isn't GC'd
    private IntPtr _hook;

    private delegate IntPtr HOOKPROC(int nCode, IntPtr wParam, IntPtr lParam);

    public LowLevelKeyboardHook(Action<string> onCaption)
    {
        _onCaption = onCaption;
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) // a failed install would leave the keystroke HUD silently inert
            throw new InvalidOperationException(
                $"SetWindowsHookEx(WH_KEYBOARD_LL) failed: {Marshal.GetLastWin32Error()}");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                int vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode is the first field
                var caption = KeystrokeFormatter.Format(vk, CurrentMods());
                if (caption.Length > 0) _onCaption(caption);
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam); // never swallow keys
    }

    // The modifier keys currently held (high bit of GetKeyState marks "down").
    private static KeyMods CurrentMods()
    {
        var m = KeyMods.None;
        if (Down(VK_CONTROL)) m |= KeyMods.Ctrl;
        if (Down(VK_MENU)) m |= KeyMods.Alt;
        if (Down(VK_SHIFT)) m |= KeyMods.Shift;
        if (Down(VK_LWIN) || Down(VK_RWIN)) m |= KeyMods.Win;
        return m;
    }

    private static bool Down(int vk) => (GetKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HOOKPROC lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
