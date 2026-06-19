using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Aquashot.Settings;

namespace Aquashot.History;

public partial class HistoryWindow : Window, INotifyPropertyChanged
{
    // A grid/filmstrip tile. Mutable + observable so the thumbnail can be decoded off the UI
    // thread and filled in after the tile is already bound (placeholder until it arrives).
    public sealed class Tile : INotifyPropertyChanged
    {
        public Tile(string path, string name, string date)
        {
            Path = path; Name = name; Date = date; Kind = MediaKinds.Of(path);
        }

        public string Path { get; }
        public string Name { get; }
        public string Date { get; }
        public MediaKind Kind { get; }

        public bool IsImage => Kind == MediaKind.Image;
        public bool IsGif => Kind == MediaKind.Gif;
        public bool IsVideo => Kind == MediaKind.Video;
        public Visibility VideoBadgeVisibility => IsVideo ? Visibility.Visible : Visibility.Collapsed;

        private BitmapSource? _thumb;
        public BitmapSource? Thumb
        {
            get => _thumb;
            set { _thumb = value; PropertyChanged?.Invoke(this, ThumbChanged); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private static readonly PropertyChangedEventArgs ThumbChanged = new(nameof(Thumb));
    }

    private static readonly string[] DropExts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".mp4" };

    private readonly CaptureLibrary _lib;
    private readonly OcrIndexer _indexer;
    private readonly IOcrService _ocr;
    private readonly string _saveFolder;
    private readonly bool _enableOcr;
    private readonly Action<int> _persistThumbSize;
    private readonly Func<BitmapSource, string>? _reannotateSave;

    private readonly ThumbnailCache _thumbs = new();
    private List<Tile> _filtered = new();
    private int _detailIndex = -1;

    // Debounce timers: keystroke -> rebuild, and detail step -> heavy decode.
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _detailTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };

    // OCR is far heavier than the image decode (full-image read + decode + engine pass), so it gets
    // its own LONGER debounce and a per-file cache — rapid arrow-stepping then never triggers an OCR
    // pass (that per-step OCR was the real source of the lag).
    private readonly DispatcherTimer _ocrTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly Dictionary<string, OcrCacheItem> _ocrCache = new(StringComparer.OrdinalIgnoreCase);
    private sealed record OcrCacheItem(IReadOnlyList<OcrLine> Lines, double SrcW, double SrcH);
    // Cap the detail-view decode width for speed on 4K screenshots; OCR maps with the TRUE source size.
    private const int DetailDecodeCap = 2560;

    // Generation token so a stale async thumbnail decode can't overwrite a fresher Refresh.
    private int _refreshGen;

    // Hover-play: at most one GIF animates at a time.
    private GifAnimator.Clip? _hoverClip;
    private int _hoverFrame;
    private System.Windows.Controls.Image? _hoverImage;
    private Tile? _hoverTile;
    private readonly DispatcherTimer _hoverTimer = new();

    // Drag-out press origin.
    private System.Windows.Point _pressOrigin;
    private bool _maybeDrag;
    private bool _dragging;

    public event PropertyChangedEventHandler? PropertyChanged;

    private double _tileSize = 200;
    public double TileSize
    {
        get => _tileSize;
        set { _tileSize = value; OnChanged(nameof(TileSize)); OnChanged(nameof(TileImageHeight)); }
    }
    public double TileImageHeight => _tileSize * 0.62;
    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    // Decode width sized to roughly the on-screen tile pixel size (kept off a fixed 480).
    private int DecodeWidth => Math.Clamp((int)(TileSize * 2), 160, 480);

