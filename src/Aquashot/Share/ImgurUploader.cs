using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Aquashot.Settings;

namespace Aquashot.Share;

// Anonymous Imgur upload: multipart POST to the v3 image endpoint with a "Client-ID" authorization
// header; the link lives at $.data.link in the JSON response. HttpClient is injectable for tests.
public sealed class ImgurUploader : IUploader
{
    private const string Endpoint = "https://api.imgur.com/3/image";
    private readonly HttpClient _http;

    public ImgurUploader(HttpClient http) => _http = http;

    public async Task<ShareResult> UploadAsync(string filePath, AppSettings settings, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(settings.ImgurClientId))
                return ShareResult.Failure("Imgur client id not set.");
            if (!File.Exists(filePath))
                return ShareResult.Failure("File not found: " + filePath);

            using var form = new MultipartFormDataContent();
            var bytes = await File.ReadAllBytesAsync(filePath, ct);
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(part, "image", Path.GetFileName(filePath));

            using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = form };
            req.Headers.TryAddWithoutValidation("Authorization", "Client-ID " + settings.ImgurClientId.Trim());

            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                // Redact the client-id in case an error body (rate-limit/4xx) echoes it back, so the
                // credential never lands in a user-visible toast.
                return ShareResult.Failure(
                    $"Imgur upload failed ({(int)resp.StatusCode}): {Trim(Redact(body, settings.ImgurClientId))}");

            var url = ShareService.ExtractUrl(body, "$.data.link");
            return string.IsNullOrWhiteSpace(url)
                ? ShareResult.Failure("Imgur response had no link.")
                : ShareResult.Success(url!);
        }
        catch (OperationCanceledException) { return ShareResult.Failure("Upload cancelled."); }
        catch (Exception ex) { return ShareResult.Failure("Imgur upload error: " + ex.Message); }
    }

    // Keep error toasts short by clipping the response body.
    private static string Trim(string s) => s.Length > 200 ? s[..200] : s;

    // Strip any occurrence of the client-id from a string before it reaches the user.
    private static string Redact(string s, string? clientId) =>
        string.IsNullOrEmpty(clientId) ? s : s.Replace(clientId.Trim(), "[REDACTED]");
}
