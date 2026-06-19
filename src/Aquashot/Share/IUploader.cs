using System.Threading;
using System.Threading.Tasks;
using Aquashot.Settings;

namespace Aquashot.Share;

// Outcome of an upload: a URL on success, an error message on failure. Never throws to the caller.
public record ShareResult(bool Ok, string? Url, string? Error)
{
    public static ShareResult Success(string url) => new(true, url, null);
    public static ShareResult Failure(string error) => new(false, null, error);
}

// Uploads a saved capture and returns a shareable URL. Implementations are best-effort and must
// surface failures via ShareResult rather than throwing.
public interface IUploader
{
    Task<ShareResult> UploadAsync(string filePath, AppSettings settings, CancellationToken ct = default);
}
