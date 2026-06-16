using System.IO;
using FluentAssertions;
using Aquashot.Settings;
using Xunit;

namespace Aquashot.Tests;

public class SettingsStoreTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"Aquashot-test-{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaults()
    {
        var store = new SettingsStore(TempFile());
        var s = store.Load();
        s.SaveFolder.Should().NotBeNullOrWhiteSpace();
        s.FilenamePattern.Should().Be("Screenshot_{yyyy-MM-dd_HHmmss}");
        s.Hotkey.Should().Be("PrintScreen");
        s.RunAtStartup.Should().BeFalse();
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var path = TempFile();
        var store = new SettingsStore(path);
        var original = store.Load() with { Hotkey = "Ctrl+Alt+S", RunAtStartup = true, SaveFolder = @"C:\Shots", EnableOcr = false, HistoryCap = 250 };
        store.Save(original);

        var reloaded = new SettingsStore(path).Load();
        reloaded.Should().BeEquivalentTo(original);
    }
}
