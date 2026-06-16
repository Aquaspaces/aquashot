using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Aquashot.History;

// Wraps the built-in Windows OCR engine (user-profile languages). Returns "" on any
// failure so indexing never breaks the app.
public class WindowsOcrService : IOcrService
{
    public async Task<string> RecognizeAsync(string imagePath)
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                         ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));
            if (engine == null) return "";

            byte[] bytes = await File.ReadAllBytesAsync(imagePath);
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bmp = await decoder.GetSoftwareBitmapAsync();
            var result = await engine.RecognizeAsync(bmp);
            return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text));
        }
        catch { return ""; }
    }

    public async Task<IReadOnlyList<OcrLine>> RecognizeLinesAsync(string imagePath)
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                         ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));
            if (engine == null) return System.Array.Empty<OcrLine>();

            byte[] bytes = await File.ReadAllBytesAsync(imagePath);
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bmp = await decoder.GetSoftwareBitmapAsync();
            var result = await engine.RecognizeAsync(bmp);

            var lines = new List<OcrLine>();
            foreach (var line in result.Lines)
            {
                System.Windows.Rect box = System.Windows.Rect.Empty;
                foreach (var w in line.Words)
                {
                    var r = w.BoundingRect; // Windows.Foundation.Rect
                    box.Union(new System.Windows.Rect(r.X, r.Y, r.Width, r.Height));
                }
                if (!box.IsEmpty) lines.Add(new OcrLine(line.Text, box));
            }
            return lines;
        }
        catch { return System.Array.Empty<OcrLine>(); }
    }
}
