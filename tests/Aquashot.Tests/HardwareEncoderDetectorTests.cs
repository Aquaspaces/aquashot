using System.Collections.Generic;
using FluentAssertions;
using Aquashot.Recording;
using Xunit;

namespace Aquashot.Tests;

public class HardwareEncoderDetectorTests
{
    private const string EncodersOutput = @"
Encoders:
 V..... = Video
 ------
 V....D h264_nvenc           NVIDIA NVENC H.264 encoder
 V....D hevc_nvenc           NVIDIA NVENC hevc encoder
 V....D h264_amf             AMD AMF H.264 encoder
 V....D libx264              libx264 H.264 / AVC
 A....D aac                  AAC (Advanced Audio Coding)
";

    [Fact]
    public void Parse_extracts_only_video_encoder_names_we_know()
    {
        var found = HardwareEncoderDetector.ParseAvailable(EncodersOutput, EncoderLadder.Default);
        found.Should().Contain("h264_nvenc");
        found.Should().Contain("hevc_nvenc");
        found.Should().Contain("h264_amf");
        found.Should().Contain("libx264");
        found.Should().NotContain("aac");
        found.Should().NotContain("av1_qsv"); // not in this output
    }

    [Fact]
    public void Pick_returns_first_available_that_probes_ok()
    {
        var available = new HashSet<string> { "hevc_nvenc", "h264_nvenc", "libx264" };
        // Simulate nvenc compiled-in but failing at runtime (no driver): only libx264 probes ok.
        bool Probe(string enc) => enc == "libx264";
        var chosen = HardwareEncoderDetector.Pick(EncoderLadder.Default, available, Probe);
        chosen!.Name.Should().Be("libx264");
    }

    [Fact]
    public void Pick_prefers_hardware_when_it_probes_ok()
    {
        var available = new HashSet<string> { "hevc_nvenc", "libx264" };
        bool Probe(string enc) => true;
        var chosen = HardwareEncoderDetector.Pick(EncoderLadder.Default, available, Probe);
        chosen!.Name.Should().Be("hevc_nvenc");
        chosen.IsHardware.Should().BeTrue();
    }

    [Fact]
    public void Pick_returns_null_when_nothing_probes()
    {
        var chosen = HardwareEncoderDetector.Pick(
            EncoderLadder.Default, new HashSet<string> { "h264_nvenc" }, _ => false);
        chosen.Should().BeNull();
    }
}
