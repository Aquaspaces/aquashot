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
using Color = System.Windows.Media.Color;

namespace Aquashot.Editor;

// Standalone re-annotation editor: opens a saved screenshot, draws annotations on a fit-scaled
// view, and hands the flattened (image + annotations) bitmap back to the caller. Saves nothing itself.
public partial class AnnotationEditorWindow : Window
{
    private BitmapSource _source;
    private readonly Action<BitmapSource> _onSave;
    private readonly AnnotationDocument _doc = new();
    private readonly AnnotationLayer _layer;
    private readonly InlineToolbar _toolbar;
    private readonly Aquashot.Settings.AppSettings _settings;
    private readonly Aquashot.History.IOcrService? _ocr;

    // Crop state: while active, a drag defines the crop rect (image px); Enter/click applies it.
    private bool _cropMode;
    private (double x, double y)? _cropStart;
    private Point? _cropEndDip; // last crop end-point (DIP), set on every crop move AND mouse-up
    private System.Windows.Shapes.Rectangle? _cropRect;

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
    private bool _closed; // set on close so async continuations can bail before touching the window

    // settings/ocr are optional so the editor can be opened standalone; onSave is last to allow a
    // trailing-lambda call site (HistoryWindow passes it that way).
    public AnnotationEditorWindow(BitmapSource source,
        Aquashot.Settings.AppSettings? settings, Aquashot.History.IOcrService? ocr,
        Action<BitmapSource> onSave)
    {
        InitializeComponent();
        _source = source;
        _onSave = onSave;
        _settings = settings ?? new Aquashot.Settings.AppSettings();
        _ocr = ocr;
        SourceImage.Source = source; // AnnotationRenderer.Draw ignores its source arg, so show the image here

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
        _toolbar.RedactRequested += OnRedact;
        _toolbar.CropModeChanged += OnCropModeChanged;
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
        if (_cropMode) { CropDown(e); return; }

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
            case ToolKind.Highlighter:
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
        if (_cropMode) { CropMove(e); return; }
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
        if (IsFreehand(_toolbar.CurrentTool) && _penPoints != null)
        {
            var (px, py) = ToImage(_lastDip);
            _penPoints.Add((px, py));
        }
        RebuildPreview();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_cropMode) { CropUp(e); return; }
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
        else if (_toolbar.CurrentTool == ToolKind.Highlighter && _penPoints != null && _penPoints.Count > 1)
            _doc.Add(new HighlightShape(_penPoints.ToArray(), _toolbar.CurrentColor,
                _settings.HighlighterWidth, _settings.HighlighterOpacity));
        else if (preview is SpotlightShape sp)
        {
            _doc.RemoveAllOfType<SpotlightShape>();
            _doc.Add(sp);
        }
        else if (preview != null)
            _doc.Add(preview);
        _penPoints = null;
        _layer.Refresh();
    }

    private static bool IsFreehand(ToolKind t) => t is ToolKind.Pen or ToolKind.Highlighter;

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

        if (_penPoints != null)
        {
            if (_toolbar.CurrentTool == ToolKind.Highlighter)
                _layer.Preview = new HighlightShape(_penPoints.ToArray(), color,
                    _settings.HighlighterWidth, _settings.HighlighterOpacity);
            else if (_toolbar.CurrentTool == ToolKind.Pen)
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
            ToolKind.Spotlight => new SpotlightShape(Math.Min(sx, cx), Math.Min(sy, cy),
                Math.Abs(cx - sx), Math.Abs(cy - sy), _settings.SpotlightDimColor),
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
        if (_cropMode)
        {
            if (e.Key == Key.Enter) { ApplyCrop(); return; }
            if (e.Key == Key.Escape) { CancelCrop(); return; }
        }
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

    // ---- Auto-redact ----

    // OCR the source image and blur/pixelate every (matching) detected text line. No-op without OCR.
    private async void OnRedact()
    {
        if (_ocr == null) return;
        string? temp = null;
        try
        {
            temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "aqua-ocr-" + Guid.NewGuid().ToString("N") + ".png");
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(_source));
            using (var fs = System.IO.File.Create(temp)) enc.Save(fs);

            var lines = await _ocr.RecognizeLinesAsync(temp);
            if (_closed) return; // the editor was closed while OCR was in flight
            // Boxes are in the saved-image's pixel space == the editor's image space (offset 0,0).
            var chosen = Aquashot.Redaction.AutoRedactor.SelectLines(lines, _settings.RedactPatterns);
            var shapes = Aquashot.Redaction.AutoRedactor.BuildShapes(chosen, 0, 0,
                _settings.RedactStyle, _settings.RedactBlurRadius, _settings.RedactPixelateBlock);
            if (shapes.Count > 0) { _doc.AddRange(shapes); _layer.Refresh(); }
        }
        catch { /* best-effort */ }
        finally { if (temp != null) try { System.IO.File.Delete(temp); } catch { } }
    }

    // ---- Post-capture crop ----

    private void OnCropModeChanged(bool on)
    {
        _cropMode = on;
        ClearSelection();
        ClearCropRect();
        _cropStart = null;
        _cropEndDip = null;
        Cursor = on ? Cursors.Cross : (_toolbar.CurrentTool == ToolKind.Select ? Cursors.Arrow : Cursors.Cross);
    }

    private void CropDown(MouseButtonEventArgs e)
    {
        var p = e.GetPosition(Overlay);
        _cropStart = ToImage(p);
        _cropEndDip = p; // seed the end-point so a click-then-Enter (no move) has a real position
        _dragging = true;
        Overlay.CaptureMouse();
        EnsureCropRect();
    }

    private void CropMove(MouseEventArgs e)
    {
        if (!_dragging || _cropStart is not (double sx, double sy)) return;
        _cropEndDip = e.GetPosition(Overlay);
        var (cx, cy) = ToImage(_cropEndDip.Value);
        // Position the crop rect overlay in DIP (over the image), derived from image-px corners.
        double x0 = Math.Min(sx, cx), y0 = Math.Min(sy, cy);
        double w = Math.Abs(cx - sx), h = Math.Abs(cy - sy);
        Canvas.SetLeft(_cropRect!, _offX + x0 * _fitScale);
        Canvas.SetTop(_cropRect!, _offY + y0 * _fitScale);
        _cropRect!.Width = w * _fitScale;
        _cropRect.Height = h * _fitScale;
    }

    private void CropUp(MouseButtonEventArgs e)
    {
        // Record the actual release position before clearing the drag, so ApplyCrop (via Enter or
        // the toolbar button) uses where the mouse was lifted, not the last tracked move event.
        _cropEndDip = e.GetPosition(Overlay);
        _dragging = false;
        Overlay.ReleaseMouseCapture();
        // Leave the rect on screen; Enter or a click on the crop button applies it. (Tiny drags do nothing.)
    }

    // Apply the pending crop: crop the source, shift all shapes by -origin, relayout, untick the tool.
    private void ApplyCrop()
    {
        if (_cropStart is not (double sx, double sy) || _cropRect == null || _cropEndDip is not Point endDip) return;
        var (cx, cy) = ToImage(endDip);
        double x0 = Math.Min(sx, cx), y0 = Math.Min(sy, cy);
        double w = Math.Abs(cx - sx), h = Math.Abs(cy - sy);
        if (w < 2 || h < 2) { CancelCrop(); return; }

        var crop = new PixelRect(x0, y0, w, h);
        // Clamp ONCE and use the same integer pixel origin for both the bitmap crop and the shape
        // translation, so a fractional drag origin can't leave shapes offset from the cropped content.
        var clamped = CropController.Clamp(_source.PixelWidth, _source.PixelHeight, crop);
        var clampedRect = new PixelRect(clamped.X, clamped.Y, clamped.Width, clamped.Height);
        var cropped = CropController.Apply(_source, clampedRect);

        var moved = CropController.TranslateShapes(_doc.Shapes, -clamped.X, -clamped.Y);
        _doc.ReplaceAll(moved);

        _source = cropped;
        SourceImage.Source = cropped;
        _layer.Source = cropped;
        _layer.Width = cropped.PixelWidth;
        _layer.Height = cropped.PixelHeight;

        ClearCropRect();
        _cropStart = null;
        _cropEndDip = null;
        _cropMode = false;
        _toolbar.ResetCropToggle();
        Layout();
    }

    private void CancelCrop()
    {
        ClearCropRect();
        _cropStart = null;
        _cropEndDip = null;
        _cropMode = false;
        _toolbar.ResetCropToggle();
    }

    private void EnsureCropRect()
    {
        if (_cropRect != null) { ClearCropRect(); }
        _cropRect = new System.Windows.Shapes.Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0x4D, 0xA3, 0xFF)),
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(0x33, 0x4D, 0xA3, 0xFF)),
            IsHitTestVisible = false
        };
        Overlay.Children.Add(_cropRect);
    }

    private void ClearCropRect()
    {
        if (_cropRect != null) { Overlay.Children.Remove(_cropRect); _cropRect = null; }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true; // let any in-flight OCR continuation bail instead of touching a closed window
        base.OnClosed(e);
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
