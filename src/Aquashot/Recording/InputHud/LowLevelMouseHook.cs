using System;
using System.Runtime.InteropServices;

namespace Aquashot.Recording.InputHud;

// Which mouse button generated a click event for the HUD ring.
public enum MouseBtn { Left, Right, Middle }

// Low-level mouse hook (WH_MOUSE_LL) that fires a callback at each button-DOWN with the screen
// coordinates of the click, so the HUD can draw a ring there during recording. Mirrors
// GlobalEscHook: the delegate is pinned in a field and the hook always chains (never swallows),
// so clicks still reach the app being recorded.
public sealed class LowLevelMouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;

    private readonly Action<int, int, MouseBtn> _onClick;
    private readonly HOOKPROC _proc; // kept in a field so the delegate isn't GC'd
    private IntPtr _hook;

    private delegate IntPtr HOOKPROC(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public LowLevelMouseHook(Action<int, int, MouseBtn> onClick)
    {
        _onClick = onClick;
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) // a failed install would leave the HUD silently inert
            throw new InvalidOperationException(
                $"SetWindowsHookEx(WH_MOUSE_LL) failed: {Marshal.GetLastWin32Error()}");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            MouseBtn? btn = msg switch
            {
                WM_LBUTTONDOWN => MouseBtn.Left,
                WM_RBUTTONDOWN => MouseBtn.Right,
                WM_MBUTTONDOWN => MouseBtn.Middle,
                _ => null
            };
            if (btn is MouseBtn b)
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                _onClick(data.pt.x, data.pt.y, b);
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam); // never swallow clicks
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
