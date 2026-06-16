using System;
using System.IO;
using System.Text;

namespace Aquashot.Output;

// Removes metadata segments from encoded image bytes so saved screenshots don't leak
// ancillary data (text comments, timestamps, EXIF/XMP/IPTC). Image and colour data are
// preserved; only metadata chunks/segments are dropped. Format is detected from the magic
// bytes, so it is robust regardless of the requested extension.
public static class MetadataStripper
{
    private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    public static byte[] Strip(byte[] data)
    {
        if (IsPng(data)) return StripPng(data);
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8) return StripJpeg(data);
        return data;
    }

    private static bool IsPng(byte[] d)
    {
        if (d.Length < PngSignature.Length) return false;
        for (int i = 0; i < PngSignature.Length; i++)
            if (d[i] != PngSignature[i]) return false;
        return true;
    }

    private static byte[] StripPng(byte[] d)
    {
        using var ms = new MemoryStream(d.Length);
        ms.Write(PngSignature, 0, PngSignature.Length);
        int i = PngSignature.Length;
        while (i + 8 <= d.Length)
        {
            int len = (d[i] << 24) | (d[i + 1] << 16) | (d[i + 2] << 8) | d[i + 3];
            if (len < 0) break;
            int total = 12 + len; // length(4) + type(4) + data(len) + crc(4)
            if (i + total > d.Length) break;
            string type = Encoding.ASCII.GetString(d, i + 4, 4);
            bool drop = type is "tEXt" or "zTXt" or "iTXt" or "tIME" or "eXIf";
            if (!drop) ms.Write(d, i, total);
            i += total;
            if (type == "IEND") break;
        }
        return ms.ToArray();
    }

    private static byte[] StripJpeg(byte[] d)
    {
        using var ms = new MemoryStream(d.Length);
        ms.WriteByte(0xFF); ms.WriteByte(0xD8); // SOI
        int i = 2;
        while (i + 1 < d.Length)
        {
            if (d[i] != 0xFF) { ms.Write(d, i, d.Length - i); break; }
            byte marker = d[i + 1];

            if (marker == 0xD9) { ms.WriteByte(0xFF); ms.WriteByte(0xD9); break; } // EOI
            if (marker == 0xDA) { ms.Write(d, i, d.Length - i); break; }           // SOS → rest is image data
            // Standalone markers (no length): RSTn, TEM
            if ((marker >= 0xD0 && marker <= 0xD7) || marker == 0x01)
            {
                ms.WriteByte(0xFF); ms.WriteByte(marker); i += 2; continue;
            }

            if (i + 4 > d.Length) { ms.Write(d, i, d.Length - i); break; }
            int len = (d[i + 2] << 8) | d[i + 3]; // includes the 2 length bytes
            int total = 2 + len;                  // marker(2) + segment
            if (len < 2 || i + total > d.Length) { ms.Write(d, i, d.Length - i); break; }

            // Drop EXIF/XMP (APP1), Photoshop/IPTC (APP13), and comments (COM).
            bool drop = marker == 0xE1 || marker == 0xED || marker == 0xFE;
            if (!drop) ms.Write(d, i, total);
            i += total;
        }
        return ms.ToArray();
    }
}
