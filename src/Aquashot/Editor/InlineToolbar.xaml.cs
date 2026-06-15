using System;
using System.Windows.Controls;
using Aquashot.Annotation;
using UserControl = System.Windows.Controls.UserControl;

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

    public InlineToolbar()
    {
        InitializeComponent();
        BtnArrow.Click   += (_, __) => SetTool(ToolKind.Arrow);
        BtnRect.Click    += (_, __) => SetTool(ToolKind.Rect);
        BtnEllipse.Click += (_, __) => SetTool(ToolKind.Ellipse);
        BtnLine.Click    += (_, __) => SetTool(ToolKind.Line);
        BtnPen.Click     += (_, __) => SetTool(ToolKind.Pen);
        BtnText.Click    += (_, __) => SetTool(ToolKind.Text);
        BtnCounter.Click += (_, __) => SetTool(ToolKind.Counter);
        BtnBlur.Click    += (_, __) => SetTool(ToolKind.Blur);
        BtnUndo.Click    += (_, __) => UndoRequested?.Invoke();
        BtnRedo.Click    += (_, __) => RedoRequested?.Invoke();
        BtnConfirm.Click += (_, __) => ConfirmRequested?.Invoke();
        BtnCancel.Click  += (_, __) => CancelRequested?.Invoke();

        foreach (var c in new[] { "#FF3B30", "#FFCC00", "#34C759", "#0A84FF", "#FFFFFF", "#000000" })
            ColorBox.Items.Add(c);
        ColorBox.SelectedIndex = 0;
        ColorBox.SelectionChanged += (_, __) =>
        {
            if (ColorBox.SelectedItem is string s) CurrentColor = s;
        };
    }

    private void SetTool(ToolKind t)
    {
        CurrentTool = t;
        ToolChanged?.Invoke(t);
    }
}
