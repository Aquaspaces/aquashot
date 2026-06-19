using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Aquashot.Settings;
using Aquashot.Share;
using Xunit;

namespace Aquashot.Tests;

public class ShareServiceTests
{
    // ===== ExtractUrl (pure JSON path) =====

    [Fact]
    public void ExtractUrl_ReadsImgurShapedResponse()
    {
        var json = """{"data":{"id":"abc","link":"https://i.imgur.com/abc.png"},"success":true,"status":200}""";
        ShareService.ExtractUrl(json, "$.data.link").Should().Be("https://i.imgur.com/abc.png");
    }

    [Fact]
    public void ExtractUrl_LeadingDollarOptional()
    {
        var json = """{"url":"https://x/y.png"}""";
        ShareService.ExtractUrl(json, "url").Should().Be("https://x/y.png");
    }

    [Theory]
    [InlineData("$.data.missing")]
    [InlineData("$.nope.link")]
    public void ExtractUrl_ReturnsNull_WhenPathMissing(string path)
    {
        var json = """{"data":{"link":"https://x/y.png"}}""";
        ShareService.ExtractUrl(json, path).Should().BeNull();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("   ")]
    public void ExtractUrl_ReturnsNull_OnInvalidJson(string json)
    {
        ShareService.ExtractUrl(json, "$.data.link").Should().BeNull();
    }

    [Fact]
    public void ExtractUrl_BlankPath_ReturnsNull()
    {
        ShareService.ExtractUrl("""{"a":1}""", "").Should().BeNull();
    }

    // ===== FormatCopy =====

    [Fact]
    public void FormatCopy_Url_ReturnsBareUrl()
    {
        ShareService.FormatCopy("https://x/y.png", "Url", "shot.png").Should().Be("https://x/y.png");
    }

    [Fact]
    public void FormatCopy_Markdown_WrapsWithAltText()
    {
        ShareService.FormatCopy("https://x/y.png", "Markdown", "shot.png")
            .Should().Be("![shot.png](https://x/y.png)");
    }

    [Fact]
    public void FormatCopy_Html_EmitsImgTag()
    {
        ShareService.FormatCopy("https://x/y.png", "Html", "shot.png")
            .Should().Be("<img src=\"https://x/y.png\" alt=\"shot.png\">");
    }

    [Fact]
    public void FormatCopy_UnknownFormat_FallsBackToUrl()
    {
        ShareService.FormatCopy("https://x/y.png", "weird", "shot.png").Should().Be("https://x/y.png");
    }

    [Fact]
    public void FormatCopy_BlankFileName_UsesPlaceholderAlt()
    {
        ShareService.FormatCopy("https://x/y.png", "Markdown", "").Should().Be("![image](https://x/y.png)");
    }

    // ===== For (provider selection) =====

    [Fact]
    public void For_None_ReturnsNull()
    {
        ShareService.For(new AppSettings { ShareProvider = "None" }).Should().BeNull();
    }

    [Fact]
    public void For_Imgur_WithClientId_ReturnsImgurUploader()
    {
        ShareService.For(new AppSettings { ShareProvider = "Imgur", ImgurClientId = "abc" })
            .Should().BeOfType<ImgurUploader>();
    }

    [Fact]
    public void For_Imgur_WithoutClientId_ReturnsNull()
    {
        ShareService.For(new AppSettings { ShareProvider = "Imgur", ImgurClientId = "" }).Should().BeNull();
    }

    [Fact]
    public void For_Custom_WithUrl_ReturnsCustomUploader()
    {
        ShareService.For(new AppSettings { ShareProvider = "Custom", CustomUploadUrl = "https://x/upload" })
            .Should().BeOfType<CustomHttpUploader>();
    }

    [Fact]
    public void For_Custom_WithoutUrl_ReturnsNull()
    {
        ShareService.For(new AppSettings { ShareProvider = "Custom", CustomUploadUrl = "" }).Should().BeNull();
    }

    // ===== CustomHttpUploader.ExtractByPath =====

    [Fact]
    public void ExtractByPath_JsonPath_ReadsValue()
    {
        var json = """{"result":{"url":"https://host/f.png"}}""";
        CustomHttpUploader.ExtractByPath(json, "$.result.url").Should().Be("https://host/f.png");
    }

    [Fact]
    public void ExtractByPath_Regex_ExtractsCaptureGroup()
    {
        var body = "OK https://host/abc123.png done";
        CustomHttpUploader.ExtractByPath(body, @"regex:(https://\S+\.png)").Should().Be("https://host/abc123.png");
    }

    [Fact]
    public void ExtractByPath_InvalidRegex_ReturnsNull()
    {
        CustomHttpUploader.ExtractByPath("anything", "regex:(unclosed").Should().BeNull();
    }

    [Fact]
    public void ExtractByPath_BlankPath_ReturnsTrimmedBody()
    {
        CustomHttpUploader.ExtractByPath("  https://host/f.png\n", "").Should().Be("https://host/f.png");
    }

    // ===== CustomHttpUploader.ParseHeaders =====

    [Fact]
    public void ParseHeaders_ParsesKeyValueLines()
    {
        var pairs = CustomHttpUploader.ParseHeaders("Authorization: Bearer tok\nX-Field: v").ToList();
        pairs.Should().HaveCount(2);
        pairs[0].Should().Be(("Authorization", "Bearer tok"));
        pairs[1].Should().Be(("X-Field", "v"));
    }

