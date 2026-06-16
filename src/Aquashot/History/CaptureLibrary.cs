using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Aquashot.History;

// Persisted, de-duplicated, capped index of captures (newest first). Backs the tray
// "Recent" menu, the history window, and text search. Persistence is best-effort and
// never throws into a capture.
public class CaptureLibrary
{
    private readonly string _path;
    private readonly int _cap;
    private List<CaptureEntry> _entries = new();

    public CaptureLibrary(string path, int cap = 1000, string? legacyRecentPath = null)
    {
        _path = path;
        _cap = Math.Max(1, cap);
        Load(legacyRecentPath);
    }

    public IReadOnlyList<CaptureEntry> Entries => _entries;

    public void Add(string file, DateTime capturedAt)
    {
        if (string.IsNullOrWhiteSpace(file)) return;
        _entries.RemoveAll(e => string.Equals(e.Path, file, StringComparison.OrdinalIgnoreCase));
        _entries.Insert(0, new CaptureEntry { Path = file, CapturedAt = capturedAt });
        if (_entries.Count > _cap) _entries.RemoveRange(_cap, _entries.Count - _cap);
        Save();
    }

    public void SetOcr(string file, string text)
    {
        var i = _entries.FindIndex(e => string.Equals(e.Path, file, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return;
        _entries[i] = _entries[i] with { OcrText = text, OcrDone = true };
        Save();
    }

    public IEnumerable<CaptureEntry> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return _entries;
        return _entries.Where(e =>
            Path.GetFileName(e.Path).Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (e.OcrText?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    private void Load(string? legacyRecentPath)
    {
        try
        {
            if (File.Exists(_path))
                _entries = JsonSerializer.Deserialize<List<CaptureEntry>>(File.ReadAllText(_path)) ?? new();
            else if (legacyRecentPath != null && File.Exists(legacyRecentPath))
            {
                var paths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(legacyRecentPath)) ?? new();
                _entries = paths.Select(p => new CaptureEntry
                {
                    Path = p,
                    CapturedAt = SafeMtime(p),
                }).ToList();
            }
        }
        catch { _entries = new(); }

        _entries.RemoveAll(e => !File.Exists(e.Path));
        if (_entries.Count > _cap) _entries.RemoveRange(_cap, _entries.Count - _cap);
    }

    private static DateTime SafeMtime(string p)
    {
        try { return File.GetLastWriteTime(p); } catch { return DateTime.Now; }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_entries));
        }
        catch { /* best-effort */ }
    }
}
