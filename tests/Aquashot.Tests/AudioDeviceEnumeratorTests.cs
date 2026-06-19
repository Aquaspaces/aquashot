using System.Linq;
using FluentAssertions;
using Aquashot.Recording;
using Xunit;

namespace Aquashot.Tests;

public class AudioDeviceEnumeratorTests
{
    // A representative slice of `ffmpeg -list_devices true -f dshow -i dummy` stderr: video and
    // audio devices, each followed by an "Alternative name" line that must be ignored.
    private const string Sample = @"
[dshow @ 000001] ""Integrated Camera"" (video)
[dshow @ 000001]   Alternative name ""@device_pnp_\\?\usb#vid_camera""
[dshow @ 000001] ""Microphone (Realtek(R) Audio)"" (audio)
[dshow @ 000001]   Alternative name ""@device_cm_{guid}\wave_microphone""
[dshow @ 000001] ""Stereo Mix (Realtek(R) Audio)"" (audio)
[dshow @ 000001]   Alternative name ""@device_cm_{guid}\wave_stereomix""
[dshow @ 000001] ""CABLE Output (VB-Audio Virtual Cable)"" (audio)
";

    [Fact]
    public void ParseAudioDevices_extracts_only_audio_quoted_names()
    {
        var devices = AudioDeviceEnumerator.ParseAudioDevices(Sample);
        devices.Select(d => d.Name).Should().BeEquivalentTo(new[]
        {
            "Microphone (Realtek(R) Audio)",
            "Stereo Mix (Realtek(R) Audio)",
            "CABLE Output (VB-Audio Virtual Cable)"
        }, o => o.WithStrictOrdering());
    }

    [Fact]
    public void ParseAudioDevices_ignores_video_and_alternative_name_lines()
    {
        var devices = AudioDeviceEnumerator.ParseAudioDevices(Sample);
        devices.Should().NotContain(d => d.Name.Contains("Camera"));
        devices.Should().NotContain(d => d.Name.Contains("@device"));
    }

    [Fact]
    public void ParseAudioDevices_flags_loopback_devices()
    {
        var devices = AudioDeviceEnumerator.ParseAudioDevices(Sample);
        devices.Single(d => d.Name.StartsWith("Microphone")).IsLoopback.Should().BeFalse();
        devices.Single(d => d.Name.StartsWith("Stereo Mix")).IsLoopback.Should().BeTrue();
        devices.Single(d => d.Name.StartsWith("CABLE Output")).IsLoopback.Should().BeTrue();
    }

    [Fact]
    public void ParseAudioDevices_handles_empty_and_dedups()
    {
        AudioDeviceEnumerator.ParseAudioDevices("").Should().BeEmpty();
        AudioDeviceEnumerator.ParseAudioDevices(null!).Should().BeEmpty();
        var dup = AudioDeviceEnumerator.ParseAudioDevices(
            "\"Mic\" (audio)\n\"Mic\" (audio)\n");
        dup.Should().ContainSingle();
    }

    [Fact]
    public void DetectSystemLoopback_returns_first_loopback_or_null()
    {
        var devices = AudioDeviceEnumerator.ParseAudioDevices(Sample);
        AudioDeviceEnumerator.DetectSystemLoopback(devices).Should().Be("Stereo Mix (Realtek(R) Audio)");

        var noLoopback = AudioDeviceEnumerator.ParseAudioDevices("\"Microphone (Realtek)\" (audio)\n");
        AudioDeviceEnumerator.DetectSystemLoopback(noLoopback).Should().BeNull();
    }
}
