using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using SystemColors = System.Windows.SystemColors;

namespace Aquashot.Editor;

public static class TextPrompt
{
    private static object? Res(string key) => Application.Current?.TryFindResource(key);

    public static string? Ask()
    {
        var win = new Window
        {
            Width = 320, Height = 130, Title = "Add text", Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowStyle = WindowStyle.ToolWindow, ResizeMode = ResizeMode.NoResize,
            Background = Res("B.Surface") as Brush ?? SystemColors.WindowBrush
        };
        var tb = new TextBox { Margin = new Thickness(10), MinWidth = 280 };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true,
            Style = Res("ModernButton") as Style };
        var panel = new StackPanel();
        panel.Children.Add(tb);
        panel.Children.Add(ok);
        win.Content = panel;
        ok.Click += (_, __) => { win.DialogResult = true; };
        win.Loaded += (_, __) => tb.Focus();
        return win.ShowDialog() == true && tb.Text.Length > 0 ? tb.Text : null;
    }
}
