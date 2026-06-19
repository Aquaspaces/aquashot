using System.Text;
using System.Text.RegularExpressions;

namespace Aquashot.Output;

public static class FilenameGenerator
{
    private const int MaxTokenLen = 60;

    public static string Generate(string pattern, string ext, DateTime now,
        string? windowTitle = null, string? appName = null)
    {
        // Substitute the {window}/{app} tokens FIRST (sanitized), so a date-format pass can't
        // mangle their text and a date pattern still works alongside them.
        var withTokens = Regex.Replace(pattern, "{(window|app)}", m =>
            string.Equals(m.Groups[1].Value, "window", StringComparison.OrdinalIgnoreCase)
                ? Sanitize(windowTitle) : Sanitize(appName),
            RegexOptions.IgnoreCase);

        var name = Regex.Replace(withTokens, "{(.*?)}", m => now.ToString(m.Groups[1].Value));
        var dotExt = "." + ext.TrimStart('.');
        return name.EndsWith(dotExt, StringComparison.OrdinalIgnoreCase) ? name : name + dotExt;
    }

    // Make a window title / app name safe for a filename: drop path-illegal + control chars,
    // collapse whitespace, trim, cap length. Empty/blank input yields "".
    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|') sb.Append('_');
            else if (char.IsControl(c)) sb.Append(' ');
            else sb.Append(c);
        }
        // collapse runs of whitespace to a single space, then trim
        var collapsed = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        if (collapsed.Length > MaxTokenLen) collapsed = collapsed[..MaxTokenLen].Trim();
        return collapsed;
    }
}
