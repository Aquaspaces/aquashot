using System.IO;
using FluentAssertions;
using Aquashot.Settings;
using Xunit;

namespace Aquashot.Tests;

public class SettingsStoreTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"Aquashot-test-{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaults()
    {
        var store = new SettingsStore(TempFile());
        var s = store.Load();
        s.SaveFolder.Should().NotBeNullOrWhiteSpace();
        s.FilenamePattern.Should().Be("Screenshot_{yyyy-MM-dd_HHmmss}");
        s.Hotkey.Should().Be("PrintScreen");
        s.RunAtStartup.Should().BeFalse();
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var path = TempFile();
        var store = new SettingsStore(path);
        var original = store.Load() with { Hotkey = "Ctrl+Alt+S", RunAtStartup = true, SaveFolder = @"C:\Shots", EnableOcr = false, HistoryCap = 250, HistoryThumbSize = 320 };
        store.Save(original);

        var reloaded = new SettingsStore(path).Load();
        reloaded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAnnotationAndRedactFields()
    {
        var path = TempFile();
        var store = new SettingsStore(path);
        var original = store.Load() with
        {
            HighlighterOpacity = 0.55,
            HighlighterWidth = 24,
            SpotlightDimColor = "#80112233",
            AutoRedactEnabled = true,
            RedactStyle = "Pixelate",
            RedactBlurRadius = 11,
            RedactPixelateBlock = 20,
            RedactPatterns = "foo;bar"
        };
        store.Save(original);

        var reloaded = new SettingsStore(path).Load();
        reloaded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsAnnotationRedactDefaults()
    {
        var s = new SettingsStore(TempFile()).Load();
        s.HighlighterOpacity.Should().Be(0.4);
        s.HighlighterWidth.Should().Be(18);
        s.SpotlightDimColor.Should().Be("#A6000000");
        s.AutoRedactEnabled.Should().BeFalse();
        s.RedactStyle.Should().Be("Blur");
        s.RedactBlurRadius.Should().Be(8);
        s.RedactPixelateBlock.Should().Be(12);
        s.RedactPatterns.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Load_JsonMissingNewKeys_YieldsDefaults()
    {
        // A legacy settings file with none of the new keys must still load with documented defaults.
        var path = TempFile();
        File.WriteAllText(path, "{ \"Hotkey\": \"PrintScreen\" }");
        var s = new SettingsStore(path).Load();
        s.HighlighterOpacity.Should().Be(0.4);
        s.RedactStyle.Should().Be("Blur");
        s.AutoRedactEnabled.Should().BeFalse();
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAudioAndCountdownFields()
    {
        var path = TempFile();
        var store = new SettingsStore(path);
        var original = store.Load() with
        {
            RecordMic = true,
            RecordSystemAudio = true,
            MicDeviceName = "Microphone (Realtek)",
            SystemAudioDeviceName = "Stereo Mix",
            AudioBitrateKbps = 192,
            RecordCountdownSeconds = 3,
            TrimAfterStop = true
        };
        store.Save(original);

        var reloaded = new SettingsStore(path).Load();
        reloaded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsAudioCountdownDefaults()
    {
        var s = new SettingsStore(TempFile()).Load();
        s.RecordMic.Should().BeFalse();
        s.RecordSystemAudio.Should().BeFalse();
        s.MicDeviceName.Should().BeEmpty();
        s.SystemAudioDeviceName.Should().BeEmpty();
        s.AudioBitrateKbps.Should().Be(160);
        s.RecordCountdownSeconds.Should().Be(0);
        s.TrimAfterStop.Should().BeFalse();
    }

    [Fact]
    public void SaveThenLoad_RoundTripsHudFields()
    {
        var path = TempFile();
        var store = new SettingsStore(path);
        var original = store.Load() with
        {
            ShowClickHighlight = true,
            ClickHighlightColor = "#AA00FF00",
            ClickHighlightRadius = 36,
            ShowKeystrokeHud = true,
            KeystrokeHudSeconds = 4
        };
        store.Save(original);

        var reloaded = new SettingsStore(path).Load();
        reloaded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsHudDefaults()
    {
        var s = new SettingsStore(TempFile()).Load();
        s.ShowClickHighlight.Should().BeFalse();
        s.ClickHighlightColor.Should().Be("#80FFD400");
        s.ClickHighlightRadius.Should().Be(28);
        s.ShowKeystrokeHud.Should().BeFalse();
        s.KeystrokeHudSeconds.Should().Be(2);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsScrollingCaptureFields()
    {
        var path = TempFile();
        var store = new SettingsStore(path);
        var original = store.Load() with
        {
            ScrollStepPx = 800,
            ScrollSettleMs = 500,
            ScrollMaxFrames = 60,
            ScrollingCaptureHotkey = "Ctrl+Alt+S"
        };
        store.Save(original);

        var reloaded = new SettingsStore(path).Load();
        reloaded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsScrollingCaptureDefaults()
    {
        var s = new SettingsStore(TempFile()).Load();
        s.ScrollStepPx.Should().Be(600);
        s.ScrollSettleMs.Should().Be(350);
        s.ScrollMaxFrames.Should().Be(40);
        s.ScrollingCaptureHotkey.Should().BeEmpty();
    }

    [Fact]
    public void SaveThenLoad_RoundTripsHotkeyAndMagnifierFields()
    {
        var path = TempFile();
        var store = new SettingsStore(path);
        var original = store.Load() with
        {
            LastRegion = "10,20,640,480",
            RepeatLastRegionHotkey = "Ctrl+Alt+R",
            RecordRegionHotkey = "Ctrl+Alt+V",
            CaptureWindowHotkey = "Ctrl+Alt+W",
            CaptureFullScreenHotkey = "Ctrl+Alt+M",
            MagnifierEnabled = false,
            MagnifierZoom = 3.5,
            MagnifierSizePx = 200
        };
        store.Save(original);

        var reloaded = new SettingsStore(path).Load();
        reloaded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsHotkeyAndMagnifierDefaults()
    {
        var s = new SettingsStore(TempFile()).Load();
        s.LastRegion.Should().BeEmpty();
        s.RepeatLastRegionHotkey.Should().BeEmpty();
        s.RecordRegionHotkey.Should().BeEmpty();
        s.CaptureWindowHotkey.Should().BeEmpty();
        s.CaptureFullScreenHotkey.Should().BeEmpty();
        s.MagnifierEnabled.Should().BeTrue();
        s.MagnifierZoom.Should().Be(2.0);
        s.MagnifierSizePx.Should().Be(140);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsShareFields()
    {
        var path = TempFile();
        var store = new SettingsStore(path);
        var original = store.Load() with
        {
            ShareProvider = "Imgur",
            ImgurClientId = "abc123",
            CustomUploadUrl = "https://host/upload",
            CustomUploadFieldName = "upload",
            CustomUploadHeaders = "Authorization: Bearer tok",
            CustomUploadResponseJsonPath = "$.result.url",
            ShareCopyFormat = "Markdown",
            ShareAfterSave = true
        };
        store.Save(original);

        var reloaded = new SettingsStore(path).Load();
        reloaded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsShareDefaults()
    {
        var s = new SettingsStore(TempFile()).Load();
        s.ShareProvider.Should().Be("None");
        s.ImgurClientId.Should().BeEmpty();
        s.CustomUploadUrl.Should().BeEmpty();
        s.CustomUploadFieldName.Should().Be("file");
        s.CustomUploadHeaders.Should().BeEmpty();
        s.CustomUploadResponseJsonPath.Should().Be("$.data.link");
        s.ShareCopyFormat.Should().Be("Url");
        s.ShareAfterSave.Should().BeFalse();
    }
}
