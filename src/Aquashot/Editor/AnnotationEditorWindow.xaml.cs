using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aquashot.Annotation;
using Aquashot.Selection;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;
using Size = System.Windows.Size;

namespace Aquashot.Editor;

// Standalone re-annotation editor: opens a saved screenshot, draws annotations on a fit-scaled
// view, and hands the flattened (image + annotations) bitmap back to the caller. Saves nothing itself.
public partial class AnnotationEditorWindow : Window
{
    private readonly BitmapSource _source;
    private readonly Action<BitmapSource> _onSave;
    private readonly AnnotationDocument _doc = new();
    private readonly AnnotationLayer _layer;
    private readonly InlineToolbar _toolbar;

    // Fit-scale + letterbox offset that map the displayed image to the host (DIP) coords.
    private double _fitScale = 1;
    private double _offX, _offY;

    private Point _start;
    private bool _dragging;
    private Point _lastDip;
    private List<(double X, double Y)>? _penPoints;
    private bool _sampling;

    private int _selectedIndex = -1;
    private bool _movingSelected;
    private (double X, double Y) _moveLastImg;

    private bool _saved;

    public AnnotationEditorWindow(BitmapSource source, Action<BitmapSource> onSave)
    {
        InitializeComponent();
        _source = source;
        _onSave = onSave;

        // Drawing surface sized to the IMAGE in pixels; a ScaleTransform fits it to the view,
        // so shapes are authored in image-pixel space (same model as the capture overlay).
        _layer = new AnnotationLayer
        {
            Doc = _doc,
            Source = source,
            Width = source.PixelWidth,
            Height = source.PixelHeight,
            IsHitTestVisible = false
        };
        Overlay.Children.Add(_layer);

        _toolbar = new InlineToolbar();
        _toolbar.SetEditorMode();
        _toolbar.ToolChanged += OnToolChanged;
        _toolbar.UndoRequested += () => { _doc.Undo(); ClearSelection(); _layer.Refresh(); };
        _toolbar.RedoRequested += () => { _doc.Redo(); ClearSelection(); _layer.Refresh(); };
        _toolbar.PrimaryClicked += Save;
        _toolbar.CancelRequested += Close;
        _toolbar.EyedropperRequested += () => _sampling = true;
        ToolbarHost.Content = _toolbar;

        // Open clamped to the source size, but no larger than ~80% of the work area.
        var wa = SystemParameters.WorkArea;
        double maxW = wa.Width * 0.8, maxH = wa.Height * 0.8;
        double scale = Math.Min(1.0, Math.Min(maxW / source.PixelWidth, maxH / source.PixelHeight));
        Width = Math.Max(MinWidth, source.PixelWidth * scale + 24);
        Height = Math.Max(MinHeight, source.PixelHeight * scale + 96);

        ImageHost.SizeChanged += (_, __) => Layout();
        Loaded += (_, __) => Layout();
    }

    // Recompute the Uniform fit-scale + letterbox offset and align the drawing layer over the image.
    private void Layout()
    {
        double availW = ImageHost.ActualWidth, availH = ImageHost.ActualHeight;
        if (availW <= 0 || availH <= 0 || _source.PixelWidth == 0 || _source.PixelHeight == 0) return;

        _fitScale = Math.Min(availW / _source.PixelWidth, availH / _source.PixelHeight);
        double dispW = _source.PixelWidth * _fitScale, dispH = _source.PixelHeight * _fitScale;
        _offX = (availW - dispW) / 2;
        _offY = (availH - dispH) / 2;

        _layer.RenderTransform = new ScaleTransform(_fitScale, _fitScale);
        Canvas.SetLeft(_layer, _offX);
        Canvas.SetTop(_layer, _offY);
        _layer.Refresh();
    }