    public HistoryWindow(CaptureLibrary lib, OcrIndexer indexer, IOcrService ocr,
        string saveFolder, bool enableOcr, int thumbSize, Action<int> persistThumbSize,
        Func<BitmapSource, string>? reannotateSave = null)
    {
        InitializeComponent();
        DataContext = this;
        _lib = lib; _indexer = indexer; _ocr = ocr; _saveFolder = saveFolder;
        _enableOcr = enableOcr; _persistThumbSize = persistThumbSize;
        _reannotateSave = reannotateSave;

        TileSize = Math.Clamp(thumbSize, 120, 480);
        SizeSlider.Value = TileSize;
        SizeSlider.ValueChanged += (_, e) => TileSize = e.NewValue;
        SizeSlider.PreviewMouseUp += (_, __) => _persistThumbSize((int)TileSize);

        // Debounced search: each keystroke restarts a 200ms timer instead of rebuilding the list.
        SearchBox.TextChanged += (_, __) => { _searchTimer.Stop(); _searchTimer.Start(); };
        _searchTimer.Tick += (_, __) => { _searchTimer.Stop(); Refresh(); };

        // File-type filter chips: changing the filter re-applies it combined with the query.
        FilterAll.Checked += (_, __) => Refresh();
        FilterImages.Checked += (_, __) => Refresh();
        FilterGif.Checked += (_, __) => Refresh();
        FilterVideo.Checked += (_, __) => Refresh();

        Grid.MouseDoubleClick += (_, __) => OpenDetailForSelected();
        Grid.PreviewMouseLeftButtonDown += Grid_PreviewMouseLeftButtonDown;
        Grid.PreviewMouseMove += Grid_PreviewMouseMove;
        Grid.PreviewMouseLeftButtonUp += (_, __) => { _maybeDrag = false; };

        BtnPrev.Click += (_, __) => StepDetail(-1);
        BtnNext.Click += (_, __) => StepDetail(+1);
        BtnClose.Click += (_, __) => CloseDetail();
        BtnCopyText.Click += (_, __) => CopyAllText();
        BtnCopyImage.Click += (_, __) => CopyCurrentImage();
        BtnEdit.Click += (_, __) => EditCurrent();
        ShowTextCheck.Click += (_, __) => Overlay.SetTextVisible(ShowTextCheck.IsChecked == true);
        PreviewKeyDown += OnKey;

        _detailTimer.Tick += (_, __) => { _detailTimer.Stop(); LoadDetailHeavy(); };
        _ocrTimer.Tick += (_, __) => { _ocrTimer.Stop(); LoadOcr(); };
        _hoverTimer.Tick += AdvanceHover;

        // Drop-in support.
        AllowDrop = true;
        DragOver += Window_DragOver;
        Drop += Window_Drop;

        Loaded += async (_, __) =>
        {
            Refresh();
            if (_enableOcr) await _indexer.BackfillAsync(_saveFolder);
            Refresh();
        };
    }

    private MediaFilter CurrentFilter()
    {
        if (FilterImages.IsChecked == true) return MediaFilter.Images;
        if (FilterGif.IsChecked == true) return MediaFilter.Gif;
        if (FilterVideo.IsChecked == true) return MediaFilter.Video;
        return MediaFilter.All;
    }

    private void Refresh()
    {
        var query = SearchBox?.Text ?? "";
        var filter = CurrentFilter();
        var gen = ++_refreshGen; // invalidate in-flight thumbnail decodes from a previous Refresh

        // Preserve the detail target across a rebuild so search-while-open lands on the same item.
        var openPath = _detailIndex >= 0 && _detailIndex < _filtered.Count ? _filtered[_detailIndex].Path : null;

        _filtered = _lib.Search(query)
            .Where(e => MediaKinds.Matches(filter, e.Path))
            .Select(e => new Tile(e.Path, Path.GetFileName(e.Path), e.CapturedAt.ToString("g")))
            .ToList();

        // Reuse already-decoded thumbnails synchronously; decode the rest off the UI thread.
        int w = DecodeWidth;
        foreach (var tile in _filtered)
        {
            tile.Thumb = _thumbs.TryGet(tile.Path, w);
            if (tile.Thumb == null) FillThumbAsync(tile, w, gen);
        }

        Grid.ItemsSource = _filtered;
        Filmstrip.ItemsSource = _filtered;

        if (DetailPanel.Visibility == Visibility.Visible)
        {
            int i = openPath == null ? -1 : _filtered.FindIndex(
                t => string.Equals(t.Path, openPath, StringComparison.OrdinalIgnoreCase));
            if (i < 0) CloseDetail();
            else { _detailIndex = i; SyncFilmstripSelection(); }
        }
    }

