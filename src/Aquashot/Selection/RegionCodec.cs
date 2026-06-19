using System.Globalization;

namespace Aquashot.Selection;

// Encodes/decodes a region as "X,Y,W,H" (virtual px) for round-tripping through AppSettings.
// Used by the last-region-repeat feature to remember the last committed selection.
public static class RegionCodec
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Encode(PixelRect r) => string.Join(",",
        R(r.X), R(r.Y), R(r.Width), R(r.Height));

    private static string R(double v) => Math.Round(v).ToString(Inv);

    // Parse "X,Y,W,H". Returns false (and a default rect) on blank/malformed input or a
    // non-positive size, so callers can guard before re-capturing.
    public static bool TryDecode(string? text, out PixelRect rect)
    {
        rect = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split(',');
        if (parts.Length != 4) return false;
        if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, Inv, out double x)) return false;
        if (!double.TryParse(parts[1].Trim(), NumberStyles.Float, Inv, out double y)) return false;
        if (!double.TryParse(parts[2].Trim(), NumberStyles.Float, Inv, out double w)) return false;
        if (!double.TryParse(parts[3].Trim(), NumberStyles.Float, Inv, out double h)) return false;
        if (w < 1 || h < 1) return false;
        rect = new PixelRect(x, y, w, h);
        return true;
    }
}
