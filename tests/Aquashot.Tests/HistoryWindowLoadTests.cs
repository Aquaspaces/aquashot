using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Aquashot.History;
using FluentAssertions;
using Xunit;

// Regression guard: HistoryWindow's XAML must load without throwing (a connectionId / Connect
// cast error from event handlers in the wrong scope only surfaces when the window is constructed,
// not at compile time). Runs on an STA thread because WPF requires it.
public class HistoryWindowLoadTests
{
    private sealed class FakeOcr : IOcrService
    {
        public Task<string> RecognizeAsync(string imagePath) => Task.FromResult("");
        public Task<IReadOnlyList<OcrLine>> RecognizeLinesAsync(string imagePath)
            => Task.FromResult((IReadOnlyList<OcrLine>)Array.Empty<OcrLine>());
    }

    [Fact]
    public void HistoryWindow_constructs_without_xaml_errors()
    {
        Exception? captured = null;
        var t = new Thread(() =>
        {
            try
            {
                // An Application must exist (once per process) so theme StaticResources resolve.
                if (Application.Current is null)
                {
                    var app = new Application();
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/Aquashot;component/Theme/Theme.xaml")
                    });
                }

                var dir = Path.Combine(Path.GetTempPath(), "aqhw-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                var lib = new CaptureLibrary(Path.Combine(dir, "library.json"));
                var ocr = new FakeOcr();
                var indexer = new OcrIndexer(lib, ocr);

                var w = new HistoryWindow(lib, indexer, ocr, dir, enableOcr: false,
                    thumbSize: 200, persistThumbSize: _ => { });
                w.Close();
                try { Directory.Delete(dir, true); } catch { }
            }
            catch (Exception e) { captured = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();

        captured.Should().BeNull("HistoryWindow XAML should load without a parse/connect error");
    }
}
