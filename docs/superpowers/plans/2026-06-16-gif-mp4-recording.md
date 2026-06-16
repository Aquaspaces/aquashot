# GIF + MP4 Screen Recording Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record a selected screen region and export a hardware-encoded MP4 and/or a high-quality GIF, both kept under 50 MB, using a bundled FFmpeg.

**Architecture:** FFmpeg (embedded as a resource, extracted to temp on first run) captures the live region via `ddagrab`/`gdigrab`, recording to a temp intermediate. On stop, the intermediate is transcoded to a size-targeted MP4 (hardware encoder auto-selected from a NVENC→QSV→AMF→MF→libx264 ladder, each verified by a real test-encode) and/or a two-pass palette GIF. Pure arg-builders and size math are unit-tested; the FFmpeg process sits behind `IFFmpegRunner` so orchestration is tested with a mock.

**Tech Stack:** C# / .NET 8 (net8.0-windows), WPF + WinForms, xUnit + FluentAssertions, bundled FFmpeg (GPL build) invoked as a subprocess.

---

## File Structure

| File | Responsibility |
|------|----------------|
| `src/Aquashot/Recording/RecordingTypes.cs` | Enums + records: `CaptureBackend`, `VideoCodec`, `EncoderCandidate`, `EncoderLadder`, `RecordOptions`, `RecordResult`. |
| `src/Aquashot/Recording/FFmpegArgs.cs` | Pure arg-list builders: capture (gdigrab/ddagrab), GIF two-pass, MP4 transcode, encode-probe. |
| `src/Aquashot/Recording/SizeTargeter.cs` | Pure math: MP4 bitrate from duration/budget; GIF fps/scale plan + shrink-on-retry. |
| `src/Aquashot/Capture/IFFmpegRunner.cs` | Interface: run ffmpeg to completion; start a long-running capture with graceful stop. |
| `src/Aquashot/Capture/FFmpegRunner.cs` | `Process`-based impl. Streams stderr; stop via writing `q` to stdin. |
| `src/Aquashot/Capture/FFmpegProvider.cs` | Extract embedded ffmpeg.exe → cache dir, hash-named, reuse if present. |
| `src/Aquashot/Recording/HardwareEncoderDetector.cs` | Parse `-encoders`; pick first ladder entry that test-encodes. Cached. |
| `src/Aquashot/Recording/RecordingEncoder.cs` | Orchestrate temp → GIF/MP4 finals (uses args + size targeter + runner + detector). |
| `src/Aquashot/Recording/RecordOverlay.xaml(.cs)` | Lightweight region drag-select; returns `PixelRect`. |
| `src/Aquashot/Recording/RecordingControlBar.xaml(.cs)` | Always-on-top Record/Stop bar, timer; excluded from capture. |
| `src/Aquashot/Recording/RecordingController.cs` | select → capture → finalize → save → notify. Mirrors `OverlayController`. |
| `src/Aquashot/Output/OutputService.cs` (modify) | `SaveRecordingFile` + clipboard `CF_HDROP` file-drop. |
| `src/Aquashot/Tray/TrayHost.cs` (modify) | "Record…" menu item; rename `_capturing`→`_busy`. |
| `src/Aquashot/Settings/AppSettings.cs` (modify) | `RecordFps`, `EncoderOverride`, `RecordFormats` defaults. |
| `src/Aquashot/Aquashot.csproj` (modify) | Embed `ffmpeg.exe` as a resource. |
| `tests/Aquashot.Tests/FFmpegArgsTests.cs` | Arg-builder assertions. |
| `tests/Aquashot.Tests/SizeTargeterTests.cs` | Size math assertions. |
| `tests/Aquashot.Tests/HardwareEncoderDetectorTests.cs` | Parse + pick logic w/ fake probe. |
| `tests/Aquashot.Tests/FFmpegProviderTests.cs` | Extract/cache w/ fake payload. |
| `tests/Aquashot.Tests/RecordingEncoderTests.cs` | Orchestration w/ mock runner (arg sequence + retry). |

---

## Task 1: Recording types & encoder ladder

**Files:**
- Create: `src/Aquashot/Recording/RecordingTypes.cs`

- [ ] **Step 1: Create the types (no test — plain data)**

```csharp
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
    // Ordered best → worst. First that test-encodes wins.
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

// Result of finalizing: produced files + final sizes.
public record RecordResult(IReadOnlyList<string> Files, bool SizeCapForced);
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Aquashot/Aquashot.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Aquashot/Recording/RecordingTypes.cs
git commit -m "feat(record): recording types and encoder ladder"
```

---

## Task 2: FFmpegArgs (pure arg builders)

**Files:**
- Create: `src/Aquashot/Recording/FFmpegArgs.cs`
- Test: `tests/Aquashot.Tests/FFmpegArgsTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using FluentAssertions;
using Aquashot.Recording;
using Aquashot.Selection;
using Xunit;

namespace Aquashot.Tests;

public class FFmpegArgsTests
{
    private static string Join(IReadOnlyList<string> a) => string.Join(" ", a);

    [Fact]
    public void Gdigrab_capture_has_offset_size_fps_and_encoder()
    {
        var args = FFmpegArgs.CaptureGdigrab(new PixelRect(100, 50, 640, 480), 30, "h264_nvenc", "C:\\t\\mid.mp4");
        var s = Join(args);
        s.Should().Contain("-f gdigrab");
        s.Should().Contain("-framerate 30");
        s.Should().Contain("-offset_x 100");
        s.Should().Contain("-offset_y 50");
        s.Should().Contain("-video_size 640x480");
        s.Should().Contain("-i desktop");
        s.Should().Contain("-c:v h264_nvenc");
        args[^1].Should().Be("C:\\t\\mid.mp4");
    }

    [Fact]
    public void Ddagrab_capture_uses_lavfi_source_and_crop()
    {
        var args = FFmpegArgs.CaptureDdagrab(new PixelRect(0, 0, 800, 600), 30, "hevc_nvenc", "C:\\t\\mid.mp4");
        var s = Join(args);
        s.Should().Contain("-f lavfi");
        s.Should().Contain("ddagrab");
        s.Should().Contain("-c:v hevc_nvenc");
    }

    [Fact]
    public void GifPass1_generates_palette_with_fps_and_scale()
    {
        var args = FFmpegArgs.GifPalettegen("C:\\t\\mid.mp4", 20, 800, "C:\\t\\pal.png");
        var s = Join(args);
        s.Should().Contain("fps=20");
        s.Should().Contain("scale=800:-1:flags=lanczos");
        s.Should().Contain("palettegen");
        args[^1].Should().Be("C:\\t\\pal.png");
    }

    [Fact]
    public void GifPass2_applies_palette_with_dither()
    {
        var args = FFmpegArgs.GifPaletteuse("C:\\t\\mid.mp4", "C:\\t\\pal.png", 20, 800, "C:\\t\\out.gif");
        var s = Join(args);
        s.Should().Contain("paletteuse");
        s.Should().Contain("dither=");
        args[^1].Should().Be("C:\\t\\out.gif");
    }

    [Fact]
    public void Mp4_transcode_sets_bitrate_and_filesize_cap()
    {
        var args = FFmpegArgs.Mp4Transcode("C:\\t\\mid.mp4", "h264_nvenc", 4000, "C:\\t\\out.mp4");
        var s = Join(args);
        s.Should().Contain("-c:v h264_nvenc");
        s.Should().Contain("-b:v 4000k");
        s.Should().Contain("-fs 49M");
        s.Should().Contain("-movflags +faststart");
        args[^1].Should().Be("C:\\t\\out.mp4");
    }

    [Fact]
    public void Probe_encodes_a_few_synthetic_frames_to_null()
    {
        var args = FFmpegArgs.EncodeProbe("h264_nvenc");
        var s = Join(args);
        s.Should().Contain("lavfi");
        s.Should().Contain("-c:v h264_nvenc");
        s.Should().Contain("-f null");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~FFmpegArgsTests`
