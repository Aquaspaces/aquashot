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
    public int MaxVideoSizeMb { get; init; } = 50;       // MP4 size budget MB; 0 = unlimited
    public int MaxGifSizeMb { get; init; } = 50;         // GIF size budget MB; 0 = unlimited
    public int GifMaxFps { get; init; } = 20;            // GIF fps cap
    public int GifMaxWidth { get; init; } = 800;         // GIF width cap px
    public int GifColors { get; init; } = 256;           // palette size 2..256
    public string GifDither { get; init; } = "sierra2_4a"; // none|bayer|sierra2_4a|floyd_steinberg
    public string DefaultClipboardAction { get; init; } = "Image"; // Image | File | Path | None
    public bool EnableOcr { get; init; } = true;
    public int HistoryCap { get; init; } = 1000;
    public int HistoryThumbSize { get; init; } = 200;
}
