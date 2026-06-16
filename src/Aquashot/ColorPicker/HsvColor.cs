using System;
using System.Globalization;

namespace Aquashot.ColorPicker;

// Hue 0-360, Saturation 0-1, Value 0-1. Conversions are exact enough to round-trip bytes.
public readonly record struct HsvColor(double H, double S, double V)
{
    public static HsvColor FromRgb(byte r, byte g, byte b)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double d = max - min;

        double h = 0;
        if (d > 0)
        {
            if (max == rd)      h = 60 * (((gd - bd) / d) % 6);
            else if (max == gd) h = 60 * (((bd - rd) / d) + 2);
            else                h = 60 * (((rd - gd) / d) + 4);
        }
        if (h < 0) h += 360;

        double s = max == 0 ? 0 : d / max;
        return new HsvColor(h, s, max);
    }

    public (byte r, byte g, byte b) ToRgb()
    {
        double h = ((H % 360) + 360) % 360;
        double c = V * S;
        double x = c * (1 - Math.Abs(((h / 60.0) % 2) - 1));
        double m = V - c;
        double r1 = 0, g1 = 0, b1 = 0;
        if      (h < 60)  { r1 = c; g1 = x; }
        else if (h < 120) { r1 = x; g1 = c; }
        else if (h < 180) { g1 = c; b1 = x; }
        else if (h < 240) { g1 = x; b1 = c; }
        else if (h < 300) { r1 = x; b1 = c; }
        else              { r1 = c; b1 = x; }

        return (Byte((r1 + m) * 255), Byte((g1 + m) * 255), Byte((b1 + m) * 255));
    }

    private static byte Byte(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

    public static HsvColor FromHex(string hex)
    {
        var s = hex.TrimStart('#');
        byte r = byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber);
        byte g = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber);
        byte b = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber);
        return FromRgb(r, g, b);
    }

    public string ToHex()
    {
        var (r, g, b) = ToRgb();
        return ColorHex.Rgb(r, g, b);
    }
}
