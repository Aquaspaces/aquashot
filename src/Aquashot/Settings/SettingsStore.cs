using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aquashot.Settings;

public class SettingsStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public SettingsStore(string path) => _path = path;

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Aquashot", "settings.json");

    public AppSettings Load()
    {
        if (!File.Exists(_path)) return new AppSettings();
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(_path))?.AsObject();
            if (json == null) return new AppSettings();
            // Explicit nulls in older/edited files should use the same defaults as missing keys.
            foreach (var key in json.Where(p => p.Value == null).Select(p => p.Key).ToList())
                json.Remove(key);
            return json.Deserialize<AppSettings>() ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public void Save(AppSettings settings)
    {
        JsonFile.Write(_path, settings, Opts);
    }
}
