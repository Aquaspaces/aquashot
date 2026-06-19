using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Aquashot.Annotation;
using Aquashot.History;

namespace Aquashot.Redaction;

// OCR-driven redaction: pick which recognized lines to hide, then emit Blur/Pixelate shapes
// (in crop-local pixel space) over their boxes. Pure logic so it's unit-testable.
public static class AutoRedactor
{
    private const double Pad = 2; // grow each box a touch so glyph edges are fully covered

    // Choose which OCR lines to redact. Empty/blank pattern set => every line. Otherwise keep
    // lines matching ANY ';'-delimited regex (case-insensitive). Invalid regexes are skipped.
    public static IReadOnlyList<OcrLine> SelectLines(IReadOnlyList<OcrLine> lines, string patternsSemicolon)
    {
        if (lines == null || lines.Count == 0) return Array.Empty<OcrLine>();
        // A blank pattern set means "no filter" => redact every line. A non-blank set that compiles
        // to zero usable regexes (every pattern invalid) is NOT "no filter" => redact nothing.
        if (string.IsNullOrWhiteSpace(patternsSemicolon)) return lines.ToList();
        var regexes = CompilePatterns(patternsSemicolon);
        if (regexes.Count == 0) return Array.Empty<OcrLine>(); // patterns given but all invalid => match nothing

        var result = new List<OcrLine>();
        foreach (var line in lines)
            if (regexes.Any(rx => SafeMatch(rx, line.Text)))
                result.Add(line);
        return result;
    }

    // Build redaction shapes for the chosen lines, translating boxes from source-image pixels to
    // crop-local pixels (subtract the crop origin). Style is "Pixelate" => PixelateShape, else Blur.
    public static IReadOnlyList<Shape> BuildShapes(IReadOnlyList<OcrLine> lines,
        double cropOffX, double cropOffY, string style, double blurRadius, int pixelateBlock)
    {
        var shapes = new List<Shape>();
        if (lines == null) return shapes;
        bool pixelate = string.Equals(style, "Pixelate", StringComparison.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var b = line.BoxPx;
            double x = b.X - cropOffX - Pad;
            double y = b.Y - cropOffY - Pad;
            double w = b.Width + Pad * 2;
            double h = b.Height + Pad * 2;
            if (w <= 0 || h <= 0) continue;
            shapes.Add(pixelate
                ? new PixelateShape(x, y, w, h, Math.Max(2, pixelateBlock))
                : new BlurShape(x, y, w, h, Math.Max(1, blurRadius)));
        }
        return shapes;
    }

    private static List<Regex> CompilePatterns(string patternsSemicolon)
    {
        var list = new List<Regex>();
        if (string.IsNullOrWhiteSpace(patternsSemicolon)) return list;
        foreach (var raw in patternsSemicolon.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pat = raw.Trim();
            if (pat.Length == 0) continue;
            try { list.Add(new Regex(pat, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)); }
            catch { /* skip invalid regex (best-effort, never throw) */ }
        }
        return list;
    }

    private static bool SafeMatch(Regex rx, string text)
    {
        try { return rx.IsMatch(text ?? ""); } catch { return false; }
    }
}
