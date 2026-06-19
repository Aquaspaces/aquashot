using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Aquashot.Annotation;
using UserControl = System.Windows.Controls.UserControl;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using RadioButton = System.Windows.Controls.RadioButton;

namespace Aquashot.Editor;

// What the Confirm action produces: a screenshot, or a recording in the given format.
public enum CaptureOutput { Image, Gif, Video }

public partial class InlineToolbar : UserControl
{
    public event Action<ToolKind>? ToolChanged;
    public event Action? UndoRequested;
    public event Action? RedoRequested;
    public event Action? PrimaryClicked; // the right-hand Capture / Record / Stop button
    public event Action? CancelRequested;
    public event Action? PinRequested;
    public event Action? EyedropperRequested;
    public event Action? ColorWheelRequested;
    public event Action<CaptureOutput>? OutputModeChanged;
    public event Action? ColorSampleRequested;    // sample a screen pixel and copy its hex
    public event Action<int>? DelayedCaptureRequested; // re-capture this region after N seconds
    public event Action? FreezeToggleRequested;   // show live region / re-freeze a fresh snapshot

    public CaptureOutput CurrentOutput { get; private set; } = CaptureOutput.Image;
    public ToolKind CurrentTool { get; private set; } = ToolKind.Arrow;
    public string CurrentColor { get; private set; } = "#FF3B30";
    public double CurrentWidth => WidthSlider.Value;
    public bool CurrentFill => FillToggle.IsChecked == true;

    /// <summary>Nudge the stroke/annotation size (mouse-wheel), clamped to the slider range.</summary>
    public void AdjustWidth(double delta)
    {
        double v = Math.Clamp(WidthSlider.Value + delta, WidthSlider.Minimum, WidthSlider.Maximum);
        WidthSlider.Value = v;
    }

    private static readonly string[] Palette =
        { "#FF3B30", "#FFCC00", "#34C759", "#0A84FF", "#FFFFFF", "#111111" };

    public InlineToolbar()
    {
        InitializeComponent();

        ToolSelect.Checked  += (_, __) => SetTool(ToolKind.Select);
        ToolArrow.Checked   += (_, __) => SetTool(ToolKind.Arrow);
        ToolRect.Checked    += (_, __) => SetTool(ToolKind.Rect);
        ToolEllipse.Checked += (_, __) => SetTool(ToolKind.Ellipse);
        ToolLine.Checked    += (_, __) => SetTool(ToolKind.Line);
        ToolPen.Checked     += (_, __) => SetTool(ToolKind.Pen);
        ToolText.Checked    += (_, __) => SetTool(ToolKind.Text);
        ToolCounter.Checked += (_, __) => SetTool(ToolKind.Counter);

        BtnUndo.Click    += (_, __) => UndoRequested?.Invoke();
        BtnRedo.Click    += (_, __) => RedoRequested?.Invoke();
        BtnPrimary.Click += (_, __) => PrimaryClicked?.Invoke();
        BtnCancel.Click  += (_, __) => CancelRequested?.Invoke();
        BtnPin.Click     += (_, __) => PinRequested?.Invoke();
        ToolEyedropper.Checked += (_, __) => EyedropperRequested?.Invoke();
        BtnColorWheel.Click    += (_, __) => ColorWheelRequested?.Invoke();
        ColorWheelRequested += ShowColorWheel;

        BtnColorCopy.Click += (_, __) => ColorSampleRequested?.Invoke();
        BtnFreeze.Click    += (_, __) => FreezeToggleRequested?.Invoke();
        // Delay is a quick menu (3/5/10s); open it under the button on click.
        BtnDelay.Click += (_, __) => { if (DelayMenu != null) { DelayMenu.PlacementTarget = BtnDelay; DelayMenu.IsOpen = true; } };

        ModeImage.Checked += (_, __) => SetOutput(CaptureOutput.Image);
        ModeGif.Checked   += (_, __) => SetOutput(CaptureOutput.Gif);
        ModeVideo.Checked += (_, __) => SetOutput(CaptureOutput.Video);

        BuildSwatches();
        ToolArrow.IsChecked = true;
        ModeImage.IsChecked = true;
    }

