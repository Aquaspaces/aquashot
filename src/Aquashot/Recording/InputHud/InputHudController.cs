using System;
using Aquashot.Selection;
using Aquashot.Settings;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Aquashot.Recording.InputHud;

// Owns the click/keystroke HUD for one recording: the click-through overlay window plus the two
// low-level input hooks. Start() installs whatever the settings enable; Stop()/Dispose() tears it
// all down (idempotent, so every stop/close path can call it without leaking hooks or windows).
public sealed class InputHudController : IDisposable
{
    private InputHudWindow? _window;
    private LowLevelMouseHook? _mouseHook;
    private LowLevelKeyboardHook? _keyHook;
    private bool _started;

    // Whether the given settings ask for any HUD element (so callers can skip Start entirely).
    public static bool Wanted(AppSettings s) => s.ShowClickHighlight || s.ShowKeystrokeHud;

    // Install the HUD over the recorded region. Must run on the UI thread (creates a Window).
    // Safe to call once; a second call is ignored. Each enabled feature installs only its hook.
    public void Start(PixelRect region, double dpiScale, AppSettings s)
    {
        if (_started) return;
        if (!Wanted(s)) return;
        _started = true;

        var ring = ParseColor(s.ClickHighlightColor, Color.FromArgb(0x80, 0xFF, 0xD4, 0x00));
        _window = new InputHudWindow(region, dpiScale, ring, s.ClickHighlightRadius, s.KeystrokeHudSeconds);
        _window.Show();

        // The HUD is best-effort decoration: if a hook can't install (now surfaced as a throw),
        // degrade gracefully rather than break the recording.
        if (s.ShowClickHighlight)
        {
            try { _mouseHook = new LowLevelMouseHook((x, y, _) => _window?.ShowClick(x, y)); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[Aquashot] click HUD hook failed: " + ex.Message); }
        }
        if (s.ShowKeystrokeHud)
        {
            try { _keyHook = new LowLevelKeyboardHook(caption => _window?.ShowKey(caption)); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[Aquashot] keystroke HUD hook failed: " + ex.Message); }
        }
    }

    // Parse an ARGB/RGB hex colour, falling back to the supplied default on anything unparseable.
    private static Color ParseColor(string hex, Color fallback)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color c) return c;
        }
        catch { /* malformed hex -> fallback */ }
        return fallback;
    }

    public void Stop() => Dispose();

    // Idempotent teardown: unhook first (stop receiving events) then close the window.
    public void Dispose()
    {
        _mouseHook?.Dispose();
        _mouseHook = null;
        _keyHook?.Dispose();
        _keyHook = null;
        if (_window != null)
        {
            try { _window.Close(); } catch { /* window may already be gone */ }
            _window = null;
        }
        _started = false;
    }
}
