using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnipTool.Annotation;
using SnipTool.Capture;
using SnipTool.Editor;
using SnipTool.Selection;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Size = System.Windows.Size;

namespace SnipTool.Overlay;

public partial class OverlayWindow : Window
{
    public enum OverlayMode { Region, Window }
    private enum Phase { Selecting, Annotating }

    private readonly CapturedFrame _frame;
    private readonly double _sc;
    private Phase _phase = Phase.Selecting;
    private Point _start;
    private bool _dragging;

    private readonly WindowDetector _detector = new();
    private PixelRect? _hoverWindow;

    private PixelRect _selVirtual;
    private AnnotationDocument? _doc;
    private AnnotationLayer? _layer;
    private InlineToolbar? _toolbar;
    private List<(double X, double Y)>? _penPoints;

    public OverlayMode Mode { get; set; } = OverlayMode.Region;

    public event Action<OverlayWindow>? RegionCommitted;
    public event Action<CapturedFrame, PixelRect, AnnotationDocument>? Confirmed;
    public event Action? Cancelled;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
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

    // ---- coordinate helpers ----
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

    // ---- mouse ----
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
        if (_phase == Phase.Annotating) { AnnotateMove(e); return; }

        if (Mode == OverlayMode.Window && !_dragging)
        {
            var (vx, vy) = DipToVirtual(e.GetPosition(Overlay));
            var win = _detector.WindowAt(vx, vy);
            if (win is PixelRect wr)
            {
                _hoverWindow = wr;
                Canvas.SetLeft(WinRect, (wr.X - _frame.Monitor.Bounds.X) / _sc);
                Canvas.SetTop(WinRect, (wr.Y - _frame.Monitor.Bounds.Y) / _sc);
                WinRect.Width = wr.Width / _sc;
                WinRect.Height = wr.Height / _sc;
                WinRect.Visibility = Visibility.Visible;
            }
            else { _hoverWindow = null; WinRect.Visibility = Visibility.Collapsed; }
            return;
        }
        if (!_dragging) return;
        var p = e.GetPosition(Overlay);
        double x = Math.Min(_start.X, p.X), y = Math.Min(_start.Y, p.Y);
        Canvas.SetLeft(SelRect, x);
        Canvas.SetTop(SelRect, y);
        SelRect.Width = Math.Abs(p.X - _start.X);
        SelRect.Height = Math.Abs(p.Y - _start.Y);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_phase == Phase.Annotating) { AnnotateUp(e); return; }
        if (!_dragging) return;
        _dragging = false;
        Overlay.ReleaseMouseCapture();
        var rect = ToVirtualRect(_start, e.GetPosition(Overlay));
        if (rect.Width < 2 || rect.Height < 2) { Cancelled?.Invoke(); return; }
        BeginAnnotate(rect);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Cancelled?.Invoke(); return; }
        if (_phase == Phase.Annotating)
        {
            if (e.Key == Key.Enter) Confirm();
            else if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { _doc!.Undo(); _layer!.Refresh(); }
            else if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { _doc!.Redo(); _layer!.Refresh(); }
        }
    }

    // ---- annotate phase ----
    private void BeginAnnotate(PixelRect virtualRect)
    {
        _selVirtual = virtualRect;
        _phase = Phase.Annotating;
        SelRect.Visibility = Visibility.Collapsed;
        WinRect.Visibility = Visibility.Collapsed;
        RegionCommitted?.Invoke(this);

        double selLeftDip = (virtualRect.X - _frame.Monitor.Bounds.X) / _sc;
        double selTopDip = (virtualRect.Y - _frame.Monitor.Bounds.Y) / _sc;
        double selWDip = virtualRect.Width / _sc;
        double selHDip = virtualRect.Height / _sc;

        // brighten selection: clip the dim layer to everything EXCEPT the selection
        Dim.UpdateLayout();
        var full = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        var hole = new RectangleGeometry(new Rect(selLeftDip, selTopDip, selWDip, selHDip));
        var grp = new GeometryGroup { FillRule = FillRule.EvenOdd };
        grp.Children.Add(full);
        grp.Children.Add(hole);
        Dim.Clip = grp;

        // cropped source (monitor-local physical px) for blur sampling
        int lx = (int)(virtualRect.X - _frame.Monitor.Bounds.X);
        int ly = (int)(virtualRect.Y - _frame.Monitor.Bounds.Y);
        int lw = (int)virtualRect.Width, lh = (int)virtualRect.Height;
        var cropped = new CroppedBitmap(_frame.Bitmap, new Int32Rect(lx, ly, lw, lh));

        _doc = new AnnotationDocument();
        _layer = new AnnotationLayer
        {
            Doc = _doc,
            Source = cropped,
            Width = lw,
            Height = lh,
            RenderTransform = new ScaleTransform(1.0 / _sc, 1.0 / _sc),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(_layer, selLeftDip);
        Canvas.SetTop(_layer, selTopDip);
        Overlay.Children.Add(_layer);

        _toolbar = new InlineToolbar();
        _toolbar.UndoRequested += () => { _doc.Undo(); _layer.Refresh(); };
        _toolbar.RedoRequested += () => { _doc.Redo(); _layer.Refresh(); };
        _toolbar.ConfirmRequested += Confirm;
        _toolbar.CancelRequested += () => Cancelled?.Invoke();
        _toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double tbW = _toolbar.DesiredSize.Width;
        double tbLeft = Math.Max(4, Math.Min(selLeftDip, ActualWidth - tbW - 4));
        double tbTop = selTopDip + selHDip + 8;
        if (tbTop + 40 > ActualHeight) tbTop = Math.Max(4, selTopDip - 48);
        Canvas.SetLeft(_toolbar, tbLeft);
        Canvas.SetTop(_toolbar, tbTop);
        Overlay.Children.Add(_toolbar);
    }

    private void AnnotateDown(MouseButtonEventArgs e)
    {
        var (cx, cy) = ToCrop(e.GetPosition(Overlay));
        var tool = _toolbar!.CurrentTool;
        string color = _toolbar.CurrentColor;
        double w = _toolbar.CurrentWidth;

        switch (tool)
        {
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
        if (!_dragging) return;
        var (cx, cy) = ToCrop(e.GetPosition(Overlay));
        string color = _toolbar!.CurrentColor;
        double w = _toolbar.CurrentWidth;
        var (sx, sy) = ToCrop(_start);

        if (_toolbar.CurrentTool == ToolKind.Pen && _penPoints != null)
        {
            _penPoints.Add((cx, cy));
            _layer!.Preview = new PenShape(_penPoints.ToArray(), color, w);
            _layer.Refresh();
            return;
        }

        _layer!.Preview = _toolbar.CurrentTool switch
        {
            ToolKind.Rect => MakeRect(sx, sy, cx, cy, color, w),
            ToolKind.Ellipse => MakeEllipse(sx, sy, cx, cy, color, w),
            ToolKind.Line => new LineShape(sx, sy, cx, cy, color, w),
            ToolKind.Arrow => new ArrowShape(sx, sy, cx, cy, color, w),
            ToolKind.Blur => MakeBlur(sx, sy, cx, cy, w),
            _ => null
        };
        _layer.Refresh();
    }

    private void AnnotateUp(MouseButtonEventArgs e)
    {
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

    private static RectShape MakeRect(double sx, double sy, double cx, double cy, string color, double w) =>
        new(Math.Min(sx, cx), Math.Min(sy, cy), Math.Abs(cx - sx), Math.Abs(cy - sy), color, w);
    private static EllipseShape MakeEllipse(double sx, double sy, double cx, double cy, string color, double w) =>
        new(Math.Min(sx, cx), Math.Min(sy, cy), Math.Abs(cx - sx), Math.Abs(cy - sy), color, w);
    private static BlurShape MakeBlur(double sx, double sy, double cx, double cy, double w) =>
        new(Math.Min(sx, cx), Math.Min(sy, cy), Math.Abs(cx - sx), Math.Abs(cy - sy), true, w);

    private void Confirm()
    {
        if (_doc != null) Confirmed?.Invoke(_frame, _selVirtual, _doc);
    }
}
