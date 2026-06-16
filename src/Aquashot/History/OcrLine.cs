using System.Windows;

namespace Aquashot.History;

// One recognized line of text and its bounding box in SOURCE-IMAGE PIXEL coordinates.
public record OcrLine(string Text, Rect BoxPx);