    // Annotations stay available in every mode (they get captured into the GIF/MP4 too).
    // The primary button just relabels: Capture for a screenshot, Record for GIF/MP4.
    private void SetOutput(CaptureOutput o)
    {
        CurrentOutput = o;
        SetPrimary(o == CaptureOutput.Image ? "Capture" : "Record",
                   o == CaptureOutput.Image ? "#3B82F6" : "#E03B3B");
        BtnPin.IsEnabled = o == CaptureOutput.Image; // pinning only applies to the screenshot flow
        OutputModeChanged?.Invoke(o);
    }

    public void SetPrimary(string text, string hexColor)
    {
        BtnPrimary.Content = text;
        BtnPrimary.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor)!);
    }

    public void ShowTimer(bool on) => RecTimer.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    public void SetTimer(string text) => RecTimer.Text = text;

    /// <summary>Re-annotation editor reuse: hide capture-only controls (output modes, record
    /// actions, pin/freeze/delay/sample, timer) and relabel the primary button to "Save".</summary>
    public void SetEditorMode()
    {
        Collapse(ModeImage); Collapse(ModeGif); Collapse(ModeVideo);
        Collapse(BtnPin); Collapse(BtnColorCopy); Collapse(BtnDelay); Collapse(BtnFreeze);
        Collapse(RecTimer);
        SetPrimary("Save", "#3B82F6");
        BtnPrimary.ToolTip = "Save annotations (Enter)";
    }

    private static void Collapse(UIElement e) => e.Visibility = Visibility.Collapsed;

    private RadioButton? _customSwatch;

    /// <summary>Set the active annotation color (from the eyedropper or color wheel) and
    /// surface it as a selected swatch so the user sees what's active.</summary>
    public void SetColor(string hex)
    {
        CurrentColor = hex;
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        if (_customSwatch == null)
        {
            _customSwatch = new RadioButton
            {
                Style = (Style)FindResource("Swatch"),
                GroupName = "color",
            };
            _customSwatch.Checked += (_, __) => CurrentColor = (string)_customSwatch!.Tag;
            ColorPanel.Children.Add(_customSwatch);
        }
        _customSwatch.Background = brush;
        _customSwatch.Tag = hex;
        _customSwatch.IsChecked = true;
        ToolEyedropper.IsChecked = false; // sampling is a one-shot
    }

    private void BuildSwatches()
    {
        var style = (Style)FindResource("Swatch");
        bool first = true;
        foreach (var hex in Palette)
        {
            var rb = new RadioButton
            {
                Style = style,
                GroupName = "color",
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!),
                Tag = hex
            };
            rb.Checked += (_, __) => CurrentColor = (string)rb.Tag;
            if (first) { rb.IsChecked = true; first = false; }
            ColorPanel.Children.Add(rb);
        }
    }

    private void SetTool(ToolKind t)
    {
        CurrentTool = t;
        ToolChanged?.Invoke(t);
    }

    private void Delay3_Click(object sender, RoutedEventArgs e)  => DelayedCaptureRequested?.Invoke(3);
    private void Delay5_Click(object sender, RoutedEventArgs e)  => DelayedCaptureRequested?.Invoke(5);
    private void Delay10_Click(object sender, RoutedEventArgs e) => DelayedCaptureRequested?.Invoke(10);

    // Highlight the freeze button while the region is showing the live (un-frozen) desktop.
    public void SetFreezeActive(bool on) =>
        BtnFreeze.Foreground = on
            ? (System.Windows.Media.Brush)FindResource("B.Accent")
            : (System.Windows.Media.Brush)FindResource("B.Icon");

    private System.Windows.Controls.Primitives.Popup? _wheelPopup;
    private Aquashot.ColorPicker.ColorWheelPopup? _wheelPopupView;

    private void ShowColorWheel()
    {
        if (_wheelPopup == null)
        {
            var view = new Aquashot.ColorPicker.ColorWheelPopup();
            view.ColorChosen += SetColor;
            view.EyedropperRequested += () => EyedropperRequested?.Invoke();
            _wheelPopup = new System.Windows.Controls.Primitives.Popup
            {
                Child = view, StaysOpen = false, AllowsTransparency = true,
                PlacementTarget = BtnColorWheel,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            };
            _wheelPopupView = view;
        }
        _wheelPopupView!.SetColor(CurrentColor);
        _wheelPopup.IsOpen = true;
    }
}
