using System;
using System.IO;
using System.Linq;
using Aquashot.History;
using FluentAssertions;
using Xunit;

public class CaptureLibraryTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;
    public CaptureLibraryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "aqlib-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "library.json");
    }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string MakeFile(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, "x");
        return p;
    }

    [Fact]
    public void Add_inserts_newest_first_and_dedupes()
    {
        var lib = new CaptureLibrary(_path);
        var a = MakeFile("a.png"); var b = MakeFile("b.png");
        lib.Add(a, new DateTime(2026, 1, 1));
        lib.Add(b, new DateTime(2026, 1, 2));
        lib.Add(a, new DateTime(2026, 1, 3));
        lib.Entries.Select(e => e.Path).Should().Equal(a, b);
    }

    [Fact]
    public void Prunes_missing_files_on_load()
    {
        var present = MakeFile("p.png");
        var lib = new CaptureLibrary(_path);
        lib.Add(present, DateTime.Now);
        lib.Add(Path.Combine(_dir, "gone.png"), DateTime.Now);
        var reloaded = new CaptureLibrary(_path);
        reloaded.Entries.Select(e => e.Path).Should().Equal(present);
    }

    [Fact]
    public void Search_matches_filename_and_ocr_text_case_insensitive()
    {
        var lib = new CaptureLibrary(_path);
        var inv = MakeFile("invoice.png");
        var cat = MakeFile("cat.png");
        lib.Add(inv, DateTime.Now);
        lib.Add(cat, DateTime.Now);
        lib.SetOcr(cat, "Total due $42 INVOICE");

        lib.Search("invoice").Select(e => e.Path).Should().BeEquivalentTo(new[] { inv, cat });
        lib.Search("CAT").Select(e => e.Path).Should().Equal(cat);
        lib.Search("").Select(e => e.Path).Should().HaveCount(2);
    }

    [Fact]
    public void SetOcr_persists_across_reload()
    {
        var f = MakeFile("doc.png");
        var lib = new CaptureLibrary(_path);
        lib.Add(f, DateTime.Now);
        lib.SetOcr(f, "hello world");
        var reloaded = new CaptureLibrary(_path);
        var e = reloaded.Entries.Single();
        e.OcrDone.Should().BeTrue();
        e.OcrText.Should().Be("hello world");
    }

    [Fact]
    public void Migrates_legacy_recent_json_paths()
    {
        var f = MakeFile("legacy.png");
        var recent = Path.Combine(_dir, "recent.json");
        File.WriteAllText(recent, System.Text.Json.JsonSerializer.Serialize(new[] { f }));
        var lib = new CaptureLibrary(_path, legacyRecentPath: recent);
        lib.Entries.Select(e => e.Path).Should().Equal(f);
    }
}
