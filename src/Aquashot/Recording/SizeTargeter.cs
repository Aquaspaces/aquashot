using System;

namespace Aquashot.Recording;

public static class SizeTargeter
{
    public const long DefaultBudgetBytes = 50L * 1024 * 1024;
    // Bitrate used when the video budget is unlimited (long.MaxValue): a high cap that keeps
    // ffmpeg's maxrate/bufsize math (×3/2, ×2) well clear of int overflow.
    public const int UnlimitedKbps = 100_000;
    private const int MinGifFps = 8;
    private const int MinGifWidth = 240;

    // MB budget -> bytes; 0 (or less) means unlimited.
    public static long BudgetBytes(int sizeMb) => sizeMb <= 0 ? long.MaxValue : (long)sizeMb * 1024 * 1024;

    // Bits budget / seconds, with 8% headroom for container overhead, floored at 200 kbps.
    // Unlimited budget short-circuits to a high constant — the raw arithmetic on long.MaxValue
    // would overflow the int cast and collapse back to the 200 kbps floor.
    public static int BitrateKbps(TimeSpan duration, long budgetBytes)
    {
        if (budgetBytes >= long.MaxValue) return UnlimitedKbps;
        double sec = Math.Max(0.5, duration.TotalSeconds);
        double bits = budgetBytes * 8 * 0.92;
        return Math.Max(200, (int)(bits / 1000.0 / sec));
    }

    public static (int fps, int width) GifPlan(int sourceWidth, int requestedFps, int maxFps, int maxWidth) =>
        (Math.Min(maxFps, requestedFps), Math.Min(maxWidth, sourceWidth));

    public static (int fps, int width) Shrink((int fps, int width) p) =>
        (Math.Max(MinGifFps, p.fps / 2), Math.Max(MinGifWidth, p.width / 2));

    public static bool OverBudget(long actualBytes, long budgetBytes) => actualBytes > budgetBytes;
}
