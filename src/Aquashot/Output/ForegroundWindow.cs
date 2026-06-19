using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Aquashot.Output;

// Reads the current foreground window's caption + owning process name, for the {window}/{app}
// filename tokens. Capture the title at capture time (by save time the foreground may be ours).
public static class ForegroundWindow
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr h, out int pid);

    // The foreground window caption ("" if there's no foreground window or it has no title).
    public static string Title()
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return "";
            int len = GetWindowTextLength(h);
            if (len <= 0) return "";
            var sb = new StringBuilder(len + 1);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }
        catch { return ""; }
    }

    // The foreground window's owning process name, without extension ("" on failure).
    public static string AppName()
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return "";
            GetWindowThreadProcessId(h, out int pid);
            if (pid == 0) return "";
            using var p = Process.GetProcessById(pid);
            return p.ProcessName; // already extension-free
        }
        catch { return ""; }
    }
}
