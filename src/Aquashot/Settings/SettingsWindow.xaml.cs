using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Aquashot.Input;
using Aquashot.Output;
using FolderBrowserDialog = System.Windows.Forms.FolderBrowserDialog;
using WinFormsDialogResult = System.Windows.Forms.DialogResult;
using MessageBox = System.Windows.MessageBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Aquashot.Settings;

public partial class SettingsWindow : Window
{
    public AppSettings Result { get; private set; }
    private string _hotkey;
    private string _scrollingHotkey = "";
    private string _repeatRegionHotkey = "";
    private string _recordRegionHotkey = "";
    private string _captureWindowHotkey = "";
    private string _captureFullScreenHotkey = "";

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        Result = current;
        _hotkey = current.Hotkey;
        HotkeyBox.Text = current.Hotkey;
        FolderBox.Text = current.SaveFolder;
        PatternBox.Text = current.FilenamePattern;
        FormatBox.SelectedIndex =
            string.Equals(current.ImageFormat, "jpg", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        SelectByTag(ClipboardActionBox, current.DefaultClipboardAction, "Image");
        StartupCheck.IsChecked = current.RunAtStartup;
        EnableOcrCheck.IsChecked = current.EnableOcr;

        RecordFpsBox.Text = current.RecordFps.ToString();
        SelectByContent(RecordFormatsBox, current.RecordFormats, "Both");
        EncoderBox.Text = string.IsNullOrWhiteSpace(current.EncoderOverride) ? "Auto" : current.EncoderOverride;
        MaxVideoSizeBox.Text = current.MaxVideoSizeMb.ToString();
        MaxGifSizeBox.Text = current.MaxGifSizeMb.ToString();
        GifMaxFpsBox.Text = current.GifMaxFps.ToString();
        GifMaxWidthBox.Text = current.GifMaxWidth.ToString();
        GifColorsBox.Text = current.GifColors.ToString();
        SelectByContent(GifDitherBox, current.GifDither, "sierra2_4a");

        RecordMicCheck.IsChecked = current.RecordMic;
        RecordSystemCheck.IsChecked = current.RecordSystemAudio;
        SeedDeviceBox(MicDeviceBox, current.MicDeviceName);
        SeedDeviceBox(SystemDeviceBox, current.SystemAudioDeviceName);
        AudioBitrateBox.Text = current.AudioBitrateKbps.ToString();
        SelectByContent(CountdownBox, current.RecordCountdownSeconds.ToString(), "0");
        TrimAfterStopCheck.IsChecked = current.TrimAfterStop;

        ShowClickHighlightCheck.IsChecked = current.ShowClickHighlight;
        ClickHighlightColorBox.Text = current.ClickHighlightColor;
        ClickHighlightRadiusBox.Text = current.ClickHighlightRadius.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ShowKeystrokeHudCheck.IsChecked = current.ShowKeystrokeHud;
        KeystrokeHudSecondsBox.Text = current.KeystrokeHudSeconds.ToString();

        _scrollingHotkey = current.ScrollingCaptureHotkey;
        ScrollingCaptureHotkeyBox.Text = current.ScrollingCaptureHotkey;
        ScrollStepBox.Text = current.ScrollStepPx.ToString();
        ScrollSettleBox.Text = current.ScrollSettleMs.ToString();
        ScrollMaxFramesBox.Text = current.ScrollMaxFrames.ToString();

        _repeatRegionHotkey = current.RepeatLastRegionHotkey;
        RepeatRegionHotkeyBox.Text = current.RepeatLastRegionHotkey;
        _recordRegionHotkey = current.RecordRegionHotkey;
        RecordRegionHotkeyBox.Text = current.RecordRegionHotkey;
        _captureWindowHotkey = current.CaptureWindowHotkey;
        CaptureWindowHotkeyBox.Text = current.CaptureWindowHotkey;
        _captureFullScreenHotkey = current.CaptureFullScreenHotkey;
        CaptureFullScreenHotkeyBox.Text = current.CaptureFullScreenHotkey;

        MagnifierEnabledCheck.IsChecked = current.MagnifierEnabled;
        MagnifierZoomBox.Text = current.MagnifierZoom.ToString(System.Globalization.CultureInfo.InvariantCulture);
        MagnifierSizeBox.Text = current.MagnifierSizePx.ToString();

        HighlighterWidthBox.Text = current.HighlighterWidth.ToString(System.Globalization.CultureInfo.InvariantCulture);
        HighlighterOpacityBox.Text = current.HighlighterOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SpotlightDimBox.Text = current.SpotlightDimColor;
        AutoRedactCheck.IsChecked = current.AutoRedactEnabled;
        SelectByContent(RedactStyleBox, current.RedactStyle, "Blur");
        RedactBlurRadiusBox.Text = current.RedactBlurRadius.ToString(System.Globalization.CultureInfo.InvariantCulture);
        RedactPixelateBlockBox.Text = current.RedactPixelateBlock.ToString();
        RedactPatternsBox.Text = current.RedactPatterns;

        SelectByContent(ShareProviderBox, current.ShareProvider, "None");
        ImgurClientIdBox.Text = current.ImgurClientId;
        CustomUrlBox.Text = current.CustomUploadUrl;
        CustomFieldBox.Text = current.CustomUploadFieldName;
        CustomHeadersBox.Text = current.CustomUploadHeaders;
        CustomResponsePathBox.Text = current.CustomUploadResponseJsonPath;
        SelectByContent(ShareCopyFormatBox, current.ShareCopyFormat, "Url");
        ShareAfterSaveCheck.IsChecked = current.ShareAfterSave;
        UpdateShareEnabled();

        UpdatePreview();
    }

