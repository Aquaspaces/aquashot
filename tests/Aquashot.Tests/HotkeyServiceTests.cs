using System;
using System.Linq;
using System.Windows.Input;
using FluentAssertions;
using Aquashot.Input;
using Xunit;

namespace Aquashot.Tests;

public class HotkeyServiceTests
{
    [Fact]
    public void HotkeyAction_ValuesAreDistinct_SoIdsDoNotCollide()
    {
        // Ids are computed as BASE + (int)action, so distinct enum values guarantee distinct ids.
        var values = Enum.GetValues<HotkeyAction>().Select(a => (int)a).ToArray();
        values.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void HotkeyAction_ContainsTheWiredActions()
    {
        var names = Enum.GetNames<HotkeyAction>();
        names.Should().Contain(new[]
        {
            nameof(HotkeyAction.Capture), nameof(HotkeyAction.Freeze),
            nameof(HotkeyAction.ScrollingCapture), nameof(HotkeyAction.RepeatLastRegion),
            nameof(HotkeyAction.RecordRegion), nameof(HotkeyAction.CaptureWindow),
            nameof(HotkeyAction.CaptureFullScreen)
        });
    }

    [Fact]
    public void ParseHotkey_PrintScreen_NoModifiers()
    {
        var (mods, vk) = HotkeyService.ParseHotkey("PrintScreen");
        mods.Should().Be(0u);
        vk.Should().Be((uint)KeyInterop.VirtualKeyFromKey(Key.PrintScreen));
    }

    [Fact]
    public void ParseHotkey_CtrlAltS_HasModifiersAndKey()
    {
        var (mods, vk) = HotkeyService.ParseHotkey("Ctrl+Alt+S");
        vk.Should().Be((uint)KeyInterop.VirtualKeyFromKey(Key.S));
        mods.Should().NotBe(0u);
        // order-independent
        HotkeyService.ParseHotkey("Alt+Ctrl+S").mods.Should().Be(mods);
        // a no-modifier combo differs
        HotkeyService.ParseHotkey("S").mods.Should().Be(0u);
    }

    [Fact]
    public void ParseHotkey_Unknown_ReturnsZeroVk()
    {
        HotkeyService.ParseHotkey("NotAKey").vk.Should().Be(0u);
    }
}
