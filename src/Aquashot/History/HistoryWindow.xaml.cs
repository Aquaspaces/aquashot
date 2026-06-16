using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Aquashot.History;

public partial class HistoryWindow : Window
{
    public record Tile(string Path, string Name, string Date, BitmapImage? Thumb);

    private readonly CaptureLibrary _lib;
    private readonly OcrIndexer _indexer;
    private readonly string _saveFolder;

    public HistoryWindow(CaptureLibrary lib, OcrIndexer indexer, string saveFolder)
    {
        InitializeComponent();
        _lib = lib; _indexer = indexer; _saveFolder = saveFolder;
        SearchBox.TextChanged += (_, __) => Refresh(SearchBox.Text);
        Grid.MouseDoubleClick += (_, __) => OpenSelected();
        Loaded += async (_, __) => { Refresh(""); await _indexer.BackfillAsync(_saveFolder); Refresh(SearchBox.Text); };
    }

    private void Refresh(string query)
    {
        Grid.ItemsSource = _lib.Search(query).Select(e => new Tile(
            e.Path, Path.GetFileName(e.Path), e.CapturedAt.ToString("g"), Thumb(e.Path))).ToList();
    }

    private static BitmapImage? Thumb(string path)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(path);
            bi.DecodePixelWidth = 240;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    private Tile? Selected => Grid.SelectedItem as Tile;

    private void OpenSelected()
    {
        if (Selected is { } t && File.Exists(t.Path))
            Process.Start(new ProcessStartInfo(t.Path) { UseShellExecute = true });
    }

    private void Open_Click(object sender, RoutedEventArgs e) => OpenSelected();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } t && File.Exists(t.Path))
        {
            try
            {
                var img = new BitmapImage();
                img.BeginInit(); img.UriSource = new Uri(t.Path);
                img.CacheOption = BitmapCacheOption.OnLoad; img.EndInit();
                System.Windows.Clipboard.SetImage(img);
            }
            catch { /* clipboard may be locked */ }
        }
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } t && File.Exists(t.Path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{t.Path}\"") { UseShellExecute = true });
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } t) return;
        var ok = System.Windows.MessageBox.Show(this, $"Delete {t.Name}?", "Aquashot",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes) return;
        try { if (File.Exists(t.Path)) File.Delete(t.Path); } catch { }
        // CaptureLibrary prunes missing files on next load; reflect immediately by re-reading from a fresh search.
        Refresh(SearchBox.Text);
    }
}