    // Select the ComboBoxItem whose Content matches value (case-insensitive), else the fallback.
    private static void SelectByContent(System.Windows.Controls.ComboBox box, string? value, string fallback)
    {
        string want = string.IsNullOrWhiteSpace(value) ? fallback : value;
        foreach (var obj in box.Items)
        {
            if (obj is ComboBoxItem item &&
                string.Equals(item.Content?.ToString(), want, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
        // fall back to the named default if present
        foreach (var obj in box.Items)
        {
            if (obj is ComboBoxItem item &&
                string.Equals(item.Content?.ToString(), fallback, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
    }

    // Select the ComboBoxItem whose Tag matches value (case-insensitive), else the fallback Tag.
    private static void SelectByTag(System.Windows.Controls.ComboBox box, string? value, string fallback)
    {
        string want = string.IsNullOrWhiteSpace(value) ? fallback : value;
        foreach (var pass in new[] { want, fallback })
            foreach (var obj in box.Items)
                if (obj is ComboBoxItem item &&
                    string.Equals(item.Tag?.ToString(), pass, StringComparison.OrdinalIgnoreCase))
                { box.SelectedItem = item; return; }
    }

    // The Tag of the selected ComboBoxItem, or the fallback.
    private static string TagText(System.Windows.Controls.ComboBox box, string fallback) =>
        (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private static string ComboText(System.Windows.Controls.ComboBox box, string fallback)
    {
        if (box.SelectedItem is ComboBoxItem item && item.Content != null)
            return item.Content.ToString() ?? fallback;
        var t = box.Text;
        return string.IsNullOrWhiteSpace(t) ? fallback : t.Trim();
    }

    // Parse an int from a box, clamped to [min,max]; keep the current value when blank/invalid.
    private static int ParseClamp(string text, int current, int min, int max)
    {
        if (!int.TryParse(text?.Trim(), out int v)) v = current;
        return Math.Clamp(v, min, max);
    }

    // Parse a double from a box, clamped to [min,max]; keep the current value when blank/invalid.
    private static double ParseClampD(string text, double current, double min, double max)
    {
        if (!double.TryParse(text?.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v)) v = current;
        return Math.Clamp(v, min, max);
    }

    private string SelectedFormat() =>
        (FormatBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "png";

    private void UpdatePreview()
    {
        if (PreviewText == null || PatternBox == null) return;
        PreviewText.Text = "Example: " + FilenameGenerator.Generate(PatternBox.Text, SelectedFormat(), DateTime.Now);
    }

    private void Pattern_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();
    private void Format_Changed(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void ShareProvider_Changed(object sender, SelectionChangedEventArgs e) => UpdateShareEnabled();

    // Grey out the provider-specific boxes that don't apply to the current selection.
    private void UpdateShareEnabled()
    {
        if (ImgurPanel == null || CustomPanel == null) return; // not yet built during InitializeComponent
        var provider = ComboText(ShareProviderBox, "None");
        ImgurPanel.IsEnabled = string.Equals(provider, "Imgur", StringComparison.OrdinalIgnoreCase);
        CustomPanel.IsEnabled = string.Equals(provider, "Custom", StringComparison.OrdinalIgnoreCase);
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var mods = Keyboard.Modifiers;
        string s = "";
        if (mods.HasFlag(ModifierKeys.Control)) s += "Ctrl+";
        if (mods.HasFlag(ModifierKeys.Alt)) s += "Alt+";
        if (mods.HasFlag(ModifierKeys.Shift)) s += "Shift+";
        if (mods.HasFlag(ModifierKeys.Windows)) s += "Win+";
        s += key.ToString();
        _hotkey = s;
        HotkeyBox.Text = s;
    }

    // Capture a combo for the scrolling-capture hotkey. Backspace/Delete clears it (blank disables).
    private void ScrollingHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.Back or Key.Delete)
        {
            _scrollingHotkey = "";
            ScrollingCaptureHotkeyBox.Text = "";
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var mods = Keyboard.Modifiers;
        string s = "";
        if (mods.HasFlag(ModifierKeys.Control)) s += "Ctrl+";
        if (mods.HasFlag(ModifierKeys.Alt)) s += "Alt+";
        if (mods.HasFlag(ModifierKeys.Shift)) s += "Shift+";
        if (mods.HasFlag(ModifierKeys.Windows)) s += "Win+";
        s += key.ToString();
        _scrollingHotkey = s;
        ScrollingCaptureHotkeyBox.Text = s;
    }

    // Shared combo-capture for the per-action hotkey boxes: Backspace/Delete clears (blank disables);
    // lone modifier presses are ignored; otherwise returns "Ctrl+Alt+Key". Returns null to ignore.
    private static string? CaptureCombo(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Back or Key.Delete) return "";
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return null;

        var mods = Keyboard.Modifiers;
        string s = "";
        if (mods.HasFlag(ModifierKeys.Control)) s += "Ctrl+";
        if (mods.HasFlag(ModifierKeys.Alt)) s += "Alt+";
        if (mods.HasFlag(ModifierKeys.Shift)) s += "Shift+";
        if (mods.HasFlag(ModifierKeys.Windows)) s += "Win+";
        return s + key.ToString();
    }

    private void RepeatRegionHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (CaptureCombo(e) is { } s) { _repeatRegionHotkey = s; RepeatRegionHotkeyBox.Text = s; }
    }

    private void RecordRegionHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (CaptureCombo(e) is { } s) { _recordRegionHotkey = s; RecordRegionHotkeyBox.Text = s; }
    }

    private void CaptureWindowHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (CaptureCombo(e) is { } s) { _captureWindowHotkey = s; CaptureWindowHotkeyBox.Text = s; }
    }

    private void CaptureFullScreenHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (CaptureCombo(e) is { } s) { _captureFullScreenHotkey = s; CaptureFullScreenHotkeyBox.Text = s; }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new FolderBrowserDialog();
        if (!string.IsNullOrWhiteSpace(FolderBox.Text)) dlg.SelectedPath = FolderBox.Text;
        if (dlg.ShowDialog() == WinFormsDialogResult.OK) FolderBox.Text = dlg.SelectedPath;
    }

    private void DisableSnip_Click(object sender, RoutedEventArgs e)
    {
        bool ok = HotkeyService.TryDisablePrtScSnipMapping();
        MessageBox.Show(ok
            ? "Windows PrtSc→Snipping mapping disabled. PrtSc can now trigger Aquashot."
            : "Could not change the setting.", "Aquashot");
    }

    // Seed an editable device combo with the saved name (kept as the selected text). A blank
    // value means "system default / auto-detect"; an explicit "" placeholder communicates that.
    private static void SeedDeviceBox(System.Windows.Controls.ComboBox box, string saved)
    {
        box.Items.Clear();
        box.Items.Add(new ComboBoxItem { Content = "(system default)", Tag = "" });
        if (!string.IsNullOrWhiteSpace(saved)) box.Text = saved;
        else box.SelectedIndex = 0;
    }

    // The editable combo's chosen device name. The "(system default)" item maps back to "".
    private static string DeviceText(System.Windows.Controls.ComboBox box)
    {
        if (box.SelectedItem is ComboBoxItem item && item.Tag is string tag && tag.Length == 0
            && string.Equals(box.Text, item.Content?.ToString(), StringComparison.Ordinal))
            return "";
        var t = box.Text?.Trim() ?? "";
        return string.Equals(t, "(system default)", StringComparison.OrdinalIgnoreCase) ? "" : t;
    }

    // Probe ffmpeg for dshow audio devices and fill both combos. Best-effort: failures (ffmpeg
    // not bundled, no devices) just leave the current text intact.
    private async void RefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        RefreshDevicesBtn.IsEnabled = false;
        string keepMic = MicDeviceBox.Text, keepSys = SystemDeviceBox.Text;
        try
        {
            var runner = new Aquashot.Capture.FFmpegRunner(
                Aquashot.Capture.FFmpegProvider.Default().EnsureExtracted());
            var devices = await new Aquashot.Recording.AudioDeviceEnumerator().EnumerateAsync(runner);

            FillDeviceBox(MicDeviceBox, devices, keepMic);
            FillDeviceBox(SystemDeviceBox, devices, keepSys);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not list audio devices: " + ex.Message, "Aquashot");
        }
        finally { RefreshDevicesBtn.IsEnabled = true; }
    }

    private static void FillDeviceBox(System.Windows.Controls.ComboBox box,
        System.Collections.Generic.IReadOnlyList<Aquashot.Recording.AudioDevice> devices, string keep)
    {
        box.Items.Clear();
        box.Items.Add(new ComboBoxItem { Content = "(system default)", Tag = "" });
        foreach (var d in devices)
            box.Items.Add(new ComboBoxItem { Content = d.Name, Tag = d.Name });
        box.Text = keep; // preserve whatever the user had typed/selected
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Result = Result with
        {
            Hotkey = _hotkey,
            SaveFolder = FolderBox.Text,
            FilenamePattern = PatternBox.Text,
            ImageFormat = SelectedFormat(),
            DefaultClipboardAction = TagText(ClipboardActionBox, Result.DefaultClipboardAction),
            RunAtStartup = StartupCheck.IsChecked == true,
            EnableOcr = EnableOcrCheck.IsChecked == true,
            RecordFps = ParseClamp(RecordFpsBox.Text, Result.RecordFps, 1, 120),
            RecordFormats = ComboText(RecordFormatsBox, Result.RecordFormats),
            EncoderOverride = string.IsNullOrWhiteSpace(EncoderBox.Text) ? "Auto" : EncoderBox.Text.Trim(),
            MaxVideoSizeMb = ParseClamp(MaxVideoSizeBox.Text, Result.MaxVideoSizeMb, 0, int.MaxValue),
            MaxGifSizeMb = ParseClamp(MaxGifSizeBox.Text, Result.MaxGifSizeMb, 0, int.MaxValue),
            GifMaxFps = ParseClamp(GifMaxFpsBox.Text, Result.GifMaxFps, 1, 120),
            GifMaxWidth = ParseClamp(GifMaxWidthBox.Text, Result.GifMaxWidth, 2, 3840),
            GifColors = ParseClamp(GifColorsBox.Text, Result.GifColors, 2, 256),
            GifDither = ComboText(GifDitherBox, Result.GifDither),
            RecordMic = RecordMicCheck.IsChecked == true,
            RecordSystemAudio = RecordSystemCheck.IsChecked == true,
            MicDeviceName = DeviceText(MicDeviceBox),
            SystemAudioDeviceName = DeviceText(SystemDeviceBox),
            AudioBitrateKbps = ParseClamp(AudioBitrateBox.Text, Result.AudioBitrateKbps, 8, 512),
            RecordCountdownSeconds = ParseClamp(ComboText(CountdownBox, Result.RecordCountdownSeconds.ToString()), Result.RecordCountdownSeconds, 0, 60),
            TrimAfterStop = TrimAfterStopCheck.IsChecked == true,
            ShowClickHighlight = ShowClickHighlightCheck.IsChecked == true,
            ClickHighlightColor = string.IsNullOrWhiteSpace(ClickHighlightColorBox.Text) ? Result.ClickHighlightColor : ClickHighlightColorBox.Text.Trim(),
            ClickHighlightRadius = ParseClampD(ClickHighlightRadiusBox.Text, Result.ClickHighlightRadius, 4, 200),
            ShowKeystrokeHud = ShowKeystrokeHudCheck.IsChecked == true,
            KeystrokeHudSeconds = ParseClamp(KeystrokeHudSecondsBox.Text, Result.KeystrokeHudSeconds, 1, 30),
            ScrollingCaptureHotkey = _scrollingHotkey ?? "",
            ScrollStepPx = ParseClamp(ScrollStepBox.Text, Result.ScrollStepPx, 60, 5000),
            ScrollSettleMs = ParseClamp(ScrollSettleBox.Text, Result.ScrollSettleMs, 0, 5000),
            ScrollMaxFrames = ParseClamp(ScrollMaxFramesBox.Text, Result.ScrollMaxFrames, 1, 500),
            HighlighterWidth = ParseClampD(HighlighterWidthBox.Text, Result.HighlighterWidth, 1, 200),
            HighlighterOpacity = ParseClampD(HighlighterOpacityBox.Text, Result.HighlighterOpacity, 0, 1),
            SpotlightDimColor = string.IsNullOrWhiteSpace(SpotlightDimBox.Text) ? Result.SpotlightDimColor : SpotlightDimBox.Text.Trim(),
            AutoRedactEnabled = AutoRedactCheck.IsChecked == true,
            RedactStyle = ComboText(RedactStyleBox, Result.RedactStyle),
            RedactBlurRadius = ParseClampD(RedactBlurRadiusBox.Text, Result.RedactBlurRadius, 0, 200),
            RedactPixelateBlock = ParseClamp(RedactPixelateBlockBox.Text, Result.RedactPixelateBlock, 2, 200),
            RedactPatterns = RedactPatternsBox.Text ?? "",
            RepeatLastRegionHotkey = _repeatRegionHotkey ?? "",
            RecordRegionHotkey = _recordRegionHotkey ?? "",
            CaptureWindowHotkey = _captureWindowHotkey ?? "",
            CaptureFullScreenHotkey = _captureFullScreenHotkey ?? "",
            MagnifierEnabled = MagnifierEnabledCheck.IsChecked == true,
            MagnifierZoom = ParseClampD(MagnifierZoomBox.Text, Result.MagnifierZoom, 1, 16),
            MagnifierSizePx = ParseClamp(MagnifierSizeBox.Text, Result.MagnifierSizePx, 40, 600),
            ShareProvider = ComboText(ShareProviderBox, Result.ShareProvider),
            ImgurClientId = ImgurClientIdBox.Text?.Trim() ?? "",
            CustomUploadUrl = CustomUrlBox.Text?.Trim() ?? "",
            CustomUploadFieldName = string.IsNullOrWhiteSpace(CustomFieldBox.Text) ? "file" : CustomFieldBox.Text.Trim(),
            CustomUploadHeaders = CustomHeadersBox.Text ?? "",
            CustomUploadResponseJsonPath = string.IsNullOrWhiteSpace(CustomResponsePathBox.Text) ? "$.data.link" : CustomResponsePathBox.Text.Trim(),
            ShareCopyFormat = ComboText(ShareCopyFormatBox, Result.ShareCopyFormat),
            ShareAfterSave = ShareAfterSaveCheck.IsChecked == true
        };

        var reg = new StartupRegistration("Aquashot", Environment.ProcessPath ?? "");
        if (Result.RunAtStartup) reg.Enable(); else reg.Disable();

        DialogResult = true;
    }
}