Expected: FAIL — `FFmpegArgs` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System.Collections.Generic;
using System.Globalization;
using Aquashot.Selection;

namespace Aquashot.Recording;

// Pure builders. Each returns an ArgumentList for ProcessStartInfo (no manual quoting).
public static class FFmpegArgs
{
    private static string I(double v) => ((int)System.Math.Round(v)).ToString(CultureInfo.InvariantCulture);

    public static List<string> CaptureGdigrab(PixelRect region, int fps, string encoder, string outPath) => new()
    {
        "-y", "-f", "gdigrab", "-framerate", fps.ToString(), "-draw_mouse", "1",
        "-offset_x", I(region.X), "-offset_y", I(region.Y),
        "-video_size", $"{I(region.Width)}x{I(region.Height)}", "-i", "desktop",
        "-c:v", encoder, "-pix_fmt", "yuv420p", outPath
    };

    // ddagrab is a lavfi source; crop the captured output to the region.
    public static List<string> CaptureDdagrab(PixelRect region, int fps, string encoder, string outPath) => new()
    {
        "-y", "-f", "lavfi", "-i",
        $"ddagrab=framerate={fps}:output_idx=0,crop={I(region.Width)}:{I(region.Height)}:{I(region.X)}:{I(region.Y)},hwdownload,format=bgra,format=yuv420p",
        "-c:v", encoder, "-pix_fmt", "yuv420p", outPath
    };

    public static List<string> GifPalettegen(string input, int fps, int width, string palettePath) => new()
    {
        "-y", "-i", input,
        "-vf", $"fps={fps},scale={width}:-1:flags=lanczos,palettegen=stats_mode=diff",
        palettePath
    };

    public static List<string> GifPaletteuse(string input, string palettePath, int fps, int width, string outPath) => new()
    {
        "-y", "-i", input, "-i", palettePath,
        "-lavfi", $"fps={fps},scale={width}:-1:flags=lanczos[x];[x][1:v]paletteuse=dither=sierra2_4a",
        outPath
    };

    public static List<string> Mp4Transcode(string input, string encoder, int bitrateKbps, string outPath) => new()
    {
        "-y", "-i", input, "-c:v", encoder,
        "-b:v", $"{bitrateKbps}k", "-maxrate", $"{bitrateKbps * 3 / 2}k", "-bufsize", $"{bitrateKbps * 2}k",
        "-fs", "49M", "-pix_fmt", "yuv420p", "-movflags", "+faststart", outPath
    };

