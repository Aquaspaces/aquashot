using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aquashot.Annotation;

namespace Aquashot.Editor;

public class AnnotationLayer : FrameworkElement
{
    private readonly AnnotationRenderer _renderer = new();
    public AnnotationDocument? Doc { get; set; }
    public Shape? Preview { get; set; }
    public BitmapSource? Source { get; set; }

    public void Refresh() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var list = new List<Shape>();
        if (Doc != null) list.AddRange(Doc.Shapes);
        if (Preview != null) list.Add(Preview);
        _renderer.Draw(dc, list, Source);
    }
}
