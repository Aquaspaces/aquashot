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

    public void SetLines(IReadOnlyList<OcrLine> lines)
    {
        _lines = lines ?? Array.Empty<OcrLine>();
        Reflow();
    }

    public string AllText() => string.Join(Environment.NewLine, _lines.Select(l => l.Text));

    private void Reflow()
    {
        TextCanvas.Children.Clear();
        if (_image == null || _lines.Count == 0) return;

        var (scale, offX, offY) = RectMapper.UniformPlacement(
            _image.PixelWidth, _image.PixelHeight, ActualWidth, ActualHeight);

        foreach (var line in _lines)
        {
            var r = RectMapper.MapRect(line.BoxPx, scale, offX, offY);
            if (r.Width <= 0 || r.Height <= 0) continue;
            var tb = new System.Windows.Controls.TextBox
            {
                Text = line.Text,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = System.Windows.Media.Brushes.Transparent, // invisible glyphs over the image; selection highlight still shows
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
