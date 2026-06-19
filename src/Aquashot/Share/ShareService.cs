using System;
using System.Net.Http;
using System.Text.Json;
using Aquashot.Settings;

namespace Aquashot.Share;

// Share façade: picks the configured uploader and provides pure URL-extraction + copy-formatting
// helpers (unit-tested without network).
public static class ShareService
{
    // The uploader for the configured provider, or null when sharing is off / misconfigured.
    // A single shared HttpClient is reused so callers don't churn sockets. An explicit timeout caps
    // a stalled upload to a slow/dead endpoint (the default 100s would block a UI-adjacent task).
    private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static IUploader? For(AppSettings s) => s.ShareProvider?.Trim().ToLowerInvariant() switch
    {
        "imgur"  => string.IsNullOrWhiteSpace(s.ImgurClientId) ? null : new ImgurUploader(Shared),
        "custom" => string.IsNullOrWhiteSpace(s.CustomUploadUrl) ? null : new CustomHttpUploader(Shared),
        _        => null,
    };

    // Extract a URL from a provider JSON response using a minimal dotted path like "$.data.link"
    // (leading "$." optional). Returns null on any missing segment / invalid JSON — never throws.
    // The extracted value is validated as an absolute http(s) URL so a non-URL response (HTML error
    // page, JSON blob) can't be copied to the clipboard / shown as a "link".
    public static string? ExtractUrl(string json, string jsonPath)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(jsonPath)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var el = doc.RootElement;
            foreach (var raw in jsonPath.Trim().TrimStart('$').Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                var seg = raw.Trim();
                if (seg.Length == 0) continue;
                if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(seg, out var next))
                    return null;
                el = next;
            }
            var value = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
            return IsHttpUrl(value) ? value : null;
        }
        catch { return null; }
    }

    // True when the string is an absolute http/https URL (the only thing we'll treat as a share link).
    public static bool IsHttpUrl(string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate) &&
        Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    // Format the shareable text for the clipboard. "Markdown" -> ![name](url); "Html" -> <img ...>;
    // anything else -> the bare URL.
    public static string FormatCopy(string url, string format, string fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "image" : fileName;
        return format?.Trim().ToLowerInvariant() switch
        {
            "markdown" => $"![{name}]({url})",
            "html"     => $"<img src=\"{url}\" alt=\"{name}\">",
            _          => url,
        };
    }
}
