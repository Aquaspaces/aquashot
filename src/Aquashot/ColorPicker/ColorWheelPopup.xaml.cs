using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UserControl = System.Windows.Controls.UserControl;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;

namespace Aquashot.ColorPicker;

public partial class ColorWheelPopup : UserControl
{
    public event Action<string>? ColorChosen;
    public event Action? EyedropperRequested;

    private HsvColor _hsv = new(0, 1, 1);

    public ColorWheelPopup()
    {
        InitializeComponent();
        HueSlider.ValueChanged += (_, __) => { _hsv = _hsv with { H = HueSlider.Value }; Refresh(); };
        SvCanvas.MouseLeftButtonDown += OnSvDrag;
        SvCanvas.MouseMove += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) OnSvDrag(s, e); };
        HexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) TryHex(); };
        HexBox.LostFocus += (_, __) => TryHex();
        BtnEyedropper.Click += (_, __) => EyedropperRequested?.Invoke();
        Loaded += (_, __) => Refresh();
    }

    public void SetColor(string hex)
    {
        try { _hsv = HsvColor.FromHex(hex); HueSlider.Value = _hsv.H; Refresh(); } catch { }
    }

    private void OnSvDrag(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(SvCanvas);
        double s = Math.Clamp(p.X / Math.Max(1, SvCanvas.ActualWidth), 0, 1);
        double v = 1 - Math.Clamp(p.Y / Math.Max(1, SvCanvas.ActualHeight), 0, 1);
        _hsv = _hsv with { S = s, V = v };
        Refresh();
    }

    private void TryHex()
    {
        try { _hsv = HsvColor.FromHex(HexBox.Text); HueSlider.Value = _hsv.H; Refresh(); } catch { }
    }

    private void Refresh()
    {
        var (hr, hg, hb) = (_hsv with { S = 1, V = 1 }).ToRgb();
        SvSquare.Background = BuildSvBrush(Color.FromRgb(hr, hg, hb));
        var (r, g, b) = _hsv.ToRgb();
        var col = Color.FromRgb(r, g, b);
        Preview.Fill = new SolidColorBrush(col);
        if (!HexBox.IsFocused) HexBox.Text = _hsv.ToHex();
        Canvas.SetLeft(SvThumb, _hsv.S * SvCanvas.ActualWidth - 7);
        Canvas.SetTop(SvThumb, (1 - _hsv.V) * SvCanvas.ActualHeight - 7);
        ColorChosen?.Invoke(_hsv.ToHex());
    }

    private static Brush BuildSvBrush(Color hue)
    {
        var dg = new DrawingGroup();
        void Add(Brush b) => dg.Children.Add(new GeometryDrawing(b, null,
            new RectangleGeometry(new Rect(0, 0, 1, 1))));
        Add(new SolidColorBrush(hue));
        Add(new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0));
        Add(new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90));
        return new DrawingBrush(dg) { Stretch = Stretch.Fill };
    }
}
