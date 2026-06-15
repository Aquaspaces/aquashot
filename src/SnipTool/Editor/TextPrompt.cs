using System.Windows;
using System.Windows.Controls;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace SnipTool.Editor;

public static class TextPrompt
{
    public static string? Ask()
    {
        var win = new Window
        {
            Width = 320, Height = 130, Title = "Add text", Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowStyle = WindowStyle.ToolWindow, ResizeMode = ResizeMode.NoResize
        };
        var tb = new TextBox { Margin = new Thickness(10), MinWidth = 280 };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        var panel = new StackPanel();
        panel.Children.Add(tb);
        panel.Children.Add(ok);
        win.Content = panel;
        ok.Click += (_, __) => { win.DialogResult = true; };
        win.Loaded += (_, __) => tb.Focus();
        return win.ShowDialog() == true && tb.Text.Length > 0 ? tb.Text : null;
    }
}
