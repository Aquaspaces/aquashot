using FluentAssertions;
using Aquashot.Recording.InputHud;
using Xunit;

namespace Aquashot.Tests;

public class KeystrokeFormatterTests
{
    // Virtual-key codes used below (Win32 VK_*).
    private const int VkS = 0x53, VkA = 0x41, VkEnter = 0x0D, VkEsc = 0x1B, VkSpace = 0x20;
    private const int VkLeft = 0x25, VkUp = 0x26, VkRight = 0x27, VkDown = 0x28;
    private const int VkF5 = 0x74, Vk1 = 0x31, VkNum3 = 0x63;
    private const int VkCtrl = 0x11, VkShift = 0x10, VkAlt = 0x12, VkLWin = 0x5B;

    [Fact]
    public void Plain_letter_has_no_modifiers()
    {
        KeystrokeFormatter.Format(VkA, KeyMods.None).Should().Be("A");
    }

    [Fact]
    public void Ctrl_combo_prefixes_ctrl()
    {
        KeystrokeFormatter.Format(VkS, KeyMods.Ctrl).Should().Be("Ctrl+S");
    }

    [Fact]
    public void Modifiers_render_in_canonical_order()
    {
        KeystrokeFormatter.Format(VkS, KeyMods.Ctrl | KeyMods.Shift | KeyMods.Alt | KeyMods.Win)
            .Should().Be("Ctrl+Alt+Shift+Win+S");
    }

    [Theory]
    [InlineData(VkEnter, "Enter")]
    [InlineData(VkEsc, "Esc")]
    [InlineData(VkSpace, "Space")]
    [InlineData(VkLeft, "←")]
    [InlineData(VkUp, "↑")]
    [InlineData(VkRight, "→")]
    [InlineData(VkDown, "↓")]
    public void Special_keys_have_friendly_names(int vk, string expected)
    {
        KeystrokeFormatter.Format(vk, KeyMods.None).Should().Be(expected);
    }

    [Fact]
    public void Function_digit_and_numpad_keys_named()
    {
        KeystrokeFormatter.KeyName(VkF5).Should().Be("F5");
        KeystrokeFormatter.KeyName(Vk1).Should().Be("1");
        KeystrokeFormatter.KeyName(VkNum3).Should().Be("Num3");
    }

    [Theory]
    [InlineData(VkCtrl)]
    [InlineData(VkShift)]
    [InlineData(VkAlt)]
    [InlineData(VkLWin)]
    public void Standalone_modifier_press_yields_empty_caption(int vk)
    {
        KeystrokeFormatter.IsModifierVk(vk).Should().BeTrue();
        // Even when other modifiers are reported held, a lone modifier key produces no caption.
        KeystrokeFormatter.Format(vk, KeyMods.Ctrl).Should().BeEmpty();
    }
}
