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
        StartupCheck.IsChecked = current.RunAtStartup;
        EnableOcrCheck.IsChecked = current.EnableOcr;
        UpdatePreview();
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
            RunAtStartup = StartupCheck.IsChecked == true,
            EnableOcr = EnableOcrCheck.IsChecked == true
        };

        var reg = new StartupRegistration("Aquashot", Environment.ProcessPath ?? "");
        if (Result.RunAtStartup) reg.Enable(); else reg.Disable();

        DialogResult = true;
    }
}
