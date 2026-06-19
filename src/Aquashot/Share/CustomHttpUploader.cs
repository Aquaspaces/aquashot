using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Aquashot.Settings;

namespace Aquashot.Share;

// Generic multipart upload to a configured endpoint with optional custom headers. The returned URL
// is pulled out of the response via the configured path: a "regex:<pattern>" extracts capture
// group 1, otherwise a dotted JSON path (e.g. "$.data.link"). HttpClient is injectable for tests.
public sealed class CustomHttpUploader : IUploader
{
    private readonly HttpClient _http;

    public CustomHttpUploader(HttpClient http) => _http = http;

    public async Task<ShareResult> UploadAsync(string filePath, AppSettings settings, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(settings.CustomUploadUrl))
                return ShareResult.Failure("Custom upload URL not set.");
            if (!File.Exists(filePath))
                return ShareResult.Failure("File not found: " + filePath);

            using var form = new MultipartFormDataContent();
            var bytes = await File.ReadAllBytesAsync(filePath, ct);
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            var field = string.IsNullOrWhiteSpace(settings.CustomUploadFieldName) ? "file" : settings.CustomUploadFieldName.Trim();
            form.Add(part, field, Path.GetFileName(filePath));

            using var req = new HttpRequestMessage(HttpMethod.Post, settings.CustomUploadUrl.Trim()) { Content = form };
            foreach (var (k, v) in ParseHeaders(settings.CustomUploadHeaders))
                req.Headers.TryAddWithoutValidation(k, v);

            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return ShareResult.Failure($"Upload failed ({(int)resp.StatusCode}): {Trim(body)}");

            var url = ExtractByPath(body, settings.CustomUploadResponseJsonPath);
            return string.IsNullOrWhiteSpace(url)
                ? ShareResult.Failure("Could not find a URL in the response.")
                : ShareResult.Success(url!);
        }
        catch (OperationCanceledException) { return ShareResult.Failure("Upload cancelled."); }
        catch (Exception ex) { return ShareResult.Failure("Custom upload error: " + ex.Message); }
    }

    // Pull the URL out of a response body: "regex:<pattern>" uses capture group 1 (or the whole
    // match), otherwise a dotted JSON path. Blank path validates the trimmed body as a plain-text
    // URL. Every path validates the result is an absolute http(s) URL so a non-URL response body
    // (HTML error page, JSON blob, binary garbage) is never copied/shown as a "link".
    public static string? ExtractByPath(string body, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            var trimmed = body?.Trim();
            return ShareService.IsHttpUrl(trimmed) ? trimmed : null;
        }
        var p = path.Trim();
        if (p.StartsWith("regex:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                // Cap the match time: a user-supplied (potentially ReDoS) pattern must not spin the
                // thread for minutes on a large body.
                var m = Regex.Match(body ?? "", p["regex:".Length..],
                    RegexOptions.None, TimeSpan.FromSeconds(5));
                if (!m.Success) return null;
                var value = (m.Groups.Count > 1 ? m.Groups[1].Value : m.Value).Trim();
                return ShareService.IsHttpUrl(value) ? value : null;
            }
            catch (RegexMatchTimeoutException) { return null; } // pathological pattern -> no URL
            catch { return null; } // invalid regex -> no URL, never throw
        }
        return ShareService.ExtractUrl(body ?? "", p);
    }

    // Parse "Key: Value" lines (one per line) into header pairs; blank/comment lines are skipped.
    public static IEnumerable<(string Key, string Value)> ParseHeaders(string? headers)
    {
        if (string.IsNullOrWhiteSpace(headers)) yield break;
        foreach (var line in headers.Split('\n'))
        {
            var trimmed = line.Trim().TrimEnd('\r');
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            int colon = trimmed.IndexOf(':');
            if (colon <= 0) continue;
            var key = trimmed[..colon].Trim();
            var val = trimmed[(colon + 1)..].Trim();
            if (key.Length > 0) yield return (key, val);
        }
    }

    private static string Trim(string s) => s.Length > 200 ? s[..200] : s;
}
