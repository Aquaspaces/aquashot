using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Aquashot.History;

// A small persisted list of recently saved capture files (newest first, de-duplicated,
// capped). Backs the tray "Recent" menu so power users can re-open recent captures fast.
public class RecentCaptures
{
    private readonly string _path;
    private readonly int _cap;
    private List<string> _items = new();

    public RecentCaptures(string path, int cap = 20)
    {
        _path = path;
        _cap = Math.Max(1, cap);
        Load();
    }

    public IReadOnlyList<string> Items => _items;

    public void Add(string file)
    {
        if (string.IsNullOrWhiteSpace(file)) return;
        _items.RemoveAll(p => string.Equals(p, file, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, file);
        if (_items.Count > _cap) _items.RemoveRange(_cap, _items.Count - _cap);
        Save();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
                _items = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path)) ?? new();
        }
        catch { _items = new(); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_items));
        }
        catch { /* history is best-effort; never break a capture over it */ }
    }
}
