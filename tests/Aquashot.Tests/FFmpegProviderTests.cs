using System;
using System.IO;
using System.Text;
using FluentAssertions;
using Aquashot.Capture;
using Xunit;

namespace Aquashot.Tests;

public class FFmpegProviderTests
{
    private static Stream Payload() => new MemoryStream(Encoding.ASCII.GetBytes("FAKE-FFMPEG-BINARY"));

    [Fact]
    public void Extract_writes_binary_and_returns_path()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aqua-ffmpeg-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new FFmpegProvider(Payload, dir);
            var path = provider.EnsureExtracted();
            File.Exists(path).Should().BeTrue();
            File.ReadAllText(path).Should().Be("FAKE-FFMPEG-BINARY");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Extract_is_idempotent_and_reuses_cached_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aqua-ffmpeg-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var provider = new FFmpegProvider(Payload, dir);
            var p1 = provider.EnsureExtracted();
            var writeTime1 = File.GetLastWriteTimeUtc(p1);
            var p2 = new FFmpegProvider(Payload, dir).EnsureExtracted();
            p2.Should().Be(p1);
            File.GetLastWriteTimeUtc(p2).Should().Be(writeTime1); // not rewritten
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
