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
    public event Action? ConfirmRequested;
    public event Action? CancelRequested;
    public event Action? PinRequested;
    public event Action<CaptureOutput>? OutputModeChanged;

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
        BtnConfirm.Click += (_, __) => ConfirmRequested?.Invoke();
        BtnCancel.Click  += (_, __) => CancelRequested?.Invoke();
        BtnPin.Click     += (_, __) => PinRequested?.Invoke();

        ModeImage.Checked += (_, __) => SetOutput(CaptureOutput.Image);
        ModeGif.Checked   += (_, __) => SetOutput(CaptureOutput.Gif);
        ModeVideo.Checked += (_, __) => SetOutput(CaptureOutput.Video);

        BuildSwatches();
        ToolArrow.IsChecked = true;
        ModeImage.IsChecked = true;
    }

    // Recording modes capture the live region, so annotation controls don't apply —
    // disable them when GIF/MP4 is selected. Confirm then starts recording (handled
    // by the overlay, which reads CurrentOutput).
    private void SetOutput(CaptureOutput o)
    {
        CurrentOutput = o;
        bool ann = o == CaptureOutput.Image;
        foreach (var rb in new RadioButton[]
                 { ToolSelect, ToolArrow, ToolRect, ToolEllipse, ToolLine, ToolPen, ToolText, ToolCounter })
            rb.IsEnabled = ann;
        WidthSlider.IsEnabled = ann;
        FillToggle.IsEnabled = ann;
        ColorPanel.IsEnabled = ann;
        BtnUndo.IsEnabled = ann;
        BtnRedo.IsEnabled = ann;
        BtnPin.IsEnabled = ann; // pinning only applies to the screenshot (Image) flow
        OutputModeChanged?.Invoke(o);
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
}
