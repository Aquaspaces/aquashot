using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aquashot.Annotation;
using Pen = System.Windows.Media.Pen;
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;

namespace Aquashot.Editor;

public class AnnotationLayer : FrameworkElement
{
    private readonly AnnotationRenderer _renderer = new();
    public AnnotationDocument? Doc { get; set; }
    public Shape? Preview { get; set; }
    public BitmapSource? Source { get; set; }

    // Dashed box around the currently selected shape (crop-local px), or null.
    public Rect? SelectionBox { get; set; }

    public void Refresh() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var list = new List<Shape>();
        if (Doc != null) list.AddRange(Doc.Shapes);
        if (Preview != null) list.Add(Preview);
        // Pass the layer's pixel size so a Spotlight dims the whole crop, not just shape bounds.
        var canvas = new Size(
            double.IsNaN(Width) || Width <= 0 ? ActualWidth : Width,
            double.IsNaN(Height) || Height <= 0 ? ActualHeight : Height);
        _renderer.Draw(dc, list, Source, canvas);

        if (SelectionBox is Rect box && !box.IsEmpty)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x4D, 0xA3, 0xFF)), 2)
            {
                DashStyle = DashStyles.Dash
            };
            pen.Freeze();
            dc.DrawRectangle(null, pen, box);
        }
    }
}
