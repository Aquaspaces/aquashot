using System.Text;
using System.Text.RegularExpressions;

namespace Aquashot.Output;

public static class FilenameGenerator
{
    private const int MaxTokenLen = 60;

    public static string Generate(string pattern, string ext, DateTime now,
        string? windowTitle = null, string? appName = null)
    {
        // One pass keeps braces in window titles/app names from becoming date formats.
        var name = Regex.Replace(pattern, "{(.*?)}", m =>
        {
            var token = m.Groups[1].Value;
            if (token.Equals("window", StringComparison.OrdinalIgnoreCase)) return Sanitize(windowTitle);
            if (token.Equals("app", StringComparison.OrdinalIgnoreCase)) return Sanitize(appName);
            try { return now.ToString(token); }
            catch (FormatException) { return m.Value; } // keep unfinished/invalid formats usable in the preview
        });
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
