using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aquashot.Annotation;
using Aquashot.Capture;
using Aquashot.Editor;
using Aquashot.Output;
using Aquashot.Pin;
using Aquashot.Recording;
using Aquashot.Selection;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Rectangle = System.Windows.Shapes.Rectangle;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;
using Size = System.Windows.Size;

namespace Aquashot.Overlay;

public partial class OverlayWindow : Window
{
    public enum OverlayMode { Region, Window }
    private enum Phase { Selecting, Annotating }

    private readonly CapturedFrame _frame;
    private readonly double _sc;
    private Phase _phase = Phase.Selecting;
    private Point _start;
    private bool _dragging;
    private bool _closed;
    private Point _lastAnnotateDip;

    private readonly WindowDetector _detector = new();
    private PixelRect? _hoverWindow;

    private PixelRect _selVirtual;
    private AnnotationDocument? _doc;
    private AnnotationLayer? _layer;
    private InlineToolbar? _toolbar;
    private List<(double X, double Y)>? _penPoints;

    private int _selectedIndex = -1;
    private bool _movingSelected;
    private (double X, double Y) _moveLastCrop;

    private readonly List<Rectangle> _handles = new();
    private Rectangle? _selBorder;
    private Rectangle? _moveBand;
    private bool _resizing;
    private int _activeHandle = -1;
    private const double MinSel = 10;
    private const double MoveBandWidth = 14;

    private bool _movingRegion;
    private (double x, double y) _moveStartVirtual;
    private PixelRect _moveOrigSel;

    public OverlayMode Mode { get; set; } = OverlayMode.Region;

    // Set by OverlayController before the window is shown; drives in-toolbar recording.
    public RecordingController? Recorder { get; set; }
    private bool _recording;
    private readonly System.Windows.Threading.DispatcherTimer _recTimer =
        new() { Interval = TimeSpan.FromSeconds(1) };

    public event Action<OverlayWindow>? RegionCommitted;
    public event Action<CapturedFrame, PixelRect, AnnotationDocument>? Confirmed;
    public event Action? PinRequested;
    public event Action? Cancelled;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr dst, IntPtr a, IntPtr b, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    private const int RGN_DIFF = 4;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    public OverlayWindow(CapturedFrame frame)
    {
        InitializeComponent();
        _frame = frame;
        _sc = frame.Monitor.DpiScale;
        FrozenImage.Source = frame.Bitmap;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var b = _frame.Monitor.Bounds;
        SetWindowPos(hwnd, HWND_TOPMOST, (int)b.X, (int)b.Y, (int)b.Width, (int)b.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
        Activate();
        Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        base.OnClosed(e);
    }

    private (double vx, double vy) DipToVirtual(Point p) =>
        (p.X * _sc + _frame.Monitor.Bounds.X, p.Y * _sc + _frame.Monitor.Bounds.Y);

    private PixelRect ToVirtualRect(Point startDip, Point endDip)
    {
        var local = SelectionEngine.Normalize(startDip.X * _sc, startDip.Y * _sc, endDip.X * _sc, endDip.Y * _sc);
        return new PixelRect(local.X + _frame.Monitor.Bounds.X, local.Y + _frame.Monitor.Bounds.Y,
            local.Width, local.Height);
    }

    private (double cx, double cy) ToCrop(Point dip) =>
        (dip.X * _sc - (_selVirtual.X - _frame.Monitor.Bounds.X),
         dip.Y * _sc - (_selVirtual.Y - _frame.Monitor.Bounds.Y));

    private double VxToDip(double vx) => (vx - _frame.Monitor.Bounds.X) / _sc;
    private double VyToDip(double vy) => (vy - _frame.Monitor.Bounds.Y) / _sc;

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_phase == Phase.Annotating) { AnnotateDown(e); return; }

