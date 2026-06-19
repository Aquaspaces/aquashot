using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Aquashot.History;

public partial class OcrTextOverlay : System.Windows.Controls.UserControl
{
    private BitmapSource? _image;
    private IReadOnlyList<OcrLine> _lines = Array.Empty<OcrLine>();
    private double _srcW, _srcH; // OCR coordinate space (full-res source px), independent of the displayed bitmap
    public bool ShowText { get; private set; } // false = invisible selectable glyphs; true = readable overlay

    private GifAnimator.Clip? _gif;
    private int _gifIndex;
    private readonly System.Windows.Threading.DispatcherTimer _gifTimer = new();

    public OcrTextOverlay()
    {
        InitializeComponent();
        SizeChanged += (_, __) => Reflow();
        _gifTimer.Tick += AdvanceGif;
    }

    // Show the image immediately; lines arrive later (after async OCR) via SetLines.
    public void SetImage(BitmapSource? image)
    {
        StopGif();
        _image = image;
        Img.Source = image;
        _lines = Array.Empty<OcrLine>();
        _srcW = _srcH = 0;
        TextCanvas.Children.Clear();
    }

    // Play an animated GIF from disk; falls back to a static first frame if decode fails.
    public void SetAnimatedGif(string path)
    {
        StopGif();
        _lines = Array.Empty<OcrLine>();
        TextCanvas.Children.Clear();
        var clip = GifAnimator.Load(path);
        if (clip == null || clip.Frames.Count == 0)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.UriSource = new Uri(path);
                bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze();
                _image = bi; Img.Source = bi;
            }
            catch { _image = null; Img.Source = null; }
            return;
        }
        _gif = clip;
        _gifIndex = 0;
        _image = clip.Frames[0];   // first frame drives OCR box mapping
        Img.Source = clip.Frames[0];
        if (clip.Frames.Count > 1)
        {
            _gifTimer.Interval = TimeSpan.FromMilliseconds(clip.DelaysMs[0]);
            _gifTimer.Start();
        }
    }

    // Play a PRE-DECODED clip (frames decoded off the UI thread by the caller) — avoids the
    // synchronous GifAnimator.Load freeze when stepping onto a GIF in the detail view.
    public void SetAnimatedClip(GifAnimator.Clip clip)
    {
        StopGif();
        _lines = Array.Empty<OcrLine>();
        _srcW = _srcH = 0;
        TextCanvas.Children.Clear();
        _gif = clip;
        _gifIndex = 0;
        _image = clip.Frames[0];
        Img.Source = clip.Frames[0];
        if (clip.Frames.Count > 1)
        {
            _gifTimer.Interval = TimeSpan.FromMilliseconds(clip.DelaysMs[0]);
            _gifTimer.Start();
        }
    }

    private void AdvanceGif(object? sender, EventArgs e)
    {
        if (_gif == null) { StopGif(); return; }
        _gifIndex = (_gifIndex + 1) % _gif.Frames.Count;
        Img.Source = _gif.Frames[_gifIndex];
        _gifTimer.Interval = TimeSpan.FromMilliseconds(_gif.DelaysMs[_gifIndex]);
    }

    private void StopGif()
    {
        _gifTimer.Stop();
        _gif = null;
        _gifIndex = 0;
    }

    // srcW/srcH = the pixel size of the image the OCR boxes were computed on (full-res source), so
    // mapping stays correct even when a downscaled/placeholder bitmap is being displayed.
    public void SetLines(IReadOnlyList<OcrLine> lines, double srcW, double srcH)
    {
        _lines = lines ?? Array.Empty<OcrLine>();
        _srcW = srcW; _srcH = srcH;
        Reflow();
    }

    // Toggle between invisible-but-selectable glyphs and a readable overlay of the recognized text.
    public void SetTextVisible(bool on) { ShowText = on; Reflow(); }

    public string AllText() => string.Join(Environment.NewLine, _lines.Select(l => l.Text));

    private void Reflow()
    {
        TextCanvas.Children.Clear();
        // Map against the OCR SOURCE pixel size (full-res), not the displayed bitmap — the detail
        // view shows a downscaled/placeholder image, so using _image.PixelWidth would mis-scale the
        // boxes. Fall back to the displayed image only if no source size was supplied.
        double srcW = _srcW > 0 ? _srcW : (_image?.PixelWidth ?? 0);
        double srcH = _srcH > 0 ? _srcH : (_image?.PixelHeight ?? 0);
        if (srcW <= 0 || srcH <= 0 || _lines.Count == 0) return;

        var (scale, offX, offY) = RectMapper.UniformPlacement(srcW, srcH, ActualWidth, ActualHeight);

        // Show-text mode paints readable glyphs on a dark plate; otherwise the glyphs are invisible
        // and only the selection highlight shows (select/copy text directly over the image).
        System.Windows.Media.Brush bg = System.Windows.Media.Brushes.Transparent;
        if (ShowText)
        {
            bg = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0xDA, 0x12, 0x12, 0x16));
            bg.Freeze();
        }
        var fg = ShowText ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Transparent;

        foreach (var line in _lines)
        {
            var r = RectMapper.MapRect(line.BoxPx, scale, offX, offY);
            if (r.Width <= 0 || r.Height <= 0) continue;
            var tb = new System.Windows.Controls.TextBox
            {
                Text = line.Text,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = bg,
                Foreground = fg,
                Padding = new Thickness(0),
                FontSize = Math.Max(8, r.Height * 0.8),
                Width = r.Width,
                Height = r.Height,
                Cursor = System.Windows.Input.Cursors.IBeam,
            };
            Canvas.SetLeft(tb, r.X);
            Canvas.SetTop(tb, r.Y);
            TextCanvas.Children.Add(tb);
        }
    }
}
