namespace Aquashot.ColorPicker;

public static class ColorHex
{
    // "#RRGGBB" upper-case, the form users paste into CSS / design tools.
    public static string Rgb(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";
}
