using System.Windows.Input;
using FluentAssertions;
using SnipTool.Input;
using Xunit;

namespace SnipTool.Tests;

public class HotkeyServiceTests
{
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
