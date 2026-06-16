using System.Threading.Tasks;

namespace Aquashot.History;

public interface IOcrService
{
    // Returns recognized text, or "" if OCR is unavailable / fails. Never throws.
    Task<string> RecognizeAsync(string imagePath);
}
