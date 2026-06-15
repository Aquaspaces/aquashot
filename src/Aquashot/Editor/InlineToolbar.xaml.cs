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

public partial class InlineToolbar : UserControl
{
    public event Action<ToolKind>? ToolChanged;
    public event Action? UndoRequested;
    public event Action? RedoRequested;
    public event Action? ConfirmRequested;
    public event Action? CancelRequested;

    public ToolKind CurrentTool { get; private set; } = ToolKind.Arrow;
    public string CurrentColor { get; private set; } = "#FF3B30";
    public double CurrentWidth => WidthSlider.Value;

    private static readonly string[] Palette =
        { "#FF3B30", "#FFCC00", "#34C759", "#0A84FF", "#FFFFFF", "#111111" };

    public InlineToolbar()
    {
        InitializeComponent();

        ToolArrow.Checked   += (_, __) => SetTool(ToolKind.Arrow);
        ToolRect.Checked    += (_, __) => SetTool(ToolKind.Rect);
        ToolEllipse.Checked += (_, __) => SetTool(ToolKind.Ellipse);
        ToolLine.Checked    += (_, __) => SetTool(ToolKind.Line);
        ToolPen.Checked     += (_, __) => SetTool(ToolKind.Pen);
        ToolText.Checked    += (_, __) => SetTool(ToolKind.Text);
        ToolCounter.Checked += (_, __) => SetTool(ToolKind.Counter);
        ToolBlur.Checked    += (_, __) => SetTool(ToolKind.Blur);

        BtnUndo.Click    += (_, __) => UndoRequested?.Invoke();
        BtnRedo.Click    += (_, __) => RedoRequested?.Invoke();
        BtnConfirm.Click += (_, __) => ConfirmRequested?.Invoke();
        BtnCancel.Click  += (_, __) => CancelRequested?.Invoke();

        BuildSwatches();
        ToolArrow.IsChecked = true;
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
