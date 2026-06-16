using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aquashot.History;
using FluentAssertions;
using Xunit;

public class OcrIndexerTests : IDisposable
{
    private readonly string _dir;
    public OcrIndexerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "aqocr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class FakeOcr : IOcrService
    {
        public Task<string> RecognizeAsync(string imagePath) =>
            Task.FromResult("text-of-" + Path.GetFileNameWithoutExtension(imagePath));

        public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlyList<OcrLine>> RecognizeLinesAsync(string imagePath)
            => System.Threading.Tasks.Task.FromResult((System.Collections.Generic.IReadOnlyList<OcrLine>)System.Array.Empty<OcrLine>());
    }

    private string MakeImage(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, "x");
        return p;
    }

    [Fact]
    public async Task Enqueue_runs_ocr_and_stores_text()
    {
        var lib = new CaptureLibrary(Path.Combine(_dir, "lib.json"));
        var f = MakeImage("a.png");
        lib.Add(f, DateTime.Now);
        var idx = new OcrIndexer(lib, new FakeOcr());

        await idx.EnqueueAsync(f);

        lib.Entries.Single().OcrText.Should().Be("text-of-a");
    }

    [Fact]
    public async Task Backfill_indexes_new_files_and_ocrs_pending()
    {
        var lib = new CaptureLibrary(Path.Combine(_dir, "lib.json"));
        var known = MakeImage("known.png");
        lib.Add(known, DateTime.Now);
        var loose = MakeImage("loose.png");
        MakeImage("note.txt");
        var idx = new OcrIndexer(lib, new FakeOcr());

        await idx.BackfillAsync(_dir);

        lib.Entries.Should().HaveCount(2);
        lib.Entries.All(e => e.OcrDone).Should().BeTrue();
        lib.Entries.Select(e => e.Path).Should().Contain(loose);
    }
}
