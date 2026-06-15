namespace SnipTool.Capture;

public interface ICaptureService
{
    IReadOnlyList<MonitorInfo> GetMonitors();
    IReadOnlyList<CapturedFrame> FreezeAll();
}
