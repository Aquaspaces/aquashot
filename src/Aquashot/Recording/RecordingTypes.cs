using System;
using System.Collections.Generic;
using Aquashot.Selection;

namespace Aquashot.Recording;

public enum CaptureBackend { Gdigrab, Ddagrab }
public enum VideoCodec { Av1, Hevc, H264 }

[Flags]
public enum RecordFormats { None = 0, Mp4 = 1, Gif = 2, Both = Mp4 | Gif }

// One FFmpeg encoder name plus what it is, so the detector can reason about fallbacks.
public record EncoderCandidate(string Name, VideoCodec Codec, bool IsHardware);

public static class EncoderLadder
{
    // Ordered best -> worst. First that test-encodes wins.
    public static readonly IReadOnlyList<EncoderCandidate> Default = new[]
    {
        new EncoderCandidate("av1_nvenc",  VideoCodec.Av1,  true),
        new EncoderCandidate("hevc_nvenc", VideoCodec.Hevc, true),
        new EncoderCandidate("h264_nvenc", VideoCodec.H264, true),
        new EncoderCandidate("av1_qsv",    VideoCodec.Av1,  true),
        new EncoderCandidate("hevc_qsv",   VideoCodec.Hevc, true),
        new EncoderCandidate("h264_qsv",   VideoCodec.H264, true),
        new EncoderCandidate("av1_amf",    VideoCodec.Av1,  true),
        new EncoderCandidate("hevc_amf",   VideoCodec.Hevc, true),
        new EncoderCandidate("h264_amf",   VideoCodec.H264, true),
        new EncoderCandidate("hevc_mf",    VideoCodec.Hevc, true),
        new EncoderCandidate("h264_mf",    VideoCodec.H264, true),
        new EncoderCandidate("libx264",    VideoCodec.H264, false),
    };
}

// What the user asked to record.
public record RecordOptions(
    PixelRect Region,
    int Fps,
    RecordFormats Formats,
    string? EncoderOverride);

// Result of finalizing: produced files + size-cap flag.
public record RecordResult(IReadOnlyList<string> Files, bool SizeCapForced);
