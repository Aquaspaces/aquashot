using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Aquashot.History;
using FluentAssertions;
using Xunit;

namespace Aquashot.Tests;

public class GifCompositorTests
{
    private const byte R = 1, G = 2, B = 3; // marker colours (stored in the blue channel)

    // A w×h frame filled with one opaque marker, or transparent where marker == 0.
    private static GifCompositor.Frame Solid(int left, int top, int w, int h, byte marker, int disposal)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            px[i * 4] = marker;
            px[i * 4 + 3] = marker == 0 ? (byte)0 : (byte)255;
        }
        return new GifCompositor.Frame(px, left, top, w, h, disposal);
    }

    // (marker, alpha) at a canvas pixel.
    private static (byte Marker, byte Alpha) At(byte[] canvas, int width, int x, int y) =>
        (canvas[(y * width + x) * 4], canvas[(y * width + x) * 4 + 3]);

    [Fact]
    public void Partial_frames_draw_over_the_previous_composite()
    {
        var frames = GifCompositor.Compose(4, 4, new[]
        {
            Solid(0, 0, 4, 4, R, disposal: 1),
            Solid(1, 1, 2, 2, G, disposal: 1),
        });
        At(frames[1], 4, 0, 0).Should().Be((R, (byte)255));
        At(frames[1], 4, 1, 1).Should().Be((G, (byte)255));
    }

    [Fact]
    public void Disposal_2_clears_only_the_frame_area_to_transparent()
    {
        var frames = GifCompositor.Compose(4, 4, new[]
        {
            Solid(0, 0, 4, 4, R, disposal: 1),
            Solid(1, 1, 2, 2, G, disposal: 2),
            Solid(0, 0, 1, 1, B, disposal: 1),
        });
        At(frames[2], 4, 1, 1).Alpha.Should().Be(0, "the disposed frame's area is cleared");
        At(frames[2], 4, 3, 3).Should().Be((R, (byte)255), "pixels outside it are kept");
        At(frames[2], 4, 0, 0).Should().Be((B, (byte)255));
    }

    [Fact]
    public void Disposal_3_restores_what_was_there_before_the_frame()
    {
        var frames = GifCompositor.Compose(4, 4, new[]
        {
            Solid(0, 0, 4, 4, R, disposal: 1),
            Solid(1, 1, 2, 2, G, disposal: 3),
            Solid(3, 3, 1, 1, B, disposal: 1),
        });
        At(frames[1], 4, 1, 1).Should().Be((G, (byte)255));
        At(frames[2], 4, 1, 1).Should().Be((R, (byte)255), "the background under the frame came back");
        At(frames[2], 4, 3, 3).Should().Be((B, (byte)255));
    }

    [Fact]
    public void Transparent_pixels_let_the_previous_composite_show_through()
    {
        var frames = GifCompositor.Compose(2, 1, new[]
        {
            Solid(0, 0, 2, 1, R, disposal: 1),
            Solid(0, 0, 2, 1, 0, disposal: 1), // fully transparent frame
        });
        At(frames[1], 2, 0, 0).Should().Be((R, (byte)255));
    }

    [Fact]
    public void Frames_overhanging_the_canvas_are_clipped()
    {
        var act = () => GifCompositor.Compose(2, 2, new[] { Solid(1, 1, 4, 4, G, disposal: 2), Solid(0, 0, 1, 1, R, 1) });
        var frames = act.Should().NotThrow().Subject;
        At(frames[0], 2, 1, 1).Should().Be((G, (byte)255));
        At(frames[1], 2, 1, 1).Alpha.Should().Be(0);
    }

    [Fact]
    public void GifAnimator_loads_every_frame_of_a_real_gif()
    {
        string path = Path.Combine(Path.GetTempPath(), "aqua-gif-" + Guid.NewGuid().ToString("N") + ".gif");
        try
        {
            var count = RunSta(() =>
            {
                var enc = new GifBitmapEncoder();
                foreach (var color in new[] { Colors.Red, Colors.Lime, Colors.Blue })
                {
                    var px = new byte[8 * 6 * 4];
                    for (int i = 0; i < 8 * 6; i++)
                    { px[i * 4] = color.B; px[i * 4 + 1] = color.G; px[i * 4 + 2] = color.R; px[i * 4 + 3] = 255; }
                    enc.Frames.Add(BitmapFrame.Create(
                        BitmapSource.Create(8, 6, 96, 96, PixelFormats.Bgra32, null, px, 8 * 4)));
                }
                using (var fs = File.Create(path)) enc.Save(fs);
                var clip = GifAnimator.Load(path);
                clip.Should().NotBeNull();
                clip!.Frames.Should().AllSatisfy(f => { f.PixelWidth.Should().Be(8); f.PixelHeight.Should().Be(6); });
                return clip.Frames.Count;
            });
            count.Should().Be(3);
        }
        finally { File.Delete(path); }
    }

    private static T RunSta<T>(Func<T> f)
    {
        T result = default!;
        Exception? error = null;
        var t = new Thread(() => { try { result = f(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw error;
        return result;
    }
}
