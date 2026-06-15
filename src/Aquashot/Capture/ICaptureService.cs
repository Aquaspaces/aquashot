namespace Aquashot.Capture;

public interface ICaptureService
{
    IReadOnlyList<MonitorInfo> GetMonitors();
    IReadOnlyList<CapturedFrame> FreezeAll();
}
