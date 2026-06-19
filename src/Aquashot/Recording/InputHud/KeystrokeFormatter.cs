using System.Collections.Generic;
using System.Text;

namespace Aquashot.Recording.InputHud;

// Modifier flags for a keystroke caption (kept WPF-free so the formatter is unit-testable).
[System.Flags]
public enum KeyMods { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8 }

// Pure virtual-key -> caption formatting for the keystroke HUD. Turns a Win32 vkCode plus the
// held modifiers into a readable badge like "Ctrl+Shift+S", "Enter", "Esc", "Space", "↑".
// No WPF/Win32 dependency so it can be exercised in plain unit tests.
public static class KeystrokeFormatter
{
    // Build a caption for vk with the given modifiers. Modifier-only presses (Ctrl/Alt/Shift/Win
    // by themselves) return an empty string so the HUD doesn't show a lone "Ctrl".
    public static string Format(int vk, KeyMods mods)
    {
        if (IsModifierVk(vk)) return "";

        var sb = new StringBuilder();
        if (mods.HasFlag(KeyMods.Ctrl)) sb.Append("Ctrl+");
        if (mods.HasFlag(KeyMods.Alt)) sb.Append("Alt+");
        if (mods.HasFlag(KeyMods.Shift)) sb.Append("Shift+");
        if (mods.HasFlag(KeyMods.Win)) sb.Append("Win+");
        sb.Append(KeyName(vk));
        return sb.ToString();
    }

    // True for the standalone modifier virtual-keys (so they're skipped as captions).
    public static bool IsModifierVk(int vk) => vk switch
    {
        0x10 or 0xA0 or 0xA1 => true, // Shift / LShift / RShift
        0x11 or 0xA2 or 0xA3 => true, // Control / LCtrl / RCtrl
        0x12 or 0xA4 or 0xA5 => true, // Alt(Menu) / LAlt / RAlt
        0x5B or 0x5C => true,         // LWin / RWin
        _ => false
    };

    // The display name for a non-modifier virtual key.
    public static string KeyName(int vk)
    {
        if (Names.TryGetValue(vk, out var n)) return n;
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();        // 0-9
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();        // A-Z
        if (vk >= 0x60 && vk <= 0x69) return "Num" + (vk - 0x60);          // numpad 0-9
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1);        // F1-F24
        return "Key" + vk;
    }

    private static readonly Dictionary<int, string> Names = new()
    {
        [0x08] = "Backspace",
        [0x09] = "Tab",
        [0x0D] = "Enter",
        [0x1B] = "Esc",
        [0x20] = "Space",
        [0x21] = "PgUp",
        [0x22] = "PgDn",
        [0x23] = "End",
        [0x24] = "Home",
        [0x25] = "←",
        [0x26] = "↑",
        [0x27] = "→",
        [0x28] = "↓",
        [0x2C] = "PrtSc",
        [0x2D] = "Insert",
        [0x2E] = "Delete",
        [0x6A] = "Num*",
        [0x6B] = "Num+",
        [0x6D] = "Num-",
        [0x6E] = "Num.",
        [0x6F] = "Num/",
        [0xBA] = ";",
        [0xBB] = "=",
        [0xBC] = ",",
        [0xBD] = "-",
        [0xBE] = ".",
        [0xBF] = "/",
        [0xC0] = "`",
        [0xDB] = "[",
        [0xDC] = "\\",
        [0xDD] = "]",
        [0xDE] = "'",
    };
}
