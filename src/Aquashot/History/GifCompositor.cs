using System;
using System.Collections.Generic;

namespace Aquashot.History;

// Composites GIF frames (each a sub-rectangle of the logical screen) into full frames, honouring
// the disposal method that says what happens to a frame's area before the next one is drawn:
//   0/1 = leave it, 2 = clear it to transparent, 3 = restore what was there before it was drawn.
// Pure BGRA32 byte buffers so it can be tested without decoding real GIFs.
public static class GifCompositor
{
    public sealed record Frame(byte[] Bgra, int Left, int Top, int Width, int Height, int Disposal);

    public static List<byte[]> Compose(int width, int height, IReadOnlyList<Frame> frames)
    {
        var canvas = new byte[width * height * 4]; // starts fully transparent
        var result = new List<byte[]>(frames.Count);
        foreach (var f in frames)
        {
            byte[]? before = f.Disposal == 3 ? (byte[])canvas.Clone() : null;
            Draw(canvas, width, height, f);
            result.Add((byte[])canvas.Clone());

            if (f.Disposal == 2) Clear(canvas, width, height, f);
            else if (before != null) canvas = before;
        }
        return result;
    }

    // GIF transparency is all-or-nothing: transparent pixels let the canvas show through.
    private static void Draw(byte[] canvas, int width, int height, Frame f)
    {
        ForEachVisibleRow(width, height, f, (srcRow, dstRow, x0, x1) =>
        {
            for (int x = x0; x < x1; x++)
            {
                int s = srcRow + (x - f.Left) * 4, d = dstRow + x * 4;
                if (f.Bgra[s + 3] == 0) continue;
                canvas[d] = f.Bgra[s];
                canvas[d + 1] = f.Bgra[s + 1];
                canvas[d + 2] = f.Bgra[s + 2];
                canvas[d + 3] = 255;
            }
        });
    }

    private static void Clear(byte[] canvas, int width, int height, Frame f) =>
        ForEachVisibleRow(width, height, f, (_, dstRow, x0, x1) =>
            Array.Clear(canvas, dstRow + x0 * 4, (x1 - x0) * 4));

    // Visits the part of the frame rectangle that lies on the canvas (frames may overhang it).
    private static void ForEachVisibleRow(int width, int height, Frame f, Action<int, int, int, int> row)
    {
        int x0 = Math.Max(0, f.Left), x1 = Math.Min(width, f.Left + f.Width);
        int y0 = Math.Max(0, f.Top), y1 = Math.Min(height, f.Top + f.Height);
        if (x0 >= x1 || y0 >= y1) return;
        for (int y = y0; y < y1; y++)
            row((y - f.Top) * f.Width * 4, y * width * 4, x0, x1);
    }
}
