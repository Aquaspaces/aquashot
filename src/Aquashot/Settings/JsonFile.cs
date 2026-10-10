using System.IO;
using System.Text.Json;

namespace Aquashot.Settings;

internal static class JsonFile
{
    // Write beside the destination, then replace it so an interrupted write cannot truncate it.
    internal static void Write<T>(string path, T value, JsonSerializerOptions? options = null)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(value, options));
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
