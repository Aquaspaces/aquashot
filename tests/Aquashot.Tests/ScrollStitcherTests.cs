using System;
using System.Collections.Generic;
using FluentAssertions;
using Aquashot.Capture.ScrollingCapture;
using Xunit;

namespace Aquashot.Tests;

public class ScrollStitcherTests
{
    private const int Bpp = 4;

    // Build a frame whose every row is filled with a byte derived from that row's "content id", so
    // two frames that share content have byte-identical overlapping bands. contentAtRow(r) returns
    // the logical content id painted on output row r.
    private static byte[] Frame(int width, int height, Func<int, byte> contentAtRow)
    {
        var buf = new byte[width * height * Bpp];
        for (int row = 0; row < height; row++)
        {
            byte v = contentAtRow(row);
            int off = row * width * Bpp;
            for (int b = 0; b < width * Bpp; b++) buf[off + b] = v;
        }
        return buf;
    }

    // A "document" of `docRows` distinct rows; a frame viewing it starting at docTop shows rows
    // [docTop, docTop+height). Content id == doc row index (mod 256, offset so 0 stays distinct).
    private static byte[] View(int width, int height, int docTop) =>
        Frame(width, height, row => (byte)(1 + ((docTop + row) % 200)));

    [Fact]
    public void FindVerticalOverlap_returns_scroll_distance()
    {
        int w = 8, h = 300;
        var prev = View(w, h, docTop: 0);
        var next = View(w, h, docTop: 50); // scrolled down 50 rows

        int newRows = ScrollStitcher.FindVerticalOverlap(prev, next, w, h, Bpp, searchBand: 200);
        newRows.Should().Be(50);
    }

    [Fact]
    public void FindVerticalOverlap_identical_frames_is_zero()
    {
        int w = 8, h = 200;
        var a = View(w, h, docTop: 10);
        var b = View(w, h, docTop: 10); // page did not move

        ScrollStitcher.FindVerticalOverlap(a, b, w, h, Bpp, searchBand: 150)
            .Should().Be(0); // terminates the loop
    }

    [Fact]
    public void FindVerticalOverlap_small_step_detected()
    {
        int w = 8, h = 256;
        var prev = View(w, h, docTop: 0);
        var next = View(w, h, docTop: 7);

        ScrollStitcher.FindVerticalOverlap(prev, next, w, h, Bpp, searchBand: 120)
            .Should().Be(7);
    }

    [Fact]
    public void FindVerticalOverlap_step_beyond_searchband_is_clamped_not_found()
    {
        int w = 8, h = 256;
        var prev = View(w, h, docTop: 0);
        var next = View(w, h, docTop: 100);

        // Only search 30 rows down: the true 100-row shift is out of range, so no overlap is found.
        var res = ScrollStitcher.FindVerticalOverlap(prev, next, w, h, Bpp, searchBand: 30, matchBand: 32);
        res.Should().Be(0); // nothing within the band matched the bottom template
    }

    [Fact]
    public void Assemble_first_frame_full_then_only_new_rows()
    {
        int w = 4, h = 100;
        var f0 = View(w, h, 0);
        var f1 = View(w, h, 20); // 20 new rows
        var f2 = View(w, h, 35); // 15 new rows

        var frames = new List<(byte[] frame, int newRows)>
        {
            (f0, h),
            (f1, 20),
            (f2, 15),
        };

        var (pixels, height) = ScrollStitcher.Assemble(frames, w, Bpp, h);
        height.Should().Be(h + 20 + 15);
        pixels.Length.Should().Be(height * w * Bpp);
    }

    [Fact]
    public void Assemble_does_not_duplicate_overlap_rows()
    {
        // One document of 130 distinct rows; two 100-row frames (docTop 0 and 30) reconstruct it
        // exactly: full first frame (rows 0..99) + 30 new rows (100..129). Assembled output row r
        // must equal document content id at row r — no duplicated overlap band.
        int w = 4, h = 100, step = 30;
        var f0 = View(w, h, 0);
        var f1 = View(w, h, step);

        var frames = new List<(byte[] frame, int newRows)> { (f0, h), (f1, step) };
        var (pixels, height) = ScrollStitcher.Assemble(frames, w, Bpp, h);

        height.Should().Be(h + step); // 130 rows
        for (int row = 0; row < height; row++)
        {
            byte expected = (byte)(1 + (row % 200));
            pixels[row * w * Bpp].Should().Be(expected, $"row {row} should hold doc content {expected}");
        }
    }

    [Fact]
    public void Assemble_skips_frames_with_no_new_rows()
    {
        int w = 4, h = 50;
        var f0 = View(w, h, 0);
        var stuck = View(w, h, 0);

        var frames = new List<(byte[] frame, int newRows)> { (f0, h), (stuck, 0) };
        var (_, height) = ScrollStitcher.Assemble(frames, w, Bpp, h);
        height.Should().Be(h); // the zero-new-rows frame contributes nothing
    }

    [Fact]
    public void Assemble_empty_input_is_empty()
    {
        var (pixels, height) = ScrollStitcher.Assemble(
            new List<(byte[] frame, int newRows)>(), 10, Bpp, 10);
        height.Should().Be(0);
        pixels.Should().BeEmpty();
    }
}
