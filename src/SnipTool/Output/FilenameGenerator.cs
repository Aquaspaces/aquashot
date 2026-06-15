using System.Text.RegularExpressions;

namespace SnipTool.Output;

public static class FilenameGenerator
{
    public static string Generate(string pattern, string ext, DateTime now)
    {
        var name = Regex.Replace(pattern, "{(.*?)}", m => now.ToString(m.Groups[1].Value));
        var dotExt = "." + ext.TrimStart('.');
        return name.EndsWith(dotExt, StringComparison.OrdinalIgnoreCase) ? name : name + dotExt;
    }
}