    // Map a pointer position (DIP, in the Overlay canvas) to image-pixel coordinates.
    private (double x, double y) ToImage(Point dip) =>
        ((dip.X - _offX) / _fitScale, (dip.Y - _offY) / _fitScale);

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_sampling)
        {
            _toolbar.SetColor(SampleColorHex(e.GetPosition(Overlay)));
            _sampling = false;
            return;
        }

        var (cx, cy) = ToImage(e.GetPosition(Overlay));
        var tool = _toolbar.CurrentTool;
        string color = _toolbar.CurrentColor;
        double w = _toolbar.CurrentWidth;

        switch (tool)
        {
            case ToolKind.Select:
                int hit = _doc.HitTest(cx, cy);
                _selectedIndex = hit;
                _movingSelected = hit >= 0;
                if (hit >= 0)
                {
                    _moveLastImg = (cx, cy);
                    _dragging = true;
                    Overlay.CaptureMouse();
                }
                UpdateSelectionBox();
                return;
            case ToolKind.Counter:
                _doc.Add(new CounterShape(cx, cy, _doc.NextCounter(), color, w));
                _layer.Refresh();
                return;
            case ToolKind.Text:
                var text = TextPrompt.Ask();
                if (!string.IsNullOrEmpty(text)) { _doc.Add(new TextShape(cx, cy, text!, color, w)); _layer.Refresh(); }
                return;
            case ToolKind.Pen:
                _penPoints = new List<(double, double)> { (cx, cy) };
                _dragging = true;
                Overlay.CaptureMouse();
                return;
            default:
                _start = e.GetPosition(Overlay);
                _dragging = true;
                Overlay.CaptureMouse();
                return;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_toolbar.CurrentTool == ToolKind.Select)
        {
            if (!_dragging || !_movingSelected || _selectedIndex < 0) return;
            var (mx, my) = ToImage(e.GetPosition(Overlay));
            _doc.MoveAt(_selectedIndex, mx - _moveLastImg.X, my - _moveLastImg.Y);
            _moveLastImg = (mx, my);
            UpdateSelectionBox();
            _layer.Refresh();
            return;
        }
        if (!_dragging) return;
        _lastDip = e.GetPosition(Overlay);
        if (_toolbar.CurrentTool == ToolKind.Pen && _penPoints != null)
        {
            var (px, py) = ToImage(_lastDip);
            _penPoints.Add((px, py));
        }
        RebuildPreview();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_toolbar.CurrentTool == ToolKind.Select)
        {
            _movingSelected = false;
            _dragging = false;
            Overlay.ReleaseMouseCapture();
            return;
        }
        if (!_dragging) return;
        _dragging = false;
        Overlay.ReleaseMouseCapture();
        var preview = _layer.Preview;
        _layer.Preview = null;
        if (_toolbar.CurrentTool == ToolKind.Pen && _penPoints != null && _penPoints.Count > 1)
            _doc.Add(new PenShape(_penPoints.ToArray(), _toolbar.CurrentColor, _toolbar.CurrentWidth));
        else if (preview != null)
            _doc.Add(preview);
        _penPoints = null;
        _layer.Refresh();
    }

    // Mouse-wheel adjusts the stroke/annotation size, refreshing any in-progress preview.
    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _toolbar.AdjustWidth(e.Delta > 0 ? 1 : -1);
        RebuildPreview();
        e.Handled = true;
    }

    private void RebuildPreview()
    {
        if (!_dragging) return;
        var (cx, cy) = ToImage(_lastDip);
        var (sx, sy) = ToImage(_start);
        string color = _toolbar.CurrentColor;
        double w = _toolbar.CurrentWidth;
        bool fill = _toolbar.CurrentFill;

        if (_toolbar.CurrentTool == ToolKind.Pen && _penPoints != null)
        {
            _layer.Preview = new PenShape(_penPoints.ToArray(), color, w);
            _layer.Refresh();
            return;
        }

        _layer.Preview = _toolbar.CurrentTool switch
        {
            ToolKind.Rect => MakeRect(sx, sy, cx, cy, color, w, fill),
            ToolKind.Ellipse => MakeEllipse(sx, sy, cx, cy, color, w, fill),
            ToolKind.Line => new LineShape(sx, sy, cx, cy, color, w),
            ToolKind.Arrow => new ArrowShape(sx, sy, cx, cy, color, w),
            _ => null
        };
        _layer.Refresh();
    }

    private static RectShape MakeRect(double sx, double sy, double cx, double cy, string color, double w, bool fill) =>
        new(Math.Min(sx, cx), Math.Min(sy, cy), Math.Abs(cx - sx), Math.Abs(cy - sy), color, w, fill);
    private static EllipseShape MakeEllipse(double sx, double sy, double cx, double cy, string color, double w, bool fill) =>
        new(Math.Min(sx, cx), Math.Min(sy, cy), Math.Abs(cx - sx), Math.Abs(cy - sy), color, w, fill);

    private void OnToolChanged(ToolKind t)
    {
        _sampling = false; // switching tools cancels a pending eyedropper
        ClearSelection();
        _layer.Refresh();
        Cursor = t == ToolKind.Select ? Cursors.Arrow : Cursors.Cross;
    }

    private void ClearSelection()
    {
        _selectedIndex = -1;
        _movingSelected = false;
        _layer.SelectionBox = null;
    }

    private void UpdateSelectionBox()
    {
        _layer.SelectionBox = _selectedIndex >= 0 ? _doc.BoundsAt(_selectedIndex) : null;
        _layer.Refresh();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); return; }
        if (e.Key == Key.Enter) { Save(); return; }
        if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { _doc.Undo(); ClearSelection(); _layer.Refresh(); }
        else if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { _doc.Redo(); ClearSelection(); _layer.Refresh(); }
        else if ((e.Key == Key.Delete || e.Key == Key.Back) && _selectedIndex >= 0)
        {
            _doc.RemoveAt(_selectedIndex);
            ClearSelection();
            _layer.Refresh();
        }
    }

    // Flatten the source + annotations into one bitmap and hand it to the caller, then close.
    private void Save()
    {
        if (_saved) return;
        _saved = true;
        var result = new AnnotationRenderer().Flatten(
            _source, new PixelRect(0, 0, _source.PixelWidth, _source.PixelHeight), _doc.Shapes);
        if (result.CanFreeze && !result.IsFrozen) result.Freeze();
        _onSave(result);
        Close();
    }

    // Sample the source's colour at a point in the Overlay (DIP) and return it as "#RRGGBB".
    private string SampleColorHex(Point pInOverlay)
    {
        var (ix, iy) = ToImage(pInOverlay);
        int x = Math.Clamp((int)ix, 0, _source.PixelWidth - 1);
        int y = Math.Clamp((int)iy, 0, _source.PixelHeight - 1);
        var one = new FormatConvertedBitmap(
            new CroppedBitmap(_source, new Int32Rect(x, y, 1, 1)),
            PixelFormats.Bgra32, null, 0);
        var px = new byte[4];
        one.CopyPixels(px, 4, 0);
        return Aquashot.ColorPicker.ColorHex.Rgb(px[2], px[1], px[0]); // BGRA -> R,G,B
    }
}
