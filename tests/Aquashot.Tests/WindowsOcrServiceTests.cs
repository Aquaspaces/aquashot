using System.IO;
using Aquashot.History;

namespace Aquashot.Tests;

public class WindowsOcrServiceTests
{
    [Fact]
    public async Task RecognizeLines_MissingImage_ReportsFailureInsteadOfNoText()
    {
        var path = Path.Combine(Path.GetTempPath(), "aqua-missing-" + Guid.NewGuid().ToString("N") + ".png");
        await Assert.ThrowsAsync<FileNotFoundException>(() => new WindowsOcrService().RecognizeLinesAsync(path));
    }
}
