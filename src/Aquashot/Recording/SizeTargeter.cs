using System;

namespace Aquashot.Recording;

public static class SizeTargeter
{
    public const long DefaultBudgetBytes = 50L * 1024 * 1024;
    private const int MaxGifFps = 20;
    private const int MaxGifWidth = 800;
    private const int MinGifFps = 8;
    private const int MinGifWidth = 240;

    // Bits budget / seconds, with 8% headroom for container overhead, floored at 200 kbps.
    public static int BitrateKbps(TimeSpan duration, long budgetBytes)
    {
        double sec = Math.Max(0.5, duration.TotalSeconds);
        double bits = budgetBytes * 8 * 0.92;
        return Math.Max(200, (int)(bits / 1000.0 / sec));
    }

    public static (int fps, int width) GifPlan(int sourceWidth, int requestedFps) =>
        (Math.Min(MaxGifFps, requestedFps), Math.Min(MaxGifWidth, sourceWidth));

    public static (int fps, int width) Shrink((int fps, int width) p) =>
        (Math.Max(MinGifFps, p.fps / 2), Math.Max(MinGifWidth, p.width / 2));

    public static bool OverBudget(long actualBytes, long budgetBytes) => actualBytes > budgetBytes;
}