        if (Mode == OverlayMode.Window)
        {
            if (_hoverWindow is PixelRect wr) BeginAnnotate(wr);
            return;
        }
        _start = e.GetPosition(Overlay);
        _dragging = true;
        SelRect.Visibility = Visibility.Visible;
        Overlay.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_movingRegion) { MoveRegion(e); return; }
        if (_resizing) { ResizeMove(e); return; }
        if (_phase == Phase.Annotating) { AnnotateMove(e); return; }

        if (Mode == OverlayMode.Window && !_dragging)
        {
            var (vx, vy) = DipToVirtual(e.GetPosition(Overlay));
            var win = _detector.WindowAt(vx, vy);
            if (win is PixelRect wr)
            {
                _hoverWindow = wr;
                Canvas.SetLeft(WinRect, VxToDip(wr.X));
                Canvas.SetTop(WinRect, VyToDip(wr.Y));
                WinRect.Width = wr.Width / _sc;
                WinRect.Height = wr.Height / _sc;
                WinRect.Visibility = Visibility.Visible;
                ShowDimAt(VxToDip(wr.X), VyToDip(wr.Y), wr);
            }
            else { _hoverWindow = null; WinRect.Visibility = Visibility.Collapsed; DimLabel.Visibility = Visibility.Collapsed; }
            return;
        }
        if (!_dragging) return;
        var p = e.GetPosition(Overlay);
        double x = Math.Min(_start.X, p.X), y = Math.Min(_start.Y, p.Y);
        Canvas.SetLeft(SelRect, x);
        Canvas.SetTop(SelRect, y);
        SelRect.Width = Math.Abs(p.X - _start.X);
        SelRect.Height = Math.Abs(p.Y - _start.Y);
        ShowDimAt(x, y, ToVirtualRect(_start, p));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_movingRegion) { _movingRegion = false; Overlay.ReleaseMouseCapture(); return; }
        if (_resizing) { _resizing = false; _activeHandle = -1; Overlay.ReleaseMouseCapture(); return; }
        if (_phase == Phase.Annotating) { AnnotateUp(e); return; }
        if (!_dragging) return;
        _dragging = false;
        Overlay.ReleaseMouseCapture();
        var rect = ToVirtualRect(_start, e.GetPosition(Overlay));
        if (rect.Width < 2 || rect.Height < 2) { RaiseCancelled(); return; }
        BeginAnnotate(rect);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_recording) { OnPrimary(); return; } // Esc stops & finalizes a recording
            RaiseCancelled();
            return;
        }
        if (_phase == Phase.Annotating)
        {
            if (e.Key == Key.Enter) OnPrimary();
            else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0) OnPrimary();
            else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { _doc!.Undo(); ClearSelection(); _layer!.Refresh(); }
            else if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { _doc!.Redo(); ClearSelection(); _layer!.Refresh(); }
            else if ((e.Key == Key.Delete || e.Key == Key.Back) && _selectedIndex >= 0)
            {
                _doc!.RemoveAt(_selectedIndex);
                ClearSelection();
                _layer!.Refresh();
            }
        }
    }

    private void ShowDimAt(double dipX, double dipY, PixelRect vrect)
    {
        DimText.Text = $"{(int)Math.Round(vrect.Width)} × {(int)Math.Round(vrect.Height)}";
        DimLabel.Visibility = Visibility.Visible;
        DimLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double ly = dipY - DimLabel.DesiredSize.Height - 4;
        if (ly < 2) ly = dipY + 4;
        Canvas.SetLeft(DimLabel, Math.Max(2, dipX));
        Canvas.SetTop(DimLabel, ly);
    }

    private void BeginAnnotate(PixelRect virtualRect)
    {
        virtualRect = ClampToMonitor(virtualRect);
        _selVirtual = virtualRect;
        _phase = Phase.Annotating;
        SelRect.Visibility = Visibility.Collapsed;
        WinRect.Visibility = Visibility.Collapsed;
        if (!_closed) RegionCommitted?.Invoke(this);

        _doc = new AnnotationDocument();
        _layer = new AnnotationLayer { Doc = _doc, IsHitTestVisible = false };
        Overlay.Children.Add(_layer);

        var accent = new SolidColorBrush(Color.FromRgb(0x4D, 0xA3, 0xFF));
        accent.Freeze();

        _selBorder = new Rectangle
        {
            Stroke = accent,
            StrokeThickness = 1.5,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            IsHitTestVisible = false
        };
        Overlay.Children.Add(_selBorder);

        // Transparent thick band over the dashed edge: grab it to drag the whole region.
        _moveBand = new Rectangle
        {
            Stroke = Brushes.Transparent,
            StrokeThickness = MoveBandWidth,
            Fill = null,
            Cursor = Cursors.SizeAll
        };
        _moveBand.MouseLeftButtonDown += (s, e) =>
        {
            _movingRegion = true;
            _moveStartVirtual = DipToVirtual(e.GetPosition(Overlay));
            _moveOrigSel = _selVirtual;
            Overlay.CaptureMouse();
            e.Handled = true;
        };
        Overlay.Children.Add(_moveBand);

        for (int i = 0; i < 8; i++)
        {
            int idx = i;
            var (hw, hh) = HandleDim(idx);
            var h = new Rectangle
            {
                Width = hw,
                Height = hh,
                RadiusX = 2,
                RadiusY = 2,
                Fill = accent,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                Cursor = HandleCursor(idx)
            };
            h.MouseLeftButtonDown += (s, e) => { _resizing = true; _activeHandle = idx; Overlay.CaptureMouse(); e.Handled = true; };
            _handles.Add(h);
            Overlay.Children.Add(h);
        }

        _toolbar = new InlineToolbar();
        _toolbar.UndoRequested += () => { _doc.Undo(); ClearSelection(); _layer.Refresh(); };
        _toolbar.RedoRequested += () => { _doc.Redo(); ClearSelection(); _layer.Refresh(); };
        _toolbar.PrimaryClicked += OnPrimary;
        _toolbar.CancelRequested += RaiseCancelled;
        _toolbar.ToolChanged += OnToolChanged;
        _toolbar.OutputModeChanged += OnModeSelected;
        _toolbar.PinRequested += OnPin;
        Overlay.Children.Add(_toolbar);

        ApplySelection(_selVirtual, translateShapes: false, oldSel: _selVirtual);
    }

    private void ApplySelection(PixelRect newSel, bool translateShapes, PixelRect oldSel)
    {
        if (translateShapes && _doc != null)
        {
            double ddx = newSel.X - oldSel.X;
            double ddy = newSel.Y - oldSel.Y;
            if (ddx != 0 || ddy != 0) _doc.TranslateAll(-ddx, -ddy);
        }
        _selVirtual = newSel;

        double selLeftDip = VxToDip(newSel.X);
        double selTopDip = VyToDip(newSel.Y);
        double selWDip = newSel.Width / _sc;
        double selHDip = newSel.Height / _sc;

        double winWDip = _frame.Monitor.Bounds.Width / _sc;
        double winHDip = _frame.Monitor.Bounds.Height / _sc;
        var full = new RectangleGeometry(new Rect(0, 0, winWDip, winHDip));
        var hole = new RectangleGeometry(new Rect(selLeftDip, selTopDip, selWDip, selHDip));
        var grp = new GeometryGroup { FillRule = FillRule.EvenOdd };
        grp.Children.Add(full);
        grp.Children.Add(hole);
        Dim.Clip = grp;

        int lx = Math.Max(0, (int)(newSel.X - _frame.Monitor.Bounds.X));
        int ly = Math.Max(0, (int)(newSel.Y - _frame.Monitor.Bounds.Y));
        int lw = Math.Max(1, (int)newSel.Width), lh = Math.Max(1, (int)newSel.Height);
        if (lx + lw > _frame.Bitmap.PixelWidth) lw = _frame.Bitmap.PixelWidth - lx;
        if (ly + lh > _frame.Bitmap.PixelHeight) lh = _frame.Bitmap.PixelHeight - ly;
        lw = Math.Max(1, lw); lh = Math.Max(1, lh);
        var cropped = new CroppedBitmap(_frame.Bitmap, new Int32Rect(lx, ly, lw, lh));

        _layer!.Source = cropped;
        _layer.Width = lw;
        _layer.Height = lh;
        _layer.RenderTransform = new ScaleTransform(1.0 / _sc, 1.0 / _sc);
        Canvas.SetLeft(_layer, selLeftDip);
        Canvas.SetTop(_layer, selTopDip);
        if (_selectedIndex >= 0) _layer.SelectionBox = _doc?.BoundsAt(_selectedIndex);
        _layer.Refresh();

        Canvas.SetLeft(_selBorder!, selLeftDip);
        Canvas.SetTop(_selBorder!, selTopDip);
        _selBorder!.Width = selWDip;
        _selBorder!.Height = selHDip;

        Canvas.SetLeft(_moveBand!, selLeftDip);
        Canvas.SetTop(_moveBand!, selTopDip);
        _moveBand!.Width = selWDip;
        _moveBand!.Height = selHDip;

        PositionHandles(selLeftDip, selTopDip, selWDip, selHDip);
        ShowDimAt(selLeftDip, selTopDip, newSel);

        _toolbar!.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tbW = _toolbar.DesiredSize.Width;
        double tbLeft = Math.Max(4, Math.Min(selLeftDip, winWDip - tbW - 4));
        double tbTop = selTopDip + selHDip + 12;
        if (tbTop + 44 > winHDip) tbTop = Math.Max(4, selTopDip - 52);
        Canvas.SetLeft(_toolbar, tbLeft);
        Canvas.SetTop(_toolbar, tbTop);
    }

    private void PositionHandles(double x, double y, double w, double h)
    {
        var pts = new (double cx, double cy)[]
        {
            (x, y), (x + w / 2, y), (x + w, y),
            (x + w, y + h / 2), (x + w, y + h),
            (x + w / 2, y + h), (x, y + h), (x, y + h / 2)
        };
        for (int i = 0; i < 8; i++)
        {
            Canvas.SetLeft(_handles[i], pts[i].cx - _handles[i].Width / 2);
            Canvas.SetTop(_handles[i], pts[i].cy - _handles[i].Height / 2);
        }
    }

    // Corners are square nubs; side handles are thick bars along their edge.
    private static (double w, double h) HandleDim(int i) => i switch
    {
        0 or 2 or 4 or 6 => (13, 13),  // corners
        1 or 5 => (30, 7),             // top / bottom
        _ => (7, 30)                   // left / right
    };

    private void ResizeMove(MouseEventArgs e)
    {
        var (vx, vy) = DipToVirtual(e.GetPosition(Overlay));
        double left = _selVirtual.X, top = _selVirtual.Y, right = _selVirtual.Right, bottom = _selVirtual.Bottom;
        switch (_activeHandle)
        {
            case 0: left = vx; top = vy; break;
            case 1: top = vy; break;
            case 2: right = vx; top = vy; break;
            case 3: right = vx; break;
            case 4: right = vx; bottom = vy; break;
            case 5: bottom = vy; break;
            case 6: left = vx; bottom = vy; break;
            case 7: left = vx; break;
        }
        if (right - left < MinSel) { if (_activeHandle is 0 or 6 or 7) left = right - MinSel; else right = left + MinSel; }
        if (bottom - top < MinSel) { if (_activeHandle is 0 or 1 or 2) top = bottom - MinSel; else bottom = top + MinSel; }
        var newSel = ClampToMonitor(new PixelRect(left, top, right - left, bottom - top));
        ApplySelection(newSel, translateShapes: true, oldSel: _selVirtual);
    }

    // Drag the whole region (grabbed on the dashed border). Keeps size; annotations ride
    // along with the region (crop-local coords unchanged, so no shape translation).
    private void MoveRegion(MouseEventArgs e)
    {
        var (vx, vy) = DipToVirtual(e.GetPosition(Overlay));
        double dx = vx - _moveStartVirtual.x;
        double dy = vy - _moveStartVirtual.y;
        var moved = ClampMove(new PixelRect(_moveOrigSel.X + dx, _moveOrigSel.Y + dy,
            _moveOrigSel.Width, _moveOrigSel.Height));
        ApplySelection(moved, translateShapes: false, oldSel: _selVirtual);
    }

    // Clamp position to keep the region fully on its monitor without changing its size.
    private PixelRect ClampMove(PixelRect r)
    {
        var b = _frame.Monitor.Bounds;
        double x = Math.Clamp(r.X, b.X, Math.Max(b.X, b.Right - r.Width));
        double y = Math.Clamp(r.Y, b.Y, Math.Max(b.Y, b.Bottom - r.Height));
        return new PixelRect(x, y, r.Width, r.Height);
    }

    private PixelRect ClampToMonitor(PixelRect r)
    {
        var b = _frame.Monitor.Bounds;
        double x = Math.Max(b.X, r.X), y = Math.Max(b.Y, r.Y);
        double right = Math.Min(b.Right, r.Right), bottom = Math.Min(b.Bottom, r.Bottom);
        return new PixelRect(x, y, Math.Max(MinSel, right - x), Math.Max(MinSel, bottom - y));
    }

    private static Cursor HandleCursor(int i) => i switch
    {
        0 or 4 => Cursors.SizeNWSE,
        2 or 6 => Cursors.SizeNESW,
        1 or 5 => Cursors.SizeNS,
        _ => Cursors.SizeWE
    };

    private void AnnotateDown(MouseButtonEventArgs e)
    {
        var (cx, cy) = ToCrop(e.GetPosition(Overlay));
        var tool = _toolbar!.CurrentTool;
        string color = _toolbar.CurrentColor;
        double w = _toolbar.CurrentWidth;

        switch (tool)
        {
            case ToolKind.Select:
                int hit = _doc!.HitTest(cx, cy);
                _selectedIndex = hit;
                _movingSelected = hit >= 0;
                if (hit >= 0)
                {
                    _moveLastCrop = (cx, cy);
                    _dragging = true;
                    Overlay.CaptureMouse();
                }
                UpdateSelectionBox();
                return;
            case ToolKind.Counter:
                _doc!.Add(new CounterShape(cx, cy, _doc.NextCounter(), color, w));
                _layer!.Refresh();
                return;
            case ToolKind.Text:
                var text = TextPrompt.Ask();
                if (!string.IsNullOrEmpty(text)) { _doc!.Add(new TextShape(cx, cy, text!, color, w)); _layer!.Refresh(); }
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

    private void AnnotateMove(MouseEventArgs e)
    {
        if (_toolbar!.CurrentTool == ToolKind.Select)
        {
            if (!_dragging || !_movingSelected || _selectedIndex < 0) return;
            var (mx, my) = ToCrop(e.GetPosition(Overlay));
            _doc!.MoveAt(_selectedIndex, mx - _moveLastCrop.X, my - _moveLastCrop.Y);
            _moveLastCrop = (mx, my);
            UpdateSelectionBox();
            _layer!.Refresh();
            return;
        }
        if (!_dragging) return;
        _lastAnnotateDip = e.GetPosition(Overlay);
        if (_toolbar!.CurrentTool == ToolKind.Pen && _penPoints != null)
        {
            var (px, py) = ToCrop(_lastAnnotateDip);
            _penPoints.Add((px, py));
        }
        RebuildPreview();
    }

    // Rebuild the live preview from the last known pointer position + current tool
    // settings. Called on mouse move and on mouse-wheel size changes so resizing the
    // annotation mid-draw updates immediately.
    private void RebuildPreview()
    {
        if (!_dragging) return;
        var (cx, cy) = ToCrop(_lastAnnotateDip);
        var (sx, sy) = ToCrop(_start);
        string color = _toolbar!.CurrentColor;
        double w = _toolbar.CurrentWidth;
        bool fill = _toolbar.CurrentFill;

        if (_toolbar.CurrentTool == ToolKind.Pen && _penPoints != null)
        {
            _layer!.Preview = new PenShape(_penPoints.ToArray(), color, w);
            _layer.Refresh();
            return;
        }

        _layer!.Preview = _toolbar.CurrentTool switch
        {
            ToolKind.Rect => MakeRect(sx, sy, cx, cy, color, w, fill),
            ToolKind.Ellipse => MakeEllipse(sx, sy, cx, cy, color, w, fill),
            ToolKind.Line => new LineShape(sx, sy, cx, cy, color, w),
            ToolKind.Arrow => new ArrowShape(sx, sy, cx, cy, color, w),
            _ => null
        };
        _layer.Refresh();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_phase != Phase.Annotating || _toolbar == null) return;
        _toolbar.AdjustWidth(e.Delta > 0 ? 1 : -1);
        RebuildPreview();
        e.Handled = true;
    }

    private void AnnotateUp(MouseButtonEventArgs e)
    {
        if (_toolbar!.CurrentTool == ToolKind.Select)
        {
            _movingSelected = false;
            _dragging = false;
            Overlay.ReleaseMouseCapture();
            return;
        }
        if (!_dragging) return;
        _dragging = false;
        Overlay.ReleaseMouseCapture();
        var preview = _layer!.Preview;
        _layer.Preview = null;
        if (_toolbar!.CurrentTool == ToolKind.Pen && _penPoints != null && _penPoints.Count > 1)
            _doc!.Add(new PenShape(_penPoints.ToArray(), _toolbar.CurrentColor, _toolbar.CurrentWidth));
        else if (preview != null)
            _doc!.Add(preview);
        _penPoints = null;
        _layer.Refresh();
    }

    private static RectShape MakeRect(double sx, double sy, double cx, double cy, string color, double w, bool fill) =>
        new(Math.Min(sx, cx), Math.Min(sy, cy), Math.Abs(cx - sx), Math.Abs(cy - sy), color, w, fill);
    private static EllipseShape MakeEllipse(double sx, double sy, double cx, double cy, string color, double w, bool fill) =>
        new(Math.Min(sx, cx), Math.Min(sy, cy), Math.Abs(cx - sx), Math.Abs(cy - sy), color, w, fill);

    private void OnToolChanged(ToolKind t)
    {
        ClearSelection();
        if (_layer != null) _layer.Refresh();
        Cursor = t == ToolKind.Select ? Cursors.Arrow : Cursors.Cross;
    }

    private void ClearSelection()
    {
        _selectedIndex = -1;
        _movingSelected = false;
        if (_layer != null) _layer.SelectionBox = null;
    }

    private void UpdateSelectionBox()
    {
        if (_layer == null) return;
        _layer.SelectionBox = _selectedIndex >= 0 ? _doc?.BoundsAt(_selectedIndex) : null;
        _layer.Refresh();
    }

    // Selecting GIF/MP4 just warms the encoder so the eventual Record click is instant.
    private void OnModeSelected(CaptureOutput o)
    {
        if (o != CaptureOutput.Image) Recorder?.Prewarm();
    }

    // The single right-hand button: Capture (save screenshot), Record (start), or Stop.
    private void OnPrimary()
    {
        if (_closed || _doc == null) return;
        if (_recording) { _ = StopRecordingAsync(); return; }
        if (_toolbar!.CurrentOutput == CaptureOutput.Image)
        {
            Confirmed?.Invoke(_frame, _selVirtual, _doc);
            return;
        }
        _ = StartRecordingAsync();
    }

    // Start recording inline: carve a hole in this (opaque, hardware-accelerated) window
    // over the region so the live screen shows through and stays interactive, while the
    // frozen+dimmed surroundings and the toolbar (below the region) remain. Annotations
    // are burned into the output by ffmpeg from a rendered PNG. No popout, fast selection.
    private async Task StartRecordingAsync()
    {
        if (Recorder == null) return;
        var fmt = _toolbar!.CurrentOutput == CaptureOutput.Gif ? RecordFormats.Gif : RecordFormats.Mp4;

        var annPng = TryRenderAnnotationPng();
        CarveRegionHole();
        DimLabel.Visibility = Visibility.Collapsed;
        foreach (var h in _handles) h.Visibility = Visibility.Collapsed;
        if (_moveBand != null) _moveBand.Visibility = Visibility.Collapsed;
        ClearSelection();
        _layer!.Refresh();

        var err = await Recorder.StartAsync(_selVirtual, fmt, annPng);
        if (err != null) { if (!_closed) Close(); return; } // recorder surfaces the error via Finished

        _recording = true;
        _toolbar.SetPrimary("Stop", "#E03B3B");
        _toolbar.ShowTimer(true);
        _toolbar.SetTimer("00:00");
        _recTimer.Tick += OnRecTick;
        _recTimer.Start();
    }

    // Render the current annotations onto a transparent PNG (crop-sized) for ffmpeg to
    // overlay onto the recording. Returns null if there's nothing to draw.
    private string? TryRenderAnnotationPng()
    {
        if (_doc == null || _doc.Shapes.Count == 0) return null;
        int w = Math.Max(1, (int)_selVirtual.Width), h = Math.Max(1, (int)_selVirtual.Height);
        var bmp = new AnnotationRenderer().RenderTransparent(w, h, _doc.Shapes);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "aqua-ann-" + Guid.NewGuid().ToString("N") + ".png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = System.IO.File.Create(path);
        enc.Save(fs);
        return path;
    }

    // Make the region a true window hole: live desktop shows through and receives clicks.
    private void CarveRegionHole()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var b = _frame.Monitor.Bounds;
        int sx = (int)(_selVirtual.X - b.X), sy = (int)(_selVirtual.Y - b.Y);
        var full = CreateRectRgn(0, 0, (int)b.Width, (int)b.Height);
        var hole = CreateRectRgn(sx, sy, sx + (int)_selVirtual.Width, sy + (int)_selVirtual.Height);
        CombineRgn(full, full, hole, RGN_DIFF);
        DeleteObject(hole);
        SetWindowRgn(hwnd, full, true); // window takes ownership of 'full'
    }

    private void OnRecTick(object? sender, EventArgs e)
    {
        var t = Recorder!.Elapsed;
        _toolbar!.SetTimer($"{(int)t.TotalMinutes:00}:{t.Seconds:00}");
    }

    private async Task StopRecordingAsync()
    {
        _recTimer.Stop();
        _recTimer.Tick -= OnRecTick;
        _recording = false;
        if (Recorder != null) await Recorder.StopAsync();
        if (!_closed) Close();
    }

    // Compose the annotated region into a bitmap and float it on screen as a pin,
    // positioned over the original selection, then dismiss the capture overlay.
    private void OnPin()
    {
        if (_closed || _doc == null) return;
        var img = new OutputService().Compose(_frame, _selVirtual, _doc);
        if (img.CanFreeze && !img.IsFrozen) img.Freeze();
        var pin = new PinWindow(img, _sc)
        {
            Left = VxToDip(_selVirtual.X),
            Top = VyToDip(_selVirtual.Y)
        };
        pin.Show();
        PinRequested?.Invoke();
    }

    private void RaiseCancelled()
    {
        if (!_closed) Cancelled?.Invoke();
    }
}
