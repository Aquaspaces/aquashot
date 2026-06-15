using Aquashot.Selection;

namespace Aquashot.Annotation;

public static class BlurRegion
{
    public static IReadOnlyList<PixelRect> PixelateBlocks(PixelRect region, double blockSize)
    {
        var blocks = new List<PixelRect>();
        for (double y = region.Y; y < region.Bottom; y += blockSize)
        for (double x = region.X; x < region.Right; x += blockSize)
        {
            double w = Math.Min(blockSize, region.Right - x);
            double h = Math.Min(blockSize, region.Bottom - y);
            blocks.Add(new PixelRect(x, y, w, h));
        }
        return blocks;
    }
}
