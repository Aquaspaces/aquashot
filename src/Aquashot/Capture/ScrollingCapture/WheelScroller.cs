using System;
using System.Runtime.InteropServices;

namespace Aquashot.Capture.ScrollingCapture;

// Drives the mouse wheel via SendInput so the target region scrolls between grabs. The cursor is
// moved over the region's centre first (most apps scroll the window under the pointer), then a
// wheel-DOWN of the configured delta is injected. PInvoke shapes follow Input/HotkeyService and
// Input/GlobalEscHook conventions.
public sealed class WheelScroller
{
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi; // only the mouse union member is used
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    private const int SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77;

    // Position the cursor at virtual-desktop pixel (vx, vy) so the wheel scrolls that window.
    public void MoveCursor(int vx, int vy)
    {
        // SetCursorPos takes virtual-desktop coordinates directly and is enough for routing the
        // wheel; ABSOLUTE move via SendInput is a fallback for apps that watch raw input.
        SetCursorPos(vx, vy);

        int w = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN));
        int h = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN));
        int ox = GetSystemMetrics(SM_XVIRTUALSCREEN), oy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int ax = (int)((vx - ox) * 65535.0 / w);
        int ay = (int)((vy - oy) * 65535.0 / h);

        var move = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = ax, dy = ay,
                dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK
            }
        };
        SendInput(1, new[] { move }, Marshal.SizeOf<INPUT>());
    }

    // Inject one wheel-DOWN scroll of `deltaPx` raw wheel units (negative wheelData scrolls down;
    // WHEEL_DELTA == 120 == one notch, so deltaPx of 600 ≈ 5 notches). Returns false when the event
    // was not injected (SendInput returned 0 — e.g. blocked by UIPI when the target runs at higher
    // integrity), so the caller can surface a diagnostic instead of silently stalling.
    public bool ScrollDown(int deltaPx)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                mouseData = unchecked((uint)(-Math.Abs(deltaPx))), // negative = scroll toward user (down)
                dwFlags = MOUSEEVENTF_WHEEL
            }
        };
        return SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) != 0;
    }
}
