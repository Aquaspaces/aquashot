namespace Aquashot.Output;

// What the save action puts on the clipboard: a bitmap (stills only), a file-drop (paste the
// file into Explorer/Discord), the file path as text, or nothing.
public enum ClipboardMode { Image, File, Path, None }