    private async void FillThumbAsync(Tile tile, int decodeWidth, int gen)
    {
        var bi = await _thumbs.GetAsync(tile.Path, decodeWidth);
        if (gen == _refreshGen && bi != null) tile.Thumb = bi; // ignore stale decodes
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

    // Light part of showing a detail: index, title, filmstrip highlight — instant on every step.
    // The heavy full-image decode + OCR lines are debounced and run on the final landed index.
    private void ShowDetail(int index)
    {
        if (index < 0 || index >= _filtered.Count) return;
        StopHover();
        _detailIndex = index;
        var tile = _filtered[index];
        DetailTitle.Text = $"{tile.Name}   {tile.Date}";
        Grid.Visibility = Visibility.Collapsed; // hide the gallery so it can't take focus/clicks
        DetailPanel.Visibility = Visibility.Visible;
        BtnEdit.IsEnabled = tile.IsImage && _reannotateSave != null;
        SyncFilmstripSelection();

        // Instant feedback: show the already-decoded thumbnail now so every arrow press updates the
        // view immediately. The full-res image (~120ms) and the OCR overlay (~350ms) fill in after
        // the user settles, and are skipped entirely while stepping fast.
        if (tile.Thumb != null) Overlay.SetImage(tile.Thumb);

        _detailTimer.Stop(); _detailTimer.Start();
        _ocrTimer.Stop(); _ocrTimer.Start();
    }

    // Runs ~120ms after the user settles on an index. Decodes off the UI thread and guards by the
    // landed index so a fast arrow run only pays for the final image.
    private async void LoadDetailHeavy()
    {
        int index = _detailIndex;
        if (index < 0 || index >= _filtered.Count) return;
        var tile = _filtered[index];

        if (tile.IsGif)
        {
            var gpath = tile.Path;
            var clip = await Task.Run(() => GifAnimator.Load(gpath)); // decode frames off the UI thread
            if (_detailIndex != index) return; // stepped away; drop this stale GIF
            if (clip != null && clip.Frames.Count > 0) Overlay.SetAnimatedClip(clip);
            else Overlay.SetImage(DecodeForDisplay(gpath));
        }
        else
        {
            var path = tile.Path;
            var full = await Task.Run(() => DecodeForDisplay(path));
            if (_detailIndex != index) return; // user already stepped away; drop this stale image
            Overlay.SetImage(full);
            // SetImage clears the overlay; re-apply cached OCR lines, mapped with the TRUE source
            // dims so they line up even though the displayed bitmap is downscaled.
            if (_ocrCache.TryGetValue(path, out var oc)) Overlay.SetLines(oc.Lines, oc.SrcW, oc.SrcH);
        }
    }

    // Decode the detail image capped to DetailDecodeCap px wide (a big speedup on 4K screenshots).
    // OCR alignment is unaffected because the overlay maps boxes with the true source dimensions.
    private static BitmapSource? DecodeForDisplay(string path)
    {
        try
        {
            int srcW = SourcePixelSize(path).w is var w && w > 0 ? (int)w : 0;
            var bi = new BitmapImage();
            bi.BeginInit(); bi.UriSource = new Uri(path);
            if (srcW > DetailDecodeCap) bi.DecodePixelWidth = DetailDecodeCap;
            bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    // Header-only read of the source pixel dimensions (the coordinate space of the OCR boxes).
    private static (double w, double h) SourcePixelSize(string path)
    {
        try
        {
            var dec = BitmapDecoder.Create(new Uri(path),
                BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var f = dec.Frames[0];
            return (f.PixelWidth, f.PixelHeight);
        }
        catch { return (0, 0); }
    }

    // Runs only after the OCR settle (~350ms) on the landed image, and caches per file, so browsing
    // never pays for OCR. Cached hits are instant; first-time recognition runs once off the engine.
    private async void LoadOcr()
    {
        int index = _detailIndex;
        if (index < 0 || index >= _filtered.Count) return;
        var tile = _filtered[index];
        if (tile.IsGif) return; // GIFs aren't text-indexed in the detail overlay
        var path = tile.Path;

        if (_ocrCache.TryGetValue(path, out var cached))
        { Overlay.SetLines(cached.Lines, cached.SrcW, cached.SrcH); return; }

        var lines = await _ocr.RecognizeLinesAsync(path);
        var (sw, sh) = await Task.Run(() => SourcePixelSize(path)); // OCR coordinate space (full-res)
        var item = new OcrCacheItem(lines, sw, sh);
        _ocrCache[path] = item;
        if (_detailIndex == index && index >= 0 && index < _filtered.Count &&
            string.Equals(_filtered[index].Path, path, StringComparison.OrdinalIgnoreCase))
            Overlay.SetLines(item.Lines, item.SrcW, item.SrcH);
    }

    private void StepDetail(int delta)
    {
        int next = CarouselIndex.Step(_detailIndex, _filtered.Count, delta, wrap: true);
        if (next >= 0) ShowDetail(next);
    }

    private void CloseDetail()
    {
        _detailTimer.Stop();
        _ocrTimer.Stop();
        DetailPanel.Visibility = Visibility.Collapsed;
        Grid.Visibility = Visibility.Visible;
        _detailIndex = -1;
    }

    // ===== Filmstrip =====

    private void SyncFilmstripSelection()
    {
        if (_detailIndex < 0 || _detailIndex >= _filtered.Count) return;
        Filmstrip.SelectedIndex = _detailIndex;
        // Scroll the current item into view once the container is realized.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (_detailIndex >= 0 && _detailIndex < _filtered.Count)
                Filmstrip.ScrollIntoView(_filtered[_detailIndex]);
        }));
    }

    private void Filmstrip_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int i = Filmstrip.SelectedIndex;
        if (i >= 0 && i != _detailIndex) ShowDetail(i);
    }

