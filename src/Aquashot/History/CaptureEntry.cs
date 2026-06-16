using System;

namespace Aquashot.History;

public record CaptureEntry
{
    public string Path { get; init; } = "";
    public DateTime CapturedAt { get; init; }
    public string? OcrText { get; init; }
    public bool OcrDone { get; init; }
}
