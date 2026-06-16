using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using Aquashot.Output;
using Xunit;

namespace Aquashot.Tests;

public class MetadataStripperTests
{
    private static readonly byte[] PngSig = { 137, 80, 78, 71, 13, 10, 26, 10 };

    private static void Chunk(List<byte> b, string type, byte[] data)
    {
        int len = data.Length;
        b.AddRange(new byte[] { (byte)(len >> 24), (byte)(len >> 16), (byte)(len >> 8), (byte)len });
        b.AddRange(Encoding.ASCII.GetBytes(type));
        b.AddRange(data);
        b.AddRange(new byte[] { 0, 0, 0, 0 }); // dummy CRC (stripper doesn't validate)
    }

    private static bool Contains(byte[] hay, string ascii)
    {
        var needle = Encoding.ASCII.GetBytes(ascii);
        for (int i = 0; i + needle.Length <= hay.Length; i++)
        {
            bool ok = true;
            for (int j = 0; j < needle.Length; j++)
                if (hay[i + j] != needle[j]) { ok = false; break; }
            if (ok) return true;
        }
        return false;
    }

    [Fact]
    public void StripPng_RemovesTextAndTimeChunks_KeepsImageChunks()
    {
        var b = new List<byte>();
        b.AddRange(PngSig);
        Chunk(b, "IHDR", new byte[13]);
        Chunk(b, "tEXt", Encoding.ASCII.GetBytes("Software\0Aquashot secret"));
        Chunk(b, "tIME", new byte[7]);
        Chunk(b, "IDAT", new byte[] { 1, 2, 3, 4 });
        Chunk(b, "IEND", System.Array.Empty<byte>());

        var outp = MetadataStripper.Strip(b.ToArray());

        Contains(outp, "tEXt").Should().BeFalse();
        Contains(outp, "tIME").Should().BeFalse();
        Contains(outp, "Aquashot secret").Should().BeFalse();
        Contains(outp, "IHDR").Should().BeTrue();
        Contains(outp, "IDAT").Should().BeTrue();
        Contains(outp, "IEND").Should().BeTrue();
        outp.Take(8).Should().Equal(PngSig);
    }

    [Fact]
    public void StripJpeg_RemovesApp1Exif_KeepsApp0AndImageData()
    {
        var b = new List<byte> { 0xFF, 0xD8 };
        // APP1 (EXIF) — should be dropped
        var exif = Encoding.ASCII.GetBytes("Exif\0\0GPS:1.0,2.0");
        b.AddRange(new byte[] { 0xFF, 0xE1, (byte)((exif.Length + 2) >> 8), (byte)(exif.Length + 2) });
        b.AddRange(exif);
        // APP0 (JFIF) — should be kept
        var jfif = Encoding.ASCII.GetBytes("JFIF\0");
        b.AddRange(new byte[] { 0xFF, 0xE0, (byte)((jfif.Length + 2) >> 8), (byte)(jfif.Length + 2) });
        b.AddRange(jfif);
        // SOS + entropy data + EOI
        b.AddRange(new byte[] { 0xFF, 0xDA, 0x00, 0x02, 0xAA, 0xBB, 0xFF, 0xD9 });

        var outp = MetadataStripper.Strip(b.ToArray());

        Contains(outp, "GPS:1.0,2.0").Should().BeFalse();
        Contains(outp, "JFIF").Should().BeTrue();
        // entropy bytes after SOS preserved
        outp.Should().ContainInOrder(new byte[] { 0xFF, 0xDA, 0x00, 0x02, 0xAA, 0xBB, 0xFF, 0xD9 });
        // no APP1 marker remains
        HasMarker(outp, 0xE1).Should().BeFalse();
    }

    private static bool HasMarker(byte[] d, byte marker)
    {
        for (int i = 0; i + 1 < d.Length; i++)
            if (d[i] == 0xFF && d[i + 1] == marker) return true;
        return false;
    }

    [Fact]
    public void Strip_UnknownFormat_ReturnedUnchanged()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        MetadataStripper.Strip(data).Should().Equal(data);
    }
}
