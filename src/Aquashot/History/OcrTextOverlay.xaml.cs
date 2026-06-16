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

    public OcrTextOverlay()
    {
        InitializeComponent();
        SizeChanged += (_, __) => Reflow();
    }

    // Show the image immediately; lines arrive later (after async OCR) via SetLines.
    public void SetImage(BitmapSource? image)
    {
        _image = image;
        Img.Source = image;
        _lines = Array.Empty<OcrLine>();
        TextCanvas.Children.Clear();
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
