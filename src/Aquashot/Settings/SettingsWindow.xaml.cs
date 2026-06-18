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

    private string SelectedFormat() =>
        (FormatBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "png";

    private void UpdatePreview()
    {
        if (PreviewText == null || PatternBox == null) return;
        PreviewText.Text = "Example: " + FilenameGenerator.Generate(PatternBox.Text, SelectedFormat(), DateTime.Now);
    }

    private void Pattern_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();
    private void Format_Changed(object sender, SelectionChangedEventArgs e) => UpdatePreview();

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
            GifDither = ComboText(GifDitherBox, Result.GifDither)
        };

        var reg = new StartupRegistration("Aquashot", Environment.ProcessPath ?? "");
        if (Result.RunAtStartup) reg.Enable(); else reg.Disable();

        DialogResult = true;
    }
}