    [Fact]
    public void ParseHeaders_SkipsBlankAndCommentLines()
    {
        var pairs = CustomHttpUploader.ParseHeaders("\n# comment\nA: 1\n  \n").ToList();
        pairs.Should().ContainSingle().Which.Should().Be(("A", "1"));
    }

    [Fact]
    public void ParseHeaders_Empty_YieldsNothing()
    {
        CustomHttpUploader.ParseHeaders("").Should().BeEmpty();
        CustomHttpUploader.ParseHeaders(null).Should().BeEmpty();
    }

    // ===== Uploaders with a stubbed HttpMessageHandler (no network) =====

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastAuthorization { get; private set; }

        public StubHandler(HttpStatusCode status, string body) { _status = status; _body = body; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastAuthorization = request.Headers.TryGetValues("Authorization", out var v) ? string.Join(",", v) : null;
            // Drain the multipart body so any content is materialized (mirrors a real send).
            if (request.Content != null) await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(_status) { Content = new StringContent(_body) };
        }
    }

    private static string TempImage()
    {
        var p = Path.Combine(Path.GetTempPath(), $"aqua-share-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(p, new byte[] { 1, 2, 3, 4 });
        return p;
    }

    [Fact]
    public async Task ImgurUploader_SendsClientIdHeaderAndParsesLink()
    {
        var stub = new StubHandler(HttpStatusCode.OK,
            """{"data":{"link":"https://i.imgur.com/ok.png"},"success":true}""");
        var uploader = new ImgurUploader(new HttpClient(stub));
        var file = TempImage();
        try
        {
            var result = await uploader.UploadAsync(file, new AppSettings { ImgurClientId = "CID123" });
            result.Ok.Should().BeTrue();
            result.Url.Should().Be("https://i.imgur.com/ok.png");
            stub.LastRequest!.RequestUri!.ToString().Should().Be("https://api.imgur.com/3/image");
            stub.LastRequest.Method.Should().Be(HttpMethod.Post);
            stub.LastAuthorization.Should().Be("Client-ID CID123");
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task ImgurUploader_ReturnsError_On4xx()
    {
        var stub = new StubHandler(HttpStatusCode.BadRequest, """{"data":{"error":"bad"},"success":false}""");
        var uploader = new ImgurUploader(new HttpClient(stub));
        var file = TempImage();
        try
        {
            var result = await uploader.UploadAsync(file, new AppSettings { ImgurClientId = "CID" });
            result.Ok.Should().BeFalse();
            result.Error.Should().Contain("400");
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task ImgurUploader_MissingClientId_FailsWithoutSending()
    {
        var stub = new StubHandler(HttpStatusCode.OK, "{}");
        var uploader = new ImgurUploader(new HttpClient(stub));
        var file = TempImage();
        try
        {
            var result = await uploader.UploadAsync(file, new AppSettings { ImgurClientId = "" });
            result.Ok.Should().BeFalse();
            stub.LastRequest.Should().BeNull(); // no network attempt
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task ImgurUploader_MissingFile_Fails()
    {
        var stub = new StubHandler(HttpStatusCode.OK, "{}");
        var uploader = new ImgurUploader(new HttpClient(stub));
        var result = await uploader.UploadAsync(@"C:\does\not\exist.png", new AppSettings { ImgurClientId = "CID" });
        result.Ok.Should().BeFalse();
        result.Error.Should().Contain("File not found");
    }

    [Fact]
    public async Task CustomHttpUploader_PostsToConfiguredUrlAndParsesResponse()
    {
        var stub = new StubHandler(HttpStatusCode.OK, """{"result":{"url":"https://host/f.png"}}""");
        var uploader = new CustomHttpUploader(new HttpClient(stub));
        var file = TempImage();
        try
        {
            var settings = new AppSettings
            {
                CustomUploadUrl = "https://host/upload",
                CustomUploadFieldName = "upload",
                CustomUploadHeaders = "X-Token: secret",
                CustomUploadResponseJsonPath = "$.result.url"
            };
            var result = await uploader.UploadAsync(file, settings);
            result.Ok.Should().BeTrue();
            result.Url.Should().Be("https://host/f.png");
            stub.LastRequest!.RequestUri!.ToString().Should().Be("https://host/upload");
            stub.LastRequest.Headers.GetValues("X-Token").Should().ContainSingle("secret");
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task CustomHttpUploader_ReturnsError_On5xx()
    {
        var stub = new StubHandler(HttpStatusCode.InternalServerError, "boom");
        var uploader = new CustomHttpUploader(new HttpClient(stub));
        var file = TempImage();
        try
        {
            var result = await uploader.UploadAsync(file,
                new AppSettings { CustomUploadUrl = "https://host/upload" });
            result.Ok.Should().BeFalse();
            result.Error.Should().Contain("500");
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task CustomHttpUploader_RegexResponsePath_Works()
    {
        var stub = new StubHandler(HttpStatusCode.OK, "Uploaded to https://host/xyz.png OK");
        var uploader = new CustomHttpUploader(new HttpClient(stub));
        var file = TempImage();
        try
        {
            var settings = new AppSettings
            {
                CustomUploadUrl = "https://host/upload",
                CustomUploadResponseJsonPath = @"regex:(https://\S+\.png)"
            };
            var result = await uploader.UploadAsync(file, settings);
            result.Ok.Should().BeTrue();
            result.Url.Should().Be("https://host/xyz.png");
        }
        finally { File.Delete(file); }
    }
}
