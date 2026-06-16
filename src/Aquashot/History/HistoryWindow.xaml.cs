using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Aquashot.Settings;

namespace Aquashot.History;

public partial class HistoryWindow : Window, INotifyPropertyChanged
{
    public record Tile(string Path, string Name, string Date, BitmapImage? Thumb);

    private readonly CaptureLibrary _lib;
    private readonly OcrIndexer _indexer;
    private readonly IOcrService _ocr;
    private readonly string _saveFolder;
    private readonly bool _enableOcr;
    private readonly Action<int> _persistThumbSize;

    private List<Tile> _filtered = new();
    private int _detailIndex = -1;

    public event PropertyChangedEventHandler? PropertyChanged;

    private double _tileSize = 200;
    public double TileSize
    {
        get => _tileSize;
        set { _tileSize = value; OnChanged(nameof(TileSize)); OnChanged(nameof(TileImageHeight)); }
    }
    public double TileImageHeight => _tileSize * 0.62;
    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public HistoryWindow(CaptureLibrary lib, OcrIndexer indexer, IOcrService ocr,
        string saveFolder, bool enableOcr, int thumbSize, Action<int> persistThumbSize)
    {
        InitializeComponent();
        DataContext = this;
        _lib = lib; _indexer = indexer; _ocr = ocr; _saveFolder = saveFolder;
        _enableOcr = enableOcr; _persistThumbSize = persistThumbSize;

        TileSize = Math.Clamp(thumbSize, 120, 480);
        SizeSlider.Value = TileSize;
        SizeSlider.ValueChanged += (_, e) => TileSize = e.NewValue;
        SizeSlider.PreviewMouseUp += (_, __) => _persistThumbSize((int)TileSize);

        SearchBox.TextChanged += (_, __) => Refresh(SearchBox.Text);
        Grid.MouseDoubleClick += (_, __) => OpenDetailForSelected();

        BtnPrev.Click += (_, __) => StepDetail(-1);
        BtnNext.Click += (_, __) => StepDetail(+1);
        BtnClose.Click += (_, __) => CloseDetail();
        BtnCopyText.Click += (_, __) => CopyAllText();
        BtnCopyImage.Click += (_, __) => CopyCurrentImage();
        PreviewKeyDown += OnKey;

        Loaded += async (_, __) =>
        {
            Refresh("");
            if (_enableOcr) await _indexer.BackfillAsync(_saveFolder);
            Refresh(SearchBox.Text);
        };
    }

    private void Refresh(string query)
    {
        _filtered = _lib.Search(query).Select(e => new Tile(
            e.Path, Path.GetFileName(e.Path), e.CapturedAt.ToString("g"), Thumb(e.Path))).ToList();
        Grid.ItemsSource = _filtered;
    }

    private static BitmapImage? Thumb(string path)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(path);
            bi.DecodePixelWidth = 480;
            bi.CacheOption = BitmapCacheOption.Default;
            bi.EndInit();
            return bi;
        }
        catch { return null; }
    }

    private void OpenDetailForSelected()
    {
        int i = Grid.SelectedIndex;
        if (i < 0) return;
        ShowDetail(i);
    }

    public void OpenDetailFor(string path)
    {
        int i = _filtered.FindIndex(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) ShowDetail(i);
    }

    private void ShowDetail(int index)
    {
        if (index < 0 || index >= _filtered.Count) return;
        _detailIndex = index;
        var tile = _filtered[index];
        DetailTitle.Text = $"{tile.Name}   {tile.Date}";
        DetailPanel.Visibility = Visibility.Visible;

        try
        {
            var full = new BitmapImage();
            full.BeginInit(); full.UriSource = new Uri(tile.Path);
            full.CacheOption = BitmapCacheOption.OnLoad; full.EndInit(); full.Freeze();
            Overlay.SetImage(full);
        }
        catch { Overlay.SetImage(null); }

        LoadLines(tile.Path);
    }

    private async void LoadLines(string path)
    {
        var lines = await _ocr.RecognizeLinesAsync(path);
        if (_detailIndex >= 0 && _detailIndex < _filtered.Count &&
            string.Equals(_filtered[_detailIndex].Path, path, StringComparison.OrdinalIgnoreCase))
            Overlay.SetLines(lines);
    }

    private void StepDetail(int delta)
    {
        int next = CarouselIndex.Step(_detailIndex, _filtered.Count, delta, wrap: true);
        if (next >= 0) ShowDetail(next);
    }

    private void CloseDetail()
    {
        DetailPanel.Visibility = Visibility.Collapsed;
        _detailIndex = -1;
    }

    private void OnKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DetailPanel.Visibility != Visibility.Visible) return;
        if (e.Key == System.Windows.Input.Key.Escape) { CloseDetail(); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.Left) { StepDetail(-1); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.Right) { StepDetail(+1); e.Handled = true; }
    }

    private void CopyAllText()
    {
        try { var t = Overlay.AllText(); if (!string.IsNullOrEmpty(t)) System.Windows.Clipboard.SetText(t); }
        catch { }
    }

    private void CopyCurrentImage()
    {
        if (_detailIndex < 0) return;
        var path = _filtered[_detailIndex].Path;
        try
        {
            var img = new BitmapImage();
            img.BeginInit(); img.UriSource = new Uri(path);
            img.CacheOption = BitmapCacheOption.OnLoad; img.EndInit();
            System.Windows.Clipboard.SetImage(img);
        }
        catch { }
    }

    private Tile? Selected => Grid.SelectedItem as Tile;

    private void Open_Click(object sender, RoutedEventArgs e) => OpenDetailForSelected();

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
            catch { }
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
        Refresh(SearchBox.Text);
    }
}
