using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotaryGeek.PublicKnowledge.Worker.Configuration;
using NotaryGeek.PublicKnowledge.Worker.Models;
using NotaryGeek.PublicKnowledge.Worker.Services;

namespace NotaryGeek.PublicKnowledge.Worker.Tests;

public sealed class SourceRedirectTests
{
    private const string Start = "https://source.example/start";
    private const string Hosts = "source.example;second.example";

    [Theory]
    [InlineData("https://source.example/next", "https://source.example/next")]
    [InlineData("https://second.example/next", "https://second.example/next")]
    [InlineData("/next", "https://source.example/next")]
    [InlineData("../next", "https://source.example/next")]
    public async Task AllowedRedirectsResolveAndKeepFinalUrl(string location, string expected)
    {
        var handler = new MemoryHandler(uri => uri.AbsoluteUri == Start
            ? Redirect(location) : Ok("safe-source"));
        using var client = new HttpClient(handler);

        var fetched = await AllowedSourceRedirects.SendAsync(client, new Uri(Start), Hosts, CancellationToken.None);
        using var response = fetched.Response;

        Assert.Equal(expected, fetched.FinalUri.AbsoluteUri);
        Assert.Equal(new[] { Start, expected }, handler.Requests);
        Assert.Equal("safe-source", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("https://blocked.example/secret", "allowlisted")]
    [InlineData("http://source.example/insecure", "HTTPS")]
    [InlineData("//blocked.example/secret", "allowlisted")]
    [InlineData("https://user:password@source.example/secret", "Invalid")]
    public async Task UnsafeRedirectIsNeverRequested(string location, string reason)
    {
        var handler = new MemoryHandler(_ => Redirect(location));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<SourceRedirectException>(() =>
            AllowedSourceRedirects.SendAsync(client, new Uri(Start), Hosts, CancellationToken.None));

        Assert.Contains(reason, error.Message);
        Assert.DoesNotContain("secret", error.Message);
        Assert.Equal(new[] { Start }, handler.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://[")]
    public async Task MissingOrInvalidLocationFailsClosed(string? location)
    {
        var handler = new MemoryHandler(_ => Redirect(location));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<SourceRedirectException>(() =>
            AllowedSourceRedirects.SendAsync(client, new Uri(Start), Hosts, CancellationToken.None));

        Assert.Contains("redirect", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task LoopFailsWithoutRevisiting()
    {
        var handler = new MemoryHandler(_ => Redirect(Start));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<SourceRedirectException>(() =>
            AllowedSourceRedirects.SendAsync(client, new Uri(Start), Hosts, CancellationToken.None));

        Assert.Contains("loop", error.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DifferentPathCaseIsNotAFalseLoop()
    {
        const string original = "https://source.example/Doc";
        const string final = "https://source.example/doc";
        var handler = new MemoryHandler(uri => uri.AbsoluteUri == original ? Redirect("/doc") : Ok("document"));
        using var client = new HttpClient(handler);
        var fetched = await AllowedSourceRedirects.SendAsync(client, new Uri(original), Hosts, CancellationToken.None);
        using var response = fetched.Response;

        Assert.Equal(final, fetched.FinalUri.AbsoluteUri);
        Assert.Equal(new[] { original, final }, handler.Requests);
    }

    [Fact]
    public async Task RedirectLimitAllowsFiveButNotSix()
    {
        var handler = new MemoryHandler(uri => Redirect($"/hop{NextHop(uri)}"));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<SourceRedirectException>(() =>
            AllowedSourceRedirects.SendAsync(client, new Uri(Start), Hosts, CancellationToken.None));

        Assert.Contains("limit", error.Message);
        Assert.Equal(AllowedSourceRedirects.MaxRedirects + 1, handler.Requests.Count);
    }

    [Fact]
    public async Task EveryHopIsCheckedBeforeSendingNextRequest()
    {
        var handler = new MemoryHandler(uri => uri.AbsoluteUri == Start
            ? Redirect("https://second.example/step")
            : Redirect("https://blocked.example/private"));
        using var client = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<SourceRedirectException>(() =>
            AllowedSourceRedirects.SendAsync(client, new Uri(Start), Hosts, CancellationToken.None));

        Assert.Contains("allowlisted", error.Message);
        Assert.Equal(new[] { Start, "https://second.example/step" }, handler.Requests);
    }

    [Fact]
    public async Task CallerCancellationIsNotConvertedIntoSourceFailure()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = Research(new MemoryHandler(_ => throw new InvalidOperationException("Must not send")),
            new PublicKnowledgeOptions { AllowedSourceHosts = Hosts });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.RunAsync(DryRun(Start), cancellation.Token));
    }

    [Fact]
    public async Task ResearchIngestionCitesFinalUrlAndRejectsBlockedContent()
    {
        var handler = new MemoryHandler(uri => uri.AbsoluteUri switch
        {
            Start => Redirect("https://second.example/final"),
            "https://second.example/final" => Ok("trusted-content"),
            "https://source.example/blocked" => Redirect("https://blocked.example/private"),
            _ => throw new InvalidOperationException("Unexpected source request")
        });
        var service = Research(handler, new PublicKnowledgeOptions { AllowedSourceHosts = Hosts });
        var result = await service.RunAsync(DryRun(Start, "https://source.example/blocked"), CancellationToken.None);

        Assert.Equal(1, result.SourceCount);
        Assert.Equal(Start, result.Sources[0].Url);
        Assert.Equal("https://second.example/final", result.Sources[0].FinalUrl);
        Assert.False(result.Sources[1].Ok);
        Assert.Null(result.Sources[1].FinalUrl);
        Assert.Contains("allowlisted", result.Sources[1].Note);
        Assert.DoesNotContain("blocked.example", handler.Requests.Select(u => new Uri(u).Host));
    }

    [Fact]
    public async Task RemoteManifestBlockedRedirectFallsBackWithoutReadingContent()
    {
        var manifestUrl = "https://source.example/manifest";
        var handler = new MemoryHandler(uri => uri.AbsoluteUri == manifestUrl
            ? Redirect("https://blocked.example/manifest") : Ok("safe-source"));
        var service = Research(handler, new PublicKnowledgeOptions
        {
            AllowedSourceHosts = Hosts,
            PublicCorpusManifestUrl = manifestUrl
        });

        var result = await service.RunAsync(DryRun(Start), CancellationToken.None);
        Assert.Contains(result.Warnings, warning => warning.Contains("allowlisted", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(manifestUrl, result.RemoteManifestUrl);
        Assert.Null(result.FinalRemoteManifestUrl);
        Assert.DoesNotContain("blocked.example", handler.Requests.Select(u => new Uri(u).Host));
        Assert.True(result.Sources[0].Ok);
    }

    [Fact]
    public async Task RemoteManifestFollowsAllowedRedirectAndSelectsItsSources()
    {
        var manifestUrl = "https://source.example/manifest";
        var handler = new MemoryHandler(uri => uri.AbsoluteUri switch
        {
            "https://source.example/manifest" => Redirect("/manifest-v2"),
            "https://source.example/manifest-v2" => Ok(
                "{\"version\":\"remote\",\"sourceSets\":[{\"urls\":[\"https://second.example/document\"]}]}"),
            "https://second.example/document" => Ok("source-text"),
            _ => throw new InvalidOperationException("Unexpected source request")
        });
        var service = Research(handler, new PublicKnowledgeOptions
        {
            AllowedSourceHosts = Hosts,
            PublicCorpusManifestUrl = manifestUrl
        });

        var result = await service.RunAsync(DryRun(), CancellationToken.None);
        Assert.Single(result.Sources);
        Assert.Equal(manifestUrl, result.RemoteManifestUrl);
        Assert.Equal("https://source.example/manifest-v2", result.FinalRemoteManifestUrl);
        Assert.Equal("https://second.example/document", result.Sources[0].FinalUrl);
        Assert.Equal(new[] { manifestUrl, "https://source.example/manifest-v2", "https://second.example/document" },
            handler.Requests);
    }

    [Fact]
    public async Task LawHealthFollowsAllowedAndBlocksCrossHost()
    {
        const string lawStart = "https://www.flsenate.gov/Laws/Statutes/2025/Chapter117/All";
        var handler = new MemoryHandler(uri => uri.AbsoluteUri == lawStart
            ? Redirect("https://second.example/statute") : Ok("statute"));
        var options = new PublicKnowledgeOptions { AllowedSourceHosts = "www.flsenate.gov;second.example" };
        var service = Index(handler, options);
        var check = Assert.Single((await service.CheckLawSourceHealthAsync("FL", 1, CancellationToken.None)).Checks);
        Assert.True(check.Ok);
        Assert.Equal(lawStart, check.Url);
        Assert.Equal("https://second.example/statute", check.FinalUrl);

        var blocked = Index(new MemoryHandler(_ => Redirect("https://blocked.example/statute")), options);
        var rejected = Assert.Single((await blocked.CheckLawSourceHealthAsync("FL", 1, CancellationToken.None)).Checks);
        Assert.False(rejected.Ok);
        Assert.Null(rejected.FinalUrl);
        Assert.Contains("allowlisted", rejected.Note);
    }

    [Fact]
    public async Task CacheStatusTracksFinalManifestAndBlocksUnsafeRedirect()
    {
        const string cache = "https://source.example/law-source-cache/source-cache-manifest.json";
        var handler = new MemoryHandler(uri => uri.AbsoluteUri == cache
            ? Redirect("https://second.example/cache")
            : Ok("{\"schema\":\"test\",\"sources\":[],\"rules\":{\"refreshAfterDays\":14}}"));
        var options = new PublicKnowledgeOptions { PublicBaseUrl = "https://source.example", AllowedSourceHosts = Hosts };
        var report = await Index(handler, options).BuildPublishedLawSourceCacheStatusAsync(null, 1, CancellationToken.None);
        Assert.True(report.Reachable);
        Assert.Equal(cache, report.ManifestUrl);
        Assert.Equal("https://second.example/cache", report.FinalManifestUrl);

        var blockedHandler = new MemoryHandler(_ => Redirect("http://source.example/cache"));
        var rejected = await Index(blockedHandler, options).BuildPublishedLawSourceCacheStatusAsync(null, 1, CancellationToken.None);
        Assert.False(rejected.Reachable);
        Assert.Null(rejected.FinalManifestUrl);
        Assert.Contains("HTTPS", rejected.Status);
        Assert.Single(blockedHandler.Requests);
    }

    private static int NextHop(Uri uri) => uri.AbsolutePath == "/start"
        ? 1 : int.Parse(uri.AbsolutePath[4..]) + 1;

    private static PublicKnowledgeRunCommand DryRun(params string[] urls) =>
        new(false, false, "redirect test", urls, null, null);

    private static PublicKnowledgeResearchService Research(MemoryHandler handler, PublicKnowledgeOptions options) =>
        new(new MemoryFactory(handler), Options.Create(options), Options.Create(new OpenAiOptions()),
            Options.Create(new StraicoOptions()), NullLogger<PublicKnowledgeResearchService>.Instance);

    private static PublicKnowledgeSourceIndexService Index(MemoryHandler handler, PublicKnowledgeOptions options) =>
        new(new MemoryFactory(handler), Options.Create(options), NullLogger<PublicKnowledgeSourceIndexService>.Instance);

    private static HttpResponseMessage Redirect(string? location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        if (location is not null)
        {
            response.Headers.TryAddWithoutValidation("Location", location);
        }
        return response;
    }

    private static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(text)
    };

    private sealed class MemoryFactory(MemoryHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class MemoryHandler(Func<Uri, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri.AbsoluteUri);
            return Task.FromResult(responder(uri));
        }
    }
}
