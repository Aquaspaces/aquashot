using System;
using System.Collections.Generic;

namespace Aquashot.Capture.ScrollingCapture;

// Pure vertical-stitch maths for scrolling capture: given consecutive frames of a region that
// scrolled down between grabs, find how much NEW content the next frame revealed (by template-
// matching a band of rows from the bottom of the previous frame against the next frame), then
// assemble the kept rows into one tall buffer. WPF-free so it can be unit-tested directly on
// row-major pixel buffers (BGRA or grayscale — any fixed bytesPerPixel).
public static class ScrollStitcher
{
    // Find the number of rows of NEW content at the bottom of `next` relative to `prev`.
    //
    // We take a horizontal template band of `matchBand` rows from the bottom of `prev`, then slide
    // it down `next` looking for the offset that minimises the sum of absolute differences. The
    // best offset is where `prev`'s bottom band reappears inside `next`; everything below that in
    // `next` is new content. Returns rows in [0, height].
    //
    //   newRows == 0      -> the page did not move (frames identical / fully overlapping) -> stop
    //   newRows == height -> no overlap found (whole frame is new) -> appended verbatim
    //
    // `searchBand` caps how far down we look for the match (the max scroll step we expect); a
    // smaller band is faster and avoids spurious far matches.
    // `tolerance` is the maximum average per-byte absolute difference (0..255) the best-matching
    // band may have to count as a real overlap. A poor best match (e.g. the true scroll exceeded
    // `searchBand`, so nothing genuinely lines up) is rejected and reported as 0 new rows rather
    // than stitching garbage.
    public static int FindVerticalOverlap(ReadOnlySpan<byte> prev, ReadOnlySpan<byte> next,
        int width, int height, int bytesPerPixel, int searchBand = 200, int matchBand = 64,
        double tolerance = 6.0)
    {
        // 0 new rows is the only safe sentinel for unusable input: returning a positive height for a
        // zero width/bpp would make the caller assemble a zero-stride buffer and throw downstream.
        if (width <= 0 || height <= 0 || bytesPerPixel <= 0) return 0;

        int stride = width * bytesPerPixel;
        int band = Math.Clamp(matchBand, 1, height);
        int maxShift = Math.Clamp(searchBand, 1, height - 1 <= 0 ? 1 : height - 1);

        // The template is the bottom `band` rows of prev. Its top row in prev is at (height - band).
        int templateTop = height - band;

        // Baseline: the cost of NO movement (the template still sits where it was in prev). The page
        // did not scroll unless some positive shift aligns the template strictly better than this; a
        // pair of identical / non-moving frames has a zero-cost baseline that no shift can beat, so
        // we correctly report 0 new rows.
        long bestCost = BandCost(prev, next, stride, templateTop, templateTop, band, width, bytesPerPixel, long.MaxValue);
        int bestShift = 0;

        // shift = how many rows the content moved up between prev and next. At shift s, prev's
        // template (originally at row templateTop) should appear in next at row (templateTop - s).
        for (int s = 1; s <= maxShift; s++)
        {
            int nextTop = templateTop - s;
            if (nextTop < 0) break; // template would run off the top of next

            long cost = BandCost(prev, next, stride, templateTop, nextTop, band, width, bytesPerPixel, bestCost);
            if (cost < bestCost) { bestCost = cost; bestShift = s; }
        }

        if (bestShift <= 0) return 0; // no plausible downward scroll detected -> page didn't move

        // Reject a best match that isn't actually similar (avg per-byte diff over the tolerance).
        long bandBytes = (long)band * width * bytesPerPixel;
        if (bandBytes > 0 && (double)bestCost / bandBytes > tolerance) return 0;

        // The content moved up by bestShift rows, so bestShift rows of new content scrolled in at
        // the bottom. Clamp defensively.
        return Math.Clamp(bestShift, 0, height);
    }

    // Sum of absolute byte differences between prev's band (rows [pTop, pTop+band)) and next's band
    // (rows [nTop, nTop+band)). Early-outs once the running cost exceeds the best found so far.
    private static long BandCost(ReadOnlySpan<byte> prev, ReadOnlySpan<byte> next, int stride,
        int pTop, int nTop, int band, int width, int bpp, long bestCost)
    {
        long cost = 0;
        int rowBytes = width * bpp;
        for (int row = 0; row < band; row++)
        {
            int pOff = (pTop + row) * stride;
            int nOff = (nTop + row) * stride;
            for (int b = 0; b < rowBytes; b++)
                cost += Math.Abs(prev[pOff + b] - next[nOff + b]);
            if (cost >= bestCost) return long.MaxValue; // can't beat the incumbent; bail
        }
        return cost;
    }

    // Compose the captured frames into one tall buffer. The first frame contributes all its rows;
    // each subsequent frame contributes only its bottom `newRows` rows (the rest overlapped the
    // prior frame and is already present). Frames with newRows <= 0 add nothing.
    public static (byte[] pixels, int height) Assemble(
        IReadOnlyList<(byte[] frame, int newRows)> frames, int width, int bytesPerPixel, int frameHeight)
    {
        if (frames.Count == 0 || width <= 0 || bytesPerPixel <= 0 || frameHeight <= 0)
            return (Array.Empty<byte>(), 0);

        int stride = width * bytesPerPixel;

        int total = frameHeight; // first frame in full
        for (int i = 1; i < frames.Count; i++)
            total += Math.Clamp(frames[i].newRows, 0, frameHeight);

        long bytes = (long)total * stride;
        if (bytes <= 0 || bytes > int.MaxValue) return (Array.Empty<byte>(), 0); // too large to hold in one array
        var outBuf = new byte[bytes];

        int destRow = 0;

        // First frame: copy in full.
        Buffer.BlockCopy(frames[0].frame, 0, outBuf, 0, Math.Min(frames[0].frame.Length, frameHeight * stride));
        destRow += frameHeight;

        // Subsequent frames: copy only the new bottom rows.
        for (int i = 1; i < frames.Count; i++)
        {
            int newRows = Math.Clamp(frames[i].newRows, 0, frameHeight);
            if (newRows == 0) continue;
            int srcRowStart = frameHeight - newRows;          // new rows live at the bottom
            int srcOff = srcRowStart * stride;
            int rowBytes = newRows * stride;
            if (srcOff + rowBytes > frames[i].frame.Length) rowBytes = Math.Max(0, frames[i].frame.Length - srcOff);
            Buffer.BlockCopy(frames[i].frame, srcOff, outBuf, destRow * stride, rowBytes);
            destRow += newRows;
        }

        return (outBuf, total);
    }
}
