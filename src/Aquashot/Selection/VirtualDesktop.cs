using System.Linq;
using Aquashot.Capture;

namespace Aquashot.Selection;

public class VirtualDesktop
{
    private readonly IReadOnlyList<MonitorInfo> _monitors;
    public VirtualDesktop(IReadOnlyList<MonitorInfo> monitors) => _monitors = monitors;

    public PixelRect Bounds
    {
        get
        {
            double minX = _monitors.Min(m => m.Bounds.X);
            double minY = _monitors.Min(m => m.Bounds.Y);
            double maxR = _monitors.Max(m => m.Bounds.Right);
            double maxB = _monitors.Max(m => m.Bounds.Bottom);
            return new PixelRect(minX, minY, maxR - minX, maxB - minY);
        }
    }

    public MonitorInfo? MonitorAt(double x, double y) =>
        _monitors.FirstOrDefault(m => m.Bounds.Contains(x, y));

    public static (double, double) ToLocal(MonitorInfo m, double vx, double vy) =>
        (vx - m.Bounds.X, vy - m.Bounds.Y);
}
