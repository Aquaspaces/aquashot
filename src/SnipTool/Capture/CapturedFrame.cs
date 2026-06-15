using System.Windows.Media.Imaging;

namespace SnipTool.Capture;

public record CapturedFrame(MonitorInfo Monitor, BitmapSource Bitmap);