    // Mouse wheel scrolls the strip horizontally instead of doing nothing in a horizontal list.
    private void Filmstrip_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = FindScrollViewer(Filmstrip);
        if (sv == null) return;
        sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
    }

    private void OnKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DetailPanel.Visibility != Visibility.Visible) return;
        if (e.Key == Key.Escape) { CloseDetail(); e.Handled = true; }
        else if (e.Key == Key.Left) { StepDetail(-1); e.Handled = true; }
        else if (e.Key == Key.Right) { StepDetail(+1); e.Handled = true; }
    }

    private void CopyAllText()
    {
        try { var t = Overlay.AllText(); if (!string.IsNullOrEmpty(t)) System.Windows.Clipboard.SetText(t); }
        catch { }
    }

    private void CopyCurrentImage()
    {
        if (_detailIndex < 0 || _detailIndex >= _filtered.Count) return;
        CopyImageToClipboard(_filtered[_detailIndex].Path);
    }

    private static void CopyImageToClipboard(string path)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit(); img.UriSource = new Uri(path);
            img.CacheOption = BitmapCacheOption.OnLoad; img.EndInit(); img.Freeze();
            System.Windows.Clipboard.SetImage(img);
        }
        catch { }
    }

    // ===== Hover-play GIFs in the grid (one at a time) =====

    private async void Tile_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not Tile tile) return;
        if (!tile.IsGif) return;
        var img = FindTileImage(fe);
        if (img == null) return;

        // Mark hover intent, then decode the GIF OFF the UI thread (RenderTargetBitmap per frame is
        // heavy and would jank the hover). A fast leave / move to another tile cancels via the
        // intent check below.
        StopHover();
        _hoverTile = tile; _hoverImage = img;
        var loadFor = tile;
        var clip = await Task.Run(() => GifAnimator.Load(loadFor.Path));
        if (!ReferenceEquals(_hoverTile, loadFor) || !ReferenceEquals(_hoverImage, img) ||
            !ReferenceEquals(img.DataContext, loadFor))
            return; // pointer already moved away or the container was recycled while decoding
        if (clip == null || clip.Frames.Count == 0) { StopHover(); return; }
        _hoverClip = clip; _hoverFrame = 0;
        img.Source = clip.Frames[0];
        if (clip.Frames.Count > 1)
        {
            _hoverTimer.Interval = TimeSpan.FromMilliseconds(clip.DelaysMs[0]);
            _hoverTimer.Start();
        }
    }

    private void Tile_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Tile tile && ReferenceEquals(tile, _hoverTile))
            StopHover();
    }

    private void AdvanceHover(object? sender, EventArgs e)
    {
        // Stop if the container was recycled to a different tile — virtualization can swap the
        // element's DataContext without firing MouseLeave, which would leak the timer and paint
        // the old GIF's frames onto a now-different tile.
        if (_hoverClip == null || _hoverImage == null || !ReferenceEquals(_hoverImage.DataContext, _hoverTile))
        { StopHover(); return; }
        _hoverFrame = (_hoverFrame + 1) % _hoverClip.Frames.Count;
        _hoverImage.Source = _hoverClip.Frames[_hoverFrame];
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(_hoverClip.DelaysMs[_hoverFrame]);
    }

    private void StopHover()
    {
        _hoverTimer.Stop();
        // Re-attach the Thumb binding we overrode with a local frame, so the element again tracks
        // whatever tile it currently holds (correct even if virtualization recycled it onto a
        // different tile while we were animating).
        if (_hoverImage != null)
            _hoverImage.SetBinding(System.Windows.Controls.Image.SourceProperty,
                new System.Windows.Data.Binding(nameof(Tile.Thumb)));
        _hoverClip = null; _hoverImage = null; _hoverTile = null; _hoverFrame = 0;
    }

    private static System.Windows.Controls.Image? FindTileImage(DependencyObject root)
    {
        if (root is System.Windows.Controls.Image img && img.Name == "TileImage") return img;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var found = FindTileImage(VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
    }

    // ===== Drag OUT =====

    private void Grid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressOrigin = e.GetPosition(this);
        _maybeDrag = TileAt(e.OriginalSource) != null;
    }

    private void Grid_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_maybeDrag || _dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _pressOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _pressOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        if (TileAt(e.OriginalSource) is not { } tile || !File.Exists(tile.Path)) { _maybeDrag = false; return; }

        _dragging = true;
        try
        {
            var data = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { tile.Path });
            System.Windows.DragDrop.DoDragDrop(Grid, data, System.Windows.DragDropEffects.Copy);
        }
        catch { }
        finally { _dragging = false; _maybeDrag = false; }
    }

    // The tile under an event's original source (walks up the tree), or null. Uses the logical
    // parent so non-visual content elements (TextBlock Runs) don't trip VisualTreeHelper.
    private static Tile? TileAt(object? source)
    {
        var d = source as DependencyObject;
        while (d != null)
        {
            if (d is FrameworkElement fe && fe.DataContext is Tile t) return t;
            d = (d is Visual or System.Windows.Media.Media3D.Visual3D) ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    // ===== Drop IN =====

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = HasDroppableFiles(e) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private static bool HasDroppableFiles(System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return false;
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files) return false;
        return files.Any(f => DropExts.Contains(Path.GetExtension(f).ToLowerInvariant()));
    }

    private async void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!HasDroppableFiles(e)) return;
        var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop)!;
        bool added = false;
        foreach (var src in files.Where(f => DropExts.Contains(Path.GetExtension(f).ToLowerInvariant())))
        {
            try
            {
                if (!File.Exists(src)) continue;
                Directory.CreateDirectory(_saveFolder);
                var dest = NonCollidingPath(_saveFolder, Path.GetFileName(src));
                // Defense-in-depth: never let a crafted name escape the save folder.
                if (!Path.GetFullPath(dest).StartsWith(
                        Path.GetFullPath(_saveFolder), StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(src, dest);
                _lib.Add(dest, DateTime.Now);
                if (_enableOcr) await _indexer.EnqueueAsync(dest);
                added = true;
            }
            catch { /* skip files we can't import */ }
        }
        if (added) Refresh();
    }

    // Append " (n)" before the extension until the path is free, so an import never overwrites.
    private static string NonCollidingPath(string folder, string fileName)
    {
        var dest = Path.Combine(folder, fileName);
        if (!File.Exists(dest)) return dest;
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (int i = 2; ; i++)
        {
            dest = Path.Combine(folder, $"{name} ({i}){ext}");
            if (!File.Exists(dest)) return dest;
        }
    }

    // ===== Re-annotate (save as NEW file, non-destructive) =====

    private void EditCurrent()
    {
        if (_detailIndex < 0 || _detailIndex >= _filtered.Count) return;
        EditTile(_filtered[_detailIndex]);
    }

    private void EditTile(Tile tile)
    {
        if (_reannotateSave == null || !tile.IsImage || !File.Exists(tile.Path)) return;
        BitmapSource source;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit(); bi.UriSource = new Uri(tile.Path);
            bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze();
            source = bi;
        }
        catch { return; }

        var editor = new Aquashot.Editor.AnnotationEditorWindow(source, flattened =>
        {
            try
            {
                if (flattened.CanFreeze && !flattened.IsFrozen) flattened.Freeze();
                var saved = _reannotateSave(flattened); // persists + library.Add + OCR enqueue (TrayHost)
                Refresh();
                OpenDetailFor(saved);
            }
            catch { }
        });
        editor.Owner = this;
        editor.Show();
    }

    // ===== Context menu (item DataTemplate; clicked tile is the menu's DataContext) =====

    private static Tile? TileOf(object sender) => (sender as FrameworkElement)?.DataContext as Tile;

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (TileOf(sender) is { } t) OpenDetailFor(t.Path);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (TileOf(sender) is { } t) EditTile(t);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (TileOf(sender) is { } t && File.Exists(t.Path)) CopyImageToClipboard(t.Path);
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (TileOf(sender) is { } t && File.Exists(t.Path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{t.Path}\"") { UseShellExecute = true });
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (TileOf(sender) is not { } t) return;
        var ok = System.Windows.MessageBox.Show(this, $"Delete {t.Name}?", "Aquashot",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ok != MessageBoxResult.Yes) return;
        try { if (File.Exists(t.Path)) File.Delete(t.Path); } catch { }
        Refresh();
    }
}
