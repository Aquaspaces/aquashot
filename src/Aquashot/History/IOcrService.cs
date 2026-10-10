using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aquashot.History;

public interface IOcrService
{
    // Returns recognized text, or "" if OCR is unavailable / fails. Never throws.
    Task<string> RecognizeAsync(string imagePath);

    // Returns detected lines; throws on failure so redaction never mistakes it for no text.
    Task<IReadOnlyList<OcrLine>> RecognizeLinesAsync(string imagePath);
}
