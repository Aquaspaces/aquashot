using System.IO;

namespace Aquashot.Settings;

public record AppSettings
{
    public string SaveFolder { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
    public string FilenamePattern { get; init; } = "Screenshot_{yyyy-MM-dd_HHmmss}";
    public string ImageFormat { get; init; } = "png";
    public string Hotkey { get; init; } = "PrintScreen";
    public string FreezeHotkey { get; init; } = "Ctrl+Alt+F"; // toggles freeze-desktop overlay; blank disables
    public bool RunAtStartup { get; init; } = false;
    public string DefaultColor { get; init; } = "#FF3B30";
    public double DefaultStrokeWidth { get; init; } = 3;
    public int RecordFps { get; init; } = 30;
    public string EncoderOverride { get; init; } = "Auto"; // "Auto" or an ffmpeg encoder name
    public string RecordFormats { get; init; } = "Both";   // "Mp4" | "Gif" | "Both"
    public bool EnableOcr { get; init; } = true;
    public int HistoryCap { get; init; } = 1000;
    public int HistoryThumbSize { get; init; } = 200;
}
