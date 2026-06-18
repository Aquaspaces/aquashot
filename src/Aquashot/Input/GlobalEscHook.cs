using System;
using System.Runtime.InteropServices;

namespace Aquashot.Input;

// Low-level keyboard hook that fires a callback on Esc, even when the foreground window
// isn't ours (the recording region is click-through, so focus can leave the overlay).
// Non-swallowing: always chains to the next hook so Esc still reaches other apps.
public sealed class GlobalEscHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_ESCAPE = 0x1B;

    private readonly Action _onEsc;
    private readonly HOOKPROC _proc; // kept in a field so the delegate isn't GC'd
    private IntPtr _hook;

    private delegate IntPtr HOOKPROC(int nCode, IntPtr wParam, IntPtr lParam);

    public GlobalEscHook(Action onEsc)
    {
        _onEsc = onEsc;
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                int vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode is the first field
                if (vk == VK_ESCAPE) _onEsc();
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam); // never swallow Esc
    }

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

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
