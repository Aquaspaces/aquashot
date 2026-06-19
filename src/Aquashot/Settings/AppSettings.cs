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

    // ---- Annotations (Highlighter / Spotlight) ----
    public double HighlighterOpacity { get; init; } = 0.4;          // 0..1, translucent stroke alpha
    public double HighlighterWidth { get; init; } = 18;             // default px stroke for highlighter
    public string SpotlightDimColor { get; init; } = "#A6000000";   // ARGB hex of the dim outside spotlight

    // ---- Magnifier loupe (Group 2 / feature 13) ----
    public bool MagnifierEnabled { get; init; } = true;             // loupe during region selection
    public double MagnifierZoom { get; init; } = 2.0;               // loupe magnification factor
    public int MagnifierSizePx { get; init; } = 140;                // loupe diameter (DIP)

    // ---- Auto-redact (OCR blur/pixelate) ----
    public bool AutoRedactEnabled { get; init; } = false;           // run OCR-driven redaction on capture
    public string RedactStyle { get; init; } = "Blur";              // "Blur" | "Pixelate"
    public double RedactBlurRadius { get; init; } = 8;              // gaussian radius for Blur
    public int RedactPixelateBlock { get; init; } = 12;             // mosaic block px for Pixelate
    public string RedactPatterns { get; init; } =                   // ';'-delimited regexes; empty = redact ALL OCR lines
        @"\b[\w.+-]+@[\w-]+\.[\w.-]+\b;\b(?:\d[ -]*?){13,16}\b";

    // ---- Recording audio (Group 4) ----
    public bool RecordMic { get; init; } = false;                   // capture default/selected mic
    public bool RecordSystemAudio { get; init; } = false;           // capture loopback (system sound)
    public string MicDeviceName { get; init; } = "";                // dshow device name; "" = system default
    public string SystemAudioDeviceName { get; init; } = "";        // loopback dshow device name; "" = auto-detect
    public int AudioBitrateKbps { get; init; } = 160;               // AAC bitrate

    // ---- Countdown (Group 4) ----
    public int RecordCountdownSeconds { get; init; } = 0;           // 0 = off; 3 typical

    // ---- Trim (Group 4) ----
    public bool TrimAfterStop { get; init; } = false;               // prompt to trim the clip before encode

    // ---- Cursor/keystroke HUD (Group 5) ----
    public bool ShowClickHighlight { get; init; } = false;          // ring on mouse click during recording
    public string ClickHighlightColor { get; init; } = "#80FFD400"; // ARGB hex of the click ring
    public double ClickHighlightRadius { get; init; } = 28;         // ring radius px (region-local)
    public bool ShowKeystrokeHud { get; init; } = false;            // on-screen keystroke captions
    public int KeystrokeHudSeconds { get; init; } = 2;              // caption linger time

    // ---- Scrolling capture (Group 5) ----
    public int ScrollStepPx { get; init; } = 600;                   // wheel delta per step (notches *120 derived)
    public int ScrollSettleMs { get; init; } = 350;                 // wait after each scroll before grab
    public int ScrollMaxFrames { get; init; } = 40;                 // safety cap on captured frames
    public string ScrollingCaptureHotkey { get; init; } = "";       // global hotkey; blank disables

    // ---- Last-region repeat + per-action hotkeys (Group 6) ----
    public string LastRegion { get; init; } = "";                   // "X,Y,W,H" virtual px; "" = none yet
    public string RepeatLastRegionHotkey { get; init; } = "";       // e.g. "Ctrl+Alt+R"; blank disables
    public string RecordRegionHotkey { get; init; } = "";           // start region capture (pick Record)
    public string CaptureWindowHotkey { get; init; } = "";          // window-pick capture
    public string CaptureFullScreenHotkey { get; init; } = "";      // all-monitors capture

    // ---- Upload / share (Group 7) ----
    public string ShareProvider { get; init; } = "None";            // "None" | "Imgur" | "Custom"
    public string ImgurClientId { get; init; } = "";                // anonymous-upload client id
    public string CustomUploadUrl { get; init; } = "";              // POST endpoint for "Custom"
    public string CustomUploadFieldName { get; init; } = "file";    // multipart field name
    public string CustomUploadHeaders { get; init; } = "";          // "Key: Val" lines, '\n'-separated
    public string CustomUploadResponseJsonPath { get; init; } = "$.data.link"; // dotted JSON path to the URL
    public string ShareCopyFormat { get; init; } = "Url";           // "Url" | "Markdown" | "Html"
    public bool ShareAfterSave { get; init; } = false;              // auto-upload every save
}
