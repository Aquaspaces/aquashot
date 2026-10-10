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
        public int LineCalls { get; private set; }
        public Task<string> RecognizeAsync(string imagePath) => Task.FromResult("");
        public Task<IReadOnlyList<OcrLine>> RecognizeLinesAsync(string imagePath)
        {
            LineCalls++;
            return Task.FromResult((IReadOnlyList<OcrLine>)Array.Empty<OcrLine>());
        }
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
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/Aquashot;component/Theme/Theme.xaml")
                    });
                }

                var dir = Path.Combine(Path.GetTempPath(), "aqhw-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                var lib = new CaptureLibrary(Path.Combine(dir, "library.json"));
                var imagePath = Path.Combine(dir, "capture.png");
                File.WriteAllBytes(imagePath, Array.Empty<byte>());
                lib.Add(imagePath, DateTime.Now);
                var ocr = new FakeOcr();
                var indexer = new OcrIndexer(lib, ocr);

                var w = new HistoryWindow(lib, indexer, ocr, dir, enableOcr: false,
                    thumbSize: 200, persistThumbSize: _ => { });
                const System.Reflection.BindingFlags privateInstance =
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(HistoryWindow).GetMethod("Refresh", privateInstance)!.Invoke(w, null);
                w.OpenDetailFor(imagePath);
                typeof(HistoryWindow).GetMethod("LoadOcr", privateInstance)!.Invoke(w, null);
                ocr.LineCalls.Should().Be(0, "disabled OCR must not run when a capture is opened");
                w.Close();
                foreach (var name in new[] { "_searchTimer", "_detailTimer", "_ocrTimer", "_hoverTimer" })
                    ((System.Windows.Threading.DispatcherTimer)typeof(HistoryWindow)
                        .GetField(name, privateInstance)!.GetValue(w)!).IsEnabled.Should().BeFalse();

                var settings = new Aquashot.Settings.SettingsWindow(new Aquashot.Settings.AppSettings());
                var settingsContent = (FrameworkElement)settings.Content;
                settingsContent.Measure(new Size(520, 560));
                settingsContent.Arrange(new Rect(0, 0, 520, 560));
                settingsContent.UpdateLayout();
                var scroll = (System.Windows.Controls.ScrollViewer)settings.FindName("SettingsScroll");
                var save = (System.Windows.Controls.Button)settings.FindName("SaveButton");
                scroll.ScrollableHeight.Should().BeGreaterThan(0, "the long settings form must scroll");
                save.TransformToAncestor(settingsContent).Transform(new Point(0, 0)).Y.Should().BeLessThan(560);
                save.ActualHeight.Should().BeGreaterThan(0);
                settings.Close();

                // Keep the single Application dispatcher alive while a background encode copies
                // through an injected action, so this test never replaces the user's clipboard.
                var clipboardCheck = Aquashot.Tests.RecordingControllerTests.VerifyBackgroundClipboardDispatchAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10));
                var frame = new System.Windows.Threading.DispatcherFrame();
                var dispatcher = Application.Current!.Dispatcher;
                _ = clipboardCheck.ContinueWith(_ => dispatcher.BeginInvoke(
                    new Action(() => frame.Continue = false)));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                clipboardCheck.GetAwaiter().GetResult();
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
