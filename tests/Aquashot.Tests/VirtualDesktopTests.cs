using FluentAssertions;
using Aquashot.Capture;
using Aquashot.Selection;
using Xunit;

namespace Aquashot.Tests;

public class VirtualDesktopTests
{
    // Primary 1920x1080 @100%, secondary 2560x1440 @150% to the right.
    private static readonly MonitorInfo Primary = new("p", new PixelRect(0, 0, 1920, 1080), 1.0);
    private static readonly MonitorInfo Secondary = new("s", new PixelRect(1920, 0, 2560, 1440), 1.5);

    [Fact]
    public void Bounds_UnionsAllMonitors()
    {
        var vd = new VirtualDesktop(new[] { Primary, Secondary });
        vd.Bounds.Should().Be(new PixelRect(0, 0, 1920 + 2560, 1440));
    }

    [Fact]
    public void MonitorAt_ReturnsContainingMonitor()
    {
        var vd = new VirtualDesktop(new[] { Primary, Secondary });
        vd.MonitorAt(2000, 10)!.Id.Should().Be("s");
        vd.MonitorAt(10, 10)!.Id.Should().Be("p");
    }

    [Fact]
    public void ToLocal_TranslatesByMonitorOrigin()
    {
        VirtualDesktop.ToLocal(Secondary, 1920, 0).Should().Be((0.0, 0.0));
    }
}
