using System.Windows.Media.Imaging;

namespace Aquashot.Capture;

public record CapturedFrame(MonitorInfo Monitor, BitmapSource Bitmap);