    public static List<string> EncodeProbe(string encoder) => new()
    {
        "-hide_banner", "-f", "lavfi", "-i", "color=c=black:s=128x128:d=0.2",
        "-frames:v", "5", "-c:v", encoder, "-f", "null", "-"
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~FFmpegArgsTests`
Expected: PASS (all 6).

- [ ] **Step 5: Commit**

```bash
git add src/Aquashot/Recording/FFmpegArgs.cs tests/Aquashot.Tests/FFmpegArgsTests.cs
git commit -m "feat(record): pure ffmpeg arg builders with tests"
```

---

## Task 3: SizeTargeter (pure size math)

**Files:**
- Create: `src/Aquashot/Recording/SizeTargeter.cs`
- Test: `tests/Aquashot.Tests/SizeTargeterTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using FluentAssertions;
using Aquashot.Recording;
using Xunit;

namespace Aquashot.Tests;

public class SizeTargeterTests
{
    private const long Budget = 50L * 1024 * 1024;

    [Fact]
    public void Bitrate_scales_inversely_with_duration()
    {
        int tenSec = SizeTargeter.BitrateKbps(TimeSpan.FromSeconds(10), Budget);
        int twentySec = SizeTargeter.BitrateKbps(TimeSpan.FromSeconds(20), Budget);
        twentySec.Should().BeLessThan(tenSec);
        tenSec.Should().BeGreaterThan(200);
    }

    [Fact]
    public void Bitrate_has_a_floor_for_long_recordings()
    {
        SizeTargeter.BitrateKbps(TimeSpan.FromHours(2), Budget).Should().BeGreaterOrEqualTo(200);
    }

    [Fact]
    public void Bitrate_clamps_tiny_durations()
    {
        SizeTargeter.BitrateKbps(TimeSpan.Zero, Budget).Should().BeGreaterThan(0);
    }

    [Fact]
    public void GifPlan_caps_fps_and_width()
    {
        var (fps, width) = SizeTargeter.GifPlan(1920, requestedFps: 60);
        fps.Should().BeLessOrEqualTo(20);
        width.Should().BeLessOrEqualTo(800);
    }

    [Fact]
    public void GifPlan_keeps_small_sources_unchanged()
    {
        var (fps, width) = SizeTargeter.GifPlan(400, requestedFps: 12);
        fps.Should().Be(12);
        width.Should().Be(400);
    }

    [Fact]
    public void Shrink_halves_and_floors()
    {
        var (fps, width) = SizeTargeter.Shrink((20, 800));
        fps.Should().Be(10);
        width.Should().Be(400);
        var (fps2, width2) = SizeTargeter.Shrink((8, 240));
        fps2.Should().BeGreaterOrEqualTo(8);
        width2.Should().BeGreaterOrEqualTo(240);
    }

    [Fact]
    public void Over_budget_detected()
    {
        SizeTargeter.OverBudget(Budget + 1, Budget).Should().BeTrue();
        SizeTargeter.OverBudget(Budget - 1, Budget).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~SizeTargeterTests`
Expected: FAIL — `SizeTargeter` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System;

namespace Aquashot.Recording;

public static class SizeTargeter
{
    public const long DefaultBudgetBytes = 50L * 1024 * 1024;
    private const int MaxGifFps = 20;
    private const int MaxGifWidth = 800;
    private const int MinGifFps = 8;
    private const int MinGifWidth = 240;

    // Bits budget / seconds, with 8% headroom for container overhead, floored at 200 kbps.
    public static int BitrateKbps(TimeSpan duration, long budgetBytes)
    {
        double sec = Math.Max(0.5, duration.TotalSeconds);
        double bits = budgetBytes * 8 * 0.92;
        return Math.Max(200, (int)(bits / 1000.0 / sec));
    }

    public static (int fps, int width) GifPlan(int sourceWidth, int requestedFps) =>
        (Math.Min(MaxGifFps, requestedFps), Math.Min(MaxGifWidth, sourceWidth));

    public static (int fps, int width) Shrink((int fps, int width) p) =>
        (Math.Max(MinGifFps, p.fps / 2), Math.Max(MinGifWidth, p.width / 2));

    public static bool OverBudget(long actualBytes, long budgetBytes) => actualBytes > budgetBytes;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~SizeTargeterTests`
Expected: PASS (all 7).

- [ ] **Step 5: Commit**

```bash
git add src/Aquashot/Recording/SizeTargeter.cs tests/Aquashot.Tests/SizeTargeterTests.cs
git commit -m "feat(record): file-size targeting math with tests"
```

---

## Task 4: IFFmpegRunner interface + result type

**Files:**
- Create: `src/Aquashot/Capture/IFFmpegRunner.cs`

- [ ] **Step 1: Create the interface (no test — contract only)**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Aquashot.Capture;

public record FFmpegResult(int ExitCode, string StderrTail)
{
    public bool Ok => ExitCode == 0;
}

// A live recording: stop it (graceful) and await the final result.
public interface IFFmpegSession
{
    Task<FFmpegResult> StopAsync();
}

public interface IFFmpegRunner
{
    // Run to completion (probe, transcode, palette passes). Blocks until exit.
    Task<FFmpegResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default);

    // Start a long-running capture; returns a session you stop later.
    IFFmpegSession StartCapture(IReadOnlyList<string> args);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Aquashot/Aquashot.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Aquashot/Capture/IFFmpegRunner.cs
git commit -m "feat(record): IFFmpegRunner contract"
```

---

## Task 5: HardwareEncoderDetector (parse + pick)

**Files:**
- Create: `src/Aquashot/Recording/HardwareEncoderDetector.cs`
- Test: `tests/Aquashot.Tests/HardwareEncoderDetectorTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~HardwareEncoderDetectorTests`
Expected: FAIL — `HardwareEncoderDetector` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aquashot.Capture;

namespace Aquashot.Recording;

public class HardwareEncoderDetector
{
    private readonly IFFmpegRunner _runner;
    private EncoderCandidate? _cached;

    public HardwareEncoderDetector(IFFmpegRunner runner) => _runner = runner;

    // Pure: which ladder names appear in `ffmpeg -encoders` output.
    public static HashSet<string> ParseAvailable(string encodersOutput, IReadOnlyList<EncoderCandidate> ladder)
    {
        var known = new HashSet<string>();
        foreach (var c in ladder) known.Add(c.Name);
        var found = new HashSet<string>();
        foreach (var raw in encodersOutput.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || !line.StartsWith("V")) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && known.Contains(parts[1])) found.Add(parts[1]);
        }
        return found;
    }

    // Pure: first ladder entry that is available AND probes ok.
    public static EncoderCandidate? Pick(
        IReadOnlyList<EncoderCandidate> ladder, ISet<string> available, Func<string, bool> probe)
    {
        foreach (var c in ladder)
            if (available.Contains(c.Name) && probe(c.Name)) return c;
        return null;
    }

    // Live: run `-encoders`, then probe candidates with a real 5-frame test encode. Cached.
    public async Task<EncoderCandidate?> DetectAsync(string? overrideName)
    {
        if (_cached != null) return _cached;

        if (!string.IsNullOrWhiteSpace(overrideName))
        {
            if ((await _runner.RunAsync(FFmpegArgs.EncodeProbe(overrideName!))).Ok)
                return _cached = new EncoderCandidate(overrideName!, VideoCodec.H264, true);
        }

        var enc = await _runner.RunAsync(new[] { "-hide_banner", "-encoders" });
        var available = ParseAvailable(enc.StderrTail, EncoderLadder.Default);
        // ffmpeg writes -encoders to stdout normally; FFmpegRunner captures both into StderrTail.

        var probed = new Dictionary<string, bool>();
        bool Probe(string name)
        {
            if (probed.TryGetValue(name, out var ok)) return ok;
            ok = _runner.RunAsync(FFmpegArgs.EncodeProbe(name)).GetAwaiter().GetResult().Ok;
            probed[name] = ok;
            return ok;
        }
        return _cached = Pick(EncoderLadder.Default, available, Probe);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~HardwareEncoderDetectorTests`
Expected: PASS (all 4).

- [ ] **Step 5: Commit**

```bash
git add src/Aquashot/Recording/HardwareEncoderDetector.cs tests/Aquashot.Tests/HardwareEncoderDetectorTests.cs
git commit -m "feat(record): encoder detection with real test-encode probe"
```

---

## Task 6: FFmpegProvider (extract embedded binary)

**Files:**
- Create: `src/Aquashot/Capture/FFmpegProvider.cs`
- Test: `tests/Aquashot.Tests/FFmpegProviderTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~FFmpegProviderTests`
Expected: FAIL — `FFmpegProvider` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace Aquashot.Capture;

// Extracts the bundled ffmpeg.exe to a cache dir, named by content hash so a new
// build's binary lands at a new path and stale ones are simply ignored.
public class FFmpegProvider
{
    private const string ResourceName = "Aquashot.Resources.ffmpeg.exe";
    private readonly Func<Stream> _open;
    private readonly string _cacheDir;

    public FFmpegProvider(Func<Stream> open, string cacheDir)
    {
        _open = open;
        _cacheDir = cacheDir;
    }

    // Production ctor: read from the embedded resource, cache under %LOCALAPPDATA%.
    public static FFmpegProvider Default()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Aquashot", "ffmpeg");
        return new FFmpegProvider(
            () => Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                  ?? throw new InvalidOperationException("Embedded ffmpeg.exe not found: " + ResourceName),
            dir);
    }

    public string EnsureExtracted()
    {
        Directory.CreateDirectory(_cacheDir);
        byte[] bytes;
        using (var s = _open()) { using var ms = new MemoryStream(); s.CopyTo(ms); bytes = ms.ToArray(); }

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).Substring(0, 12).ToLowerInvariant();
        var path = Path.Combine(_cacheDir, $"ffmpeg-{hash}.exe");
        if (File.Exists(path) && new FileInfo(path).Length == bytes.Length) return path;
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~FFmpegProviderTests`
Expected: PASS (both).

- [ ] **Step 5: Commit**

```bash
git add src/Aquashot/Capture/FFmpegProvider.cs tests/Aquashot.Tests/FFmpegProviderTests.cs
git commit -m "feat(record): embedded ffmpeg extraction with content-hash cache"
```

---

## Task 7: FFmpegRunner (process impl)

**Files:**
- Create: `src/Aquashot/Capture/FFmpegRunner.cs`

This wraps `Process`. It is not unit-tested (needs a real binary + no display in CI); it is exercised by manual verification. Keep it small and obviously correct.

- [ ] **Step 1: Write the implementation**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Aquashot.Capture;

public class FFmpegRunner : IFFmpegRunner
{
    private readonly string _exePath;
    private const int StderrTailChars = 4000;

    public FFmpegRunner(string exePath) => _exePath = exePath;

    private ProcessStartInfo Psi(IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _exePath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return psi;
    }

    public async Task<FFmpegResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        using var p = new Process { StartInfo = Psi(args) };
        var sb = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Append(sb, e.Data); };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Append(sb, e.Data); };
        p.Start();
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        await p.WaitForExitAsync(ct);
        return new FFmpegResult(p.ExitCode, sb.ToString());
    }

    public IFFmpegSession StartCapture(IReadOnlyList<string> args)
    {
        var p = new Process { StartInfo = Psi(args) };
        var sb = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Append(sb, e.Data); };
        p.Start();
        p.BeginErrorReadLine();
        return new Session(p, sb);
    }

    private static void Append(StringBuilder sb, string line)
    {
        sb.Append(line).Append('\n');
        if (sb.Length > StderrTailChars * 2) sb.Remove(0, sb.Length - StderrTailChars);
    }

    private sealed class Session : IFFmpegSession
    {
        private readonly Process _p;
        private readonly StringBuilder _sb;
        public Session(Process p, StringBuilder sb) { _p = p; _sb = sb; }

        public async Task<FFmpegResult> StopAsync()
        {
            try
            {
                // Graceful: 'q' tells ffmpeg to finalize the file (write moov atom, etc.).
                await _p.StandardInput.WriteLineAsync("q");
                await _p.StandardInput.FlushAsync();
            }
            catch { /* stdin may be closed if ffmpeg already exited */ }

            if (!_p.WaitForExit(5000)) { try { _p.Kill(true); } catch { } _p.WaitForExit(2000); }
            var code = _p.HasExited ? _p.ExitCode : -1;
            _p.Dispose();
            return new FFmpegResult(code, _sb.ToString());
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Aquashot/Aquashot.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Aquashot/Capture/FFmpegRunner.cs
git commit -m "feat(record): Process-based FFmpegRunner with graceful stop"
```

---

## Task 8: RecordingEncoder (orchestrate finals)

**Files:**
- Create: `src/Aquashot/Recording/RecordingEncoder.cs`
- Test: `tests/Aquashot.Tests/RecordingEncoderTests.cs`

This is the heart: given a temp intermediate + chosen encoder, produce the requested finals with size targeting and one GIF shrink-retry. Tested with a mock runner that records the arg sequences and reports file sizes via an injectable size probe.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Aquashot.Capture;
using Aquashot.Recording;
using Xunit;

namespace Aquashot.Tests;

public class RecordingEncoderTests
{
    private sealed class FakeRunner : IFFmpegRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = new();
        public Func<IReadOnlyList<string>, int> ExitFor = _ => 0;
        public Task<FFmpegResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
        { Calls.Add(args); return Task.FromResult(new FFmpegResult(ExitFor(args), "")); }
        public IFFmpegSession StartCapture(IReadOnlyList<string> args) => throw new NotImplementedException();
    }

    private static bool Has(IReadOnlyList<string> a, string token) => a.Contains(token);

    [Fact]
    public async Task Mp4_only_runs_a_single_transcode()
    {
        var runner = new FakeRunner();
        var enc = new RecordingEncoder(runner, sizeOf: _ => 1_000_000);
        var files = await enc.ProduceAsync("mid.mp4", "h264_nvenc", RecordFormats.Mp4,
            TimeSpan.FromSeconds(10), sourceWidth: 1280, fps: 30, outBase: "out");
        runner.Calls.Should().HaveCount(1);
        runner.Calls[0].Should().Contain("-c:v");
        files.Files.Should().ContainSingle(f => f.EndsWith(".mp4"));
    }

    [Fact]
    public async Task Gif_runs_two_passes_palettegen_then_paletteuse()
    {
        var runner = new FakeRunner();
        var enc = new RecordingEncoder(runner, sizeOf: _ => 1_000_000);
        await enc.ProduceAsync("mid.mp4", "h264_nvenc", RecordFormats.Gif,
            TimeSpan.FromSeconds(5), sourceWidth: 1280, fps: 20, outBase: "out");
        runner.Calls.Should().HaveCount(2);
        Has(runner.Calls[0], "-y").Should().BeTrue();
        runner.Calls[0].Any(a => a.Contains("palettegen")).Should().BeTrue();
        runner.Calls[1].Any(a => a.Contains("paletteuse")).Should().BeTrue();
    }

    [Fact]
    public async Task Gif_over_budget_triggers_one_shrink_retry()
    {
        var runner = new FakeRunner();
        // First gif output is too big; after that, small.
        int gifCount = 0;
        long SizeOf(string path)
        {
            if (!path.EndsWith(".gif")) return 1_000_000;
            return (++gifCount == 1) ? 60L * 1024 * 1024 : 10L * 1024 * 1024;
        }
        var enc = new RecordingEncoder(runner, sizeOf: SizeOf);
        var res = await enc.ProduceAsync("mid.mp4", "h264_nvenc", RecordFormats.Gif,
            TimeSpan.FromSeconds(5), sourceWidth: 1280, fps: 20, outBase: "out");
        // 2 passes + 2 passes on retry = 4 calls
        runner.Calls.Should().HaveCount(4);
        res.SizeCapForced.Should().BeTrue();
    }

    [Fact]
    public async Task Both_formats_produce_two_files()
    {
        var runner = new FakeRunner();
        var enc = new RecordingEncoder(runner, sizeOf: _ => 1_000_000);
        var res = await enc.ProduceAsync("mid.mp4", "h264_nvenc", RecordFormats.Both,
            TimeSpan.FromSeconds(8), sourceWidth: 1280, fps: 30, outBase: "out");
        res.Files.Should().Contain(f => f.EndsWith(".mp4"));
        res.Files.Should().Contain(f => f.EndsWith(".gif"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~RecordingEncoderTests`
Expected: FAIL — `RecordingEncoder` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Aquashot.Capture;

namespace Aquashot.Recording;

// Turns a recorded temp intermediate into the requested final files, enforcing the
// size budget (MP4 via bitrate; GIF via fps/scale with one shrink-retry).
public class RecordingEncoder
{
    private readonly IFFmpegRunner _runner;
    private readonly Func<string, long> _sizeOf;
    private readonly long _budget;

    public RecordingEncoder(IFFmpegRunner runner, Func<string, long>? sizeOf = null,
        long budgetBytes = SizeTargeter.DefaultBudgetBytes)
    {
        _runner = runner;
        _sizeOf = sizeOf ?? (p => new FileInfo(p).Length);
        _budget = budgetBytes;
    }

    public async Task<RecordResult> ProduceAsync(string intermediate, string encoder,
        RecordFormats formats, TimeSpan duration, int sourceWidth, int fps, string outBase)
    {
        var files = new List<string>();
        bool capForced = false;

        if (formats.HasFlag(RecordFormats.Mp4))
        {
            var mp4 = outBase + ".mp4";
            int kbps = SizeTargeter.BitrateKbps(duration, _budget);
            await _runner.RunAsync(FFmpegArgs.Mp4Transcode(intermediate, encoder, kbps, mp4));
            files.Add(mp4);
        }

        if (formats.HasFlag(RecordFormats.Gif))
        {
            var gif = outBase + ".gif";
            var plan = SizeTargeter.GifPlan(sourceWidth, fps);
            await RenderGif(intermediate, plan, gif);
            if (SizeTargeter.OverBudget(_sizeOf(gif), _budget))
            {
                capForced = true;
                await RenderGif(intermediate, SizeTargeter.Shrink(plan), gif);
            }
            files.Add(gif);
        }

        return new RecordResult(files, capForced);
    }

    private async Task RenderGif(string input, (int fps, int width) plan, string outGif)
    {
        var palette = Path.Combine(Path.GetTempPath(), "aqua-pal-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            await _runner.RunAsync(FFmpegArgs.GifPalettegen(input, plan.fps, plan.width, palette));
            await _runner.RunAsync(FFmpegArgs.GifPaletteuse(input, palette, plan.fps, plan.width, outGif));
        }
        finally { try { if (File.Exists(palette)) File.Delete(palette); } catch { } }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~RecordingEncoderTests`
Expected: PASS (all 4).

- [ ] **Step 5: Commit**

```bash
git add src/Aquashot/Recording/RecordingEncoder.cs tests/Aquashot.Tests/RecordingEncoderTests.cs
git commit -m "feat(record): finalize temp into size-targeted MP4/GIF"
```

---

## Task 9: Output — save recording file + clipboard file-drop

**Files:**
- Modify: `src/Aquashot/Output/OutputService.cs`
- Test: `tests/Aquashot.Tests/OutputServiceTests.cs` (add cases)

- [ ] **Step 1: Write the failing test (append to OutputServiceTests)**

```csharp
    [Fact]
    public void RecordingOutputPath_uses_pattern_and_extension()
    {
        var settings = new Aquashot.Settings.AppSettings { FilenamePattern = "Clip_{yyyy}" };
        var path = OutputService.RecordingOutputBase(settings, new System.DateTime(2026, 1, 2));
        path.Should().EndWith("Clip_2026");
        System.IO.Path.IsPathRooted(path).Should().BeTrue();
    }
```

(Keep the existing `using FluentAssertions;` and `using Xunit;` at the top of the file.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~OutputServiceTests.RecordingOutputPath_uses_pattern_and_extension`
Expected: FAIL — `RecordingOutputBase` does not exist.

- [ ] **Step 3: Add implementation to OutputService**

Add these members to `OutputService` (keep existing code). Add `using System.Runtime.InteropServices;` and `using System.Collections.Specialized;` at the top.

```csharp
    // Folder + filename stem (no extension) for a recording, e.g. ...\Screenshots\Clip_2026
    public static string RecordingOutputBase(AppSettings settings, DateTime now)
    {
        Directory.CreateDirectory(settings.SaveFolder);
        // Reuse the screenshot stem generator but strip any image extension it appends.
        var name = FilenameGenerator.Generate(settings.FilenamePattern, "tmp", now);
        if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return Path.Combine(settings.SaveFolder, name);
    }

    // Put the produced file on the clipboard as a file-drop (paste into Discord/Explorer).
    public static void CopyFileToClipboard(string path)
    {
        var paths = new System.Collections.Specialized.StringCollection { path };
        for (int attempt = 0; ; attempt++)
        {
            try { Clipboard.SetFileDropList(paths); return; }
            catch (ExternalException) when (attempt < 8) { Thread.Sleep(60); }
        }
    }
```

If `FilenameGenerator.Generate` does not accept an arbitrary extension cleanly, instead verify its signature in `src/Aquashot/Output/FilenameGenerator.cs` and adapt: the goal is a rooted base path with no extension. Read that file first.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~OutputServiceTests`
Expected: PASS (existing + new).

- [ ] **Step 5: Commit**

```bash
git add src/Aquashot/Output/OutputService.cs tests/Aquashot.Tests/OutputServiceTests.cs
git commit -m "feat(record): recording output path + clipboard file-drop"
```

---

## Task 10: AppSettings — recording defaults

**Files:**
- Modify: `src/Aquashot/Settings/AppSettings.cs`

- [ ] **Step 1: Add recording fields**

```csharp
    public int RecordFps { get; init; } = 30;
    public string EncoderOverride { get; init; } = "Auto"; // "Auto" or an ffmpeg encoder name
    public string RecordFormats { get; init; } = "Both";   // "Mp4" | "Gif" | "Both"
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Aquashot/Aquashot.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Aquashot/Settings/AppSettings.cs
git commit -m "feat(record): recording settings defaults"
```

---

## Task 11: RecordOverlay — region selection

**Files:**
- Create: `src/Aquashot/Recording/RecordOverlay.xaml`
- Create: `src/Aquashot/Recording/RecordOverlay.xaml.cs`

This is a trimmed clone of the selecting phase of `OverlayWindow` (see `src/Aquashot/Overlay/OverlayWindow.xaml.cs:119-178` and `:105-110`). It freezes a frame for the visual backdrop, lets the user drag a rectangle, and raises `RegionSelected(PixelRect)` (virtual-desktop pixels). No annotation phase.

- [ ] **Step 1: Create the XAML**

```xml
<Window x:Class="Aquashot.Recording.RecordOverlay"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True" Background="#01000000"
        ShowInTaskbar="False" Topmost="True" ResizeMode="NoResize"
        Cursor="Cross"
        MouseLeftButtonDown="OnMouseDown" MouseMove="OnMouseMove"
        MouseLeftButtonUp="OnMouseUp" KeyDown="OnKeyDown">
    <Canvas x:Name="Overlay">
        <Image x:Name="FrozenImage" Stretch="Fill"/>
        <Rectangle x:Name="Dim" Fill="#66000000"/>
        <Rectangle x:Name="SelRect" Stroke="#4DA3FF" StrokeThickness="1.5"
                   StrokeDashArray="4 3" Visibility="Collapsed"/>
    </Canvas>
</Window>
```

- [ ] **Step 2: Create the code-behind**

```csharp
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Aquashot.Capture;
using Aquashot.Selection;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Aquashot.Recording;

public partial class RecordOverlay : Window
{
    private readonly CapturedFrame _frame;
    private readonly double _sc;
    private Point _start;
    private bool _dragging;

    public event Action<PixelRect>? RegionSelected;
    public event Action? Cancelled;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;

    public RecordOverlay(CapturedFrame frame)
    {
        InitializeComponent();
        _frame = frame;
        _sc = frame.Monitor.DpiScale;
        FrozenImage.Source = frame.Bitmap;
        var b = frame.Monitor.Bounds;
        Width = b.Width / _sc; Height = b.Height / _sc;
        Dim.Width = Width; Dim.Height = Height;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var b = _frame.Monitor.Bounds;
        SetWindowPos(hwnd, HWND_TOPMOST, (int)b.X, (int)b.Y, (int)b.Width, (int)b.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
        Activate(); Focus();
    }

    private PixelRect ToVirtualRect(Point a, Point b)
    {
        var n = SelectionEngine.Normalize(a.X * _sc, a.Y * _sc, b.X * _sc, b.Y * _sc);
        return new PixelRect(n.X + _frame.Monitor.Bounds.X, n.Y + _frame.Monitor.Bounds.Y, n.Width, n.Height);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(Overlay);
        _dragging = true;
        SelRect.Visibility = Visibility.Visible;
        Overlay.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(Overlay);
        Canvas.SetLeft(SelRect, Math.Min(_start.X, p.X));
        Canvas.SetTop(SelRect, Math.Min(_start.Y, p.Y));
        SelRect.Width = Math.Abs(p.X - _start.X);
        SelRect.Height = Math.Abs(p.Y - _start.Y);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Overlay.ReleaseMouseCapture();
        var rect = ToVirtualRect(_start, e.GetPosition(Overlay));
        if (rect.Width < 10 || rect.Height < 10) { Cancelled?.Invoke(); return; }
        RegionSelected?.Invoke(rect);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancelled?.Invoke();
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Aquashot/Aquashot.csproj`
Expected: build succeeds. (Confirm `SelectionEngine.Normalize` signature matches `src/Aquashot/Selection/SelectionEngine.cs`; adapt the call if its return shape differs.)

- [ ] **Step 4: Commit**

```bash
git add src/Aquashot/Recording/RecordOverlay.xaml src/Aquashot/Recording/RecordOverlay.xaml.cs
git commit -m "feat(record): region-selection overlay"
```

---

## Task 12: RecordingControlBar — Record/Stop bar

**Files:**
- Create: `src/Aquashot/Recording/RecordingControlBar.xaml`
- Create: `src/Aquashot/Recording/RecordingControlBar.xaml.cs`

A small always-on-top window placed just outside the selected region. Buttons: Record/Stop, elapsed timer, Cancel. Critically, it calls `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` so it never appears in the recording.

- [ ] **Step 1: Create the XAML**

```xml
<Window x:Class="Aquashot.Recording.RecordingControlBar"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        ShowInTaskbar="False" Topmost="True" ResizeMode="NoResize" SizeToContent="WidthAndHeight">
    <Border Background="#FF202225" CornerRadius="8" Padding="8" BorderBrush="#FF3A3D42" BorderThickness="1">
        <StackPanel Orientation="Horizontal">
            <Button x:Name="RecordBtn" Content="● Record" Click="OnRecordClick"
                    Foreground="White" Background="#FFE03B3B" Padding="10,4" BorderThickness="0"/>
            <TextBlock x:Name="Timer" Text="00:00" Foreground="White" Margin="10,0"
                       VerticalAlignment="Center" FontFamily="Consolas"/>
            <Button x:Name="CancelBtn" Content="✕" Click="OnCancelClick"
                    Foreground="White" Background="#FF3A3D42" Padding="8,4" BorderThickness="0"/>
        </StackPanel>
    </Border>
</Window>
```

- [ ] **Step 2: Create the code-behind**

```csharp
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Aquashot.Recording;

public partial class RecordingControlBar : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _startedUtc;
    private bool _recording;

    public event Action? RecordStarted;
    public event Action? Stopped;
    public event Action? Cancelled;

    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11; // hides window from screen capture (Win10 2004+)

    public RecordingControlBar()
    {
        InitializeComponent();
        _timer.Tick += (_, __) =>
        {
            var t = DateTime.UtcNow - _startedUtc;
            Timer.Text = $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
    }

    // Place the bar just above the region (virtual px → DIP via the region monitor's scale).
    public void PlaceAbove(double leftDip, double topDip)
    {
        Left = leftDip;
        Top = Math.Max(0, topDip - 48);
    }

    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        if (_recording) { Stopped?.Invoke(); return; }
        _recording = true;
        _startedUtc = DateTime.UtcNow;
        _timer.Start();
        RecordBtn.Content = "■ Stop";
        RecordStarted?.Invoke();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Cancelled?.Invoke();
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Aquashot/Aquashot.csproj`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/Aquashot/Recording/RecordingControlBar.xaml src/Aquashot/Recording/RecordingControlBar.xaml.cs
git commit -m "feat(record): capture-excluded control bar with timer"
```

---

## Task 13: RecordingController — orchestration

**Files:**
- Create: `src/Aquashot/Recording/RecordingController.cs`

Ties it together: freeze a frame for the picker, show `RecordOverlay`, on region selected close the overlay and show `RecordingControlBar`, on Record start the capture session, on Stop finalize → detector picks encoder → `RecordingEncoder.ProduceAsync` → report files.

- [ ] **Step 1: Write the implementation**

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aquashot.Capture;
using Aquashot.Selection;
using Aquashot.Settings;

namespace Aquashot.Recording;

public class RecordingController
{
    private readonly ICaptureService _capture;
    private readonly IFFmpegRunner _runner;
    private readonly HardwareEncoderDetector _detector;
    private readonly AppSettings _settings;

    private RecordOverlay? _overlay;
    private RecordingControlBar? _bar;
    private IFFmpegSession? _session;
    private string _intermediate = "";
    private PixelRect _region;
    private DateTime _startedUtc;

    // files, sizeCapForced, error(null on success)
    public event Action<RecordResult?, string?>? Finished;

    public RecordingController(ICaptureService capture, IFFmpegRunner runner,
        HardwareEncoderDetector detector, AppSettings settings)
    {
        _capture = capture; _runner = runner; _detector = detector; _settings = settings;
    }

    private static RecordFormats ParseFormats(string s) => s switch
    {
        "Mp4" => RecordFormats.Mp4,
        "Gif" => RecordFormats.Gif,
        _ => RecordFormats.Both,
    };

    public void Start()
    {
        // Region picker is shown on the monitor under the cursor's frame.
        var frame = _capture.FreezeAll().First();
        _overlay = new RecordOverlay(frame);
        _overlay.Cancelled += () => { _overlay?.Close(); Finished?.Invoke(null, null); };
        _overlay.RegionSelected += region =>
        {
            _overlay?.Close();
            _region = region;
            ShowBar(frame.Monitor.DpiScale, region);
        };
        _overlay.Show();
    }

    private void ShowBar(double scale, PixelRect region)
    {
        _bar = new RecordingControlBar();
        _bar.PlaceAbove(region.X / scale, region.Y / scale);
        _bar.Cancelled += () => { _bar?.Close(); Finished?.Invoke(null, null); };
        _bar.RecordStarted += OnRecordStarted;
        _bar.Stopped += () => _ = OnStoppedAsync();
        _bar.Show();
    }

    private async void OnRecordStarted()
    {
        var encoder = (await _detector.DetectAsync(
            _settings.EncoderOverride == "Auto" ? null : _settings.EncoderOverride))?.Name ?? "libx264";
        _intermediate = Path.Combine(Path.GetTempPath(), "aqua-rec-" + Guid.NewGuid().ToString("N") + ".mp4");
        _startedUtc = DateTime.UtcNow;
        // gdigrab is the universal path; ddagrab selection is a future enhancement (see design).
        var args = FFmpegArgs.CaptureGdigrab(_region, _settings.RecordFps, encoder, _intermediate);
        _session = _runner.StartCapture(args);
        _encoderName = encoder;
    }

    private string _encoderName = "libx264";

    private async Task OnStoppedAsync()
    {
        try
        {
            var duration = DateTime.UtcNow - _startedUtc;
            var capResult = _session == null ? null : await _session.StopAsync();
            _bar?.Close();
            if (capResult is { Ok: false })
            { Finished?.Invoke(null, "Capture failed: " + Tail(capResult.StderrTail)); Cleanup(); return; }

            var encoder = new RecordingEncoder(_runner);
            var outBase = OutputService.RecordingOutputBase(_settings, DateTime.Now);
            var result = await encoder.ProduceAsync(_intermediate, _encoderName,
                ParseFormats(_settings.RecordFormats), duration,
                (int)_region.Width, _settings.RecordFps, outBase);

            foreach (var f in result.Files) OutputService.CopyFileToClipboard(f); // last wins on clipboard
            Finished?.Invoke(result, null);
        }
        catch (Exception ex) { Finished?.Invoke(null, ex.Message); }
        finally { Cleanup(); }
    }

    private void Cleanup()
    {
        try { if (File.Exists(_intermediate)) File.Delete(_intermediate); } catch { }
        _session = null;
    }

    private static string Tail(string s) => s.Length <= 300 ? s : s[^300..];
}
```

Add `using Aquashot.Output;` to the top (for `OutputService`).

- [ ] **Step 2: Build**

Run: `dotnet build src/Aquashot/Aquashot.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Aquashot/Recording/RecordingController.cs
git commit -m "feat(record): recording controller wiring select→capture→finalize"
```

---

## Task 14: Tray wiring + `_busy` rename

**Files:**
- Modify: `src/Aquashot/Tray/TrayHost.cs`

- [ ] **Step 1: Rename `_capturing` → `_busy`**

Replace every `_capturing` with `_busy` in `TrayHost.cs` (field declaration line 26, and all reads/writes in `StartCapture` and `CaptureAllMonitors`).

- [ ] **Step 2: Add the runner/detector fields + menu item**

Add fields near the existing service fields (after line 24 `private readonly OutputService _output = new();`):

```csharp
    private readonly Aquashot.Capture.FFmpegRunner _ffmpeg =
        new(Aquashot.Capture.FFmpegProvider.Default().EnsureExtracted());
    private Aquashot.Recording.HardwareEncoderDetector? _encoderDetector;
```

Add a menu item after "Capture all monitors" (after line 42):

```csharp
        menu.Items.Add("Record…", null, (_, __) => StartRecording());
```

Add the method (near `CaptureAllMonitors`):

```csharp
    private void StartRecording()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            _encoderDetector ??= new Aquashot.Recording.HardwareEncoderDetector(_ffmpeg);
            var ctrl = new Aquashot.Recording.RecordingController(_capture, _ffmpeg, _encoderDetector, _settings);
            ctrl.Finished += (result, error) =>
            {
                _busy = false;
                if (error != null)
                    _icon.ShowBalloonTip(3000, "Aquashot", "Record failed: " + error, ToolTipIcon.Error);
                else if (result != null)
                {
                    var note = result.SizeCapForced ? " (reduced to fit 50 MB)" : "";
                    _icon.ShowBalloonTip(2500, "Aquashot",
                        "Saved & copied: " + string.Join(", ", result.Files.Select(Path.GetFileName)) + note,
                        ToolTipIcon.Info);
                }
            };
            ctrl.Start();
        }
        catch (Exception ex)
        {
            _busy = false;
            _icon.ShowBalloonTip(3000, "Aquashot", "Record failed: " + ex.Message, ToolTipIcon.Error);
        }
    }
```

Add `using System.IO;` (for `Path`) if not present. `System.Linq` is already imported.

- [ ] **Step 3: Build**

Run: `dotnet build src/Aquashot/Aquashot.csproj`
Expected: build succeeds.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test`
Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Aquashot/Tray/TrayHost.cs
git commit -m "feat(record): tray Record menu item; rename capture lock to _busy"
```

---

## Task 15: Embed ffmpeg.exe + build/packaging notes

**Files:**
- Modify: `src/Aquashot/Aquashot.csproj`
- Create: `src/Aquashot/Resources/README-ffmpeg.md`
- Create: `docs/ffmpeg-bundle.md`

- [ ] **Step 1: Add the embedded resource to the csproj**

Add inside a new `<ItemGroup>` in `src/Aquashot/Aquashot.csproj`:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Resources\ffmpeg.exe">
      <LogicalName>Aquashot.Resources.ffmpeg.exe</LogicalName>
    </EmbeddedResource>
  </ItemGroup>
```

The `LogicalName` must match `FFmpegProvider.ResourceName` exactly.

- [ ] **Step 2: Document how to obtain the binary**

Create `src/Aquashot/Resources/README-ffmpeg.md`:

```markdown
# Bundled ffmpeg.exe

Place a Windows `ffmpeg.exe` here (git-ignored or committed per your preference).

**Required build features:** `--enable-nvenc --enable-amf --enable-libvpl (qsv) --enable-libx264`,
plus `ddagrab`/`gdigrab` input devices and `palettegen`/`paletteuse`/`scale`/`fps` filters.

**Source:** BtbN FFmpeg-Builds `ffmpeg-master-latest-win64-gpl-shared` (or `-gpl`), or a
custom trimmed build (~25–35 MB) configured with only the features above.

**License:** This is a GPL build (includes libx264). Aquashot's distribution must comply
with GPL terms. nvenc needs no extra DLL — the NVIDIA driver provides `nvEncodeAPI64.dll`.
```

Create `docs/ffmpeg-bundle.md` with the same notes plus a verification command:

```markdown
# Verifying the bundled ffmpeg

Confirm encoders compiled in:

    ffmpeg -hide_banner -encoders | findstr /R "nvenc qsv amf libx264"

Confirm capture devices:

    ffmpeg -hide_banner -devices | findstr /R "gdigrab ddagrab"
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Aquashot/Aquashot.csproj`
Expected: build succeeds **if** `Resources\ffmpeg.exe` exists. If it is absent, the build
fails on the missing resource — this is the signal to drop the binary in place before shipping.

- [ ] **Step 4: Commit**

```bash
git add src/Aquashot/Aquashot.csproj src/Aquashot/Resources/README-ffmpeg.md docs/ffmpeg-bundle.md
git commit -m "build(record): embed ffmpeg.exe resource + bundling docs"
```

---

## Task 16: Manual verification (real hardware)

**Files:** none (checklist)

Cannot be automated — CI has no display or GPU. Run locally with a real bundled ffmpeg.exe.

- [ ] Tray → "Record…" opens the region picker; drag a region; control bar appears just above it.
- [ ] Click ● Record, do something on screen ~5 s, click ■ Stop.
- [ ] On an NVIDIA machine: confirm the chosen encoder is `*_nvenc` (temporarily log `DetectAsync` result), MP4 is produced and plays.
- [ ] GIF is produced, animates, colors look good (palette dithering), file < 50 MB.
- [ ] The control bar / its border does **not** appear in the recording (WDA_EXCLUDEFROMCAPTURE works).
- [ ] Paste into Discord/Explorer → the saved file pastes (CF_HDROP file-drop).
- [ ] Force fallback (rename ffmpeg without nvenc, or set `EncoderOverride` to a bogus name → falls back to libx264) and confirm MP4 still records.
- [ ] Long recording that would exceed 50 MB → balloon shows "(reduced to fit 50 MB)" and GIF is still < 50 MB.

---

## Self-Review Notes

- **Spec coverage:** capture engine (Tasks 11/13 gdigrab; ddagrab builder Task 2, selection deferred per design), encoder ladder + NVENC test-encode (Tasks 1/5), two-pass GIF (Tasks 2/8), MP4 size targeting (Tasks 3/8), embedded trimmed GPL ffmpeg (Tasks 6/15), control bar capture-exclusion (Task 12), clipboard file-drop (Task 9), tray wiring + `_busy` (Task 14), error handling (Tasks 7/13/14), testing strategy (pure units + mock runner throughout).
- **Deferred (matches design "future enhancement"):** runtime ddagrab-vs-gdigrab auto-selection — the `CaptureDdagrab` builder exists and is tested, but `RecordingController` uses `CaptureGdigrab` (universal). Promoting ddagrab is a follow-up once validated on GPU hardware.
- **Type consistency:** `RecordFormats` flags, `EncoderCandidate`, `RecordResult(Files, SizeCapForced)`, `IFFmpegRunner.RunAsync/StartCapture`, `IFFmpegSession.StopAsync`, `RecordingEncoder.ProduceAsync(...)`, `OutputService.RecordingOutputBase/CopyFileToClipboard` are used consistently across tasks.
- **Verify-before-adapt callouts:** `SelectionEngine.Normalize` (Task 11), `FilenameGenerator.Generate` extension handling (Task 9) — read the source and adapt if signatures differ.
