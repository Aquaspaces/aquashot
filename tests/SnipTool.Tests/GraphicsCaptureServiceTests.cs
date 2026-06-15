using FluentAssertions;
using SnipTool.Capture;
using Xunit;

namespace SnipTool.Tests;

public class GraphicsCaptureServiceTests
{
    [Fact]
    public void GetMonitors_ReturnsAtLeastOne()
    {
        var svc = new GraphicsCaptureService();
        svc.GetMonitors().Should().NotBeEmpty();
    }

    [Fact]
    public void GetMonitors_AllHavePositiveSizeAndDpi()
    {
        var svc = new GraphicsCaptureService();
        foreach (var m in svc.GetMonitors())
        {
            m.Bounds.Width.Should().BeGreaterThan(0);
            m.Bounds.Height.Should().BeGreaterThan(0);
            m.DpiScale.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void FreezeAll_ReturnsOneFramePerMonitor_WithPositiveSizeBitmaps()
    {
        var svc = new GraphicsCaptureService();
        var monitors = svc.GetMonitors();
        var frames = svc.FreezeAll();
        frames.Should().HaveCount(monitors.Count);
        foreach (var f in frames)
        {
            f.Bitmap.PixelWidth.Should().BeGreaterThan(0);
            f.Bitmap.PixelHeight.Should().BeGreaterThan(0);
        }
    }
}
