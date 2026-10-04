using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotaryGeek.PublicKnowledge.Worker.Configuration;
using NotaryGeek.PublicKnowledge.Worker.Models;
using NotaryGeek.PublicKnowledge.Worker.Services;

namespace NotaryGeek.PublicKnowledge.Worker.Tests.W20SourceContracts;

internal static class Check
{
    public static void That(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static async Task Cancelled(Func<Task> action)
    {
        try { await action(); } catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Cancellation became success");
    }
}

internal sealed class SyntheticSources : IDisposable
{
    public readonly ConcurrentQueue<string> Requests = new();
    public readonly ConcurrentQueue<string> Names = new();
    public readonly ConcurrentQueue<(string Name, bool AutoRedirect)> Constructed = new();
    public readonly ConcurrentBag<TrackingContent> Bodies = [];
    private readonly ServiceProvider services;
    private readonly string directory = Path.Combine(Path.GetTempPath(), "w20-" + Guid.NewGuid().ToString("N"));
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond { get; set; }
        = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("synthetic") });
    public PublicKnowledgeOptions Options { get; }
    public IHttpClientFactory Factory => services.GetRequiredService<IHttpClientFactory>();

    public SyntheticSources()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), "{\"sourceSets\":[]}");
        File.WriteAllText(Path.Combine(directory, "law.json"), """
            {"jurisdictions":[{"id":"synthetic","state":"ZZ","sources":[
            {"id":"synthetic-law","url":"https://source.example/law","title":"Synthetic","publisher":"Fixture","sourceType":"synthetic"}]}]}
            """);
        Options = new PublicKnowledgeOptions
        {
            AllowedSourceHosts = ContractOracle.Hosts, PublicBaseUrl = "https://source.example",
            LocalManifestPath = Path.Combine(directory, "manifest.json"),
            LocalLawSourceIndexPath = Path.Combine(directory, "law.json"),
            SourceFetchConcurrency = 4, MaxSourcesPerRun = 100
        };
        var registrations = new ServiceCollection();
        registrations.AddHttpClient(); // same default registration left in Program.cs
        registrations.AddPublicKnowledgeSourceHttpClients();
        registrations.AddSingleton<IHttpMessageHandlerBuilderFilter>(new InspectThenSubstitute(this));
        services = registrations.BuildServiceProvider();
    }

    public PublicKnowledgeResearchService Research() => new(Factory,
        Microsoft.Extensions.Options.Options.Create(Options), Microsoft.Extensions.Options.Options.Create(new OpenAiOptions()),
        Microsoft.Extensions.Options.Options.Create(new StraicoOptions()), NullLogger<PublicKnowledgeResearchService>.Instance);
    public PublicKnowledgeSourceIndexService Index() => new(Factory,
        Microsoft.Extensions.Options.Options.Create(Options), NullLogger<PublicKnowledgeSourceIndexService>.Instance);
    public static PublicKnowledgeRunCommand Command(params string[] urls) => new(false, false, "synthetic W20", urls, null, null);
    public TrackingContent Body(string value, string fault = "ok", Action? started = null)
    {
        var result = new TrackingContent(value, fault, started); Bodies.Add(result); return result;
    }
    public HttpResponseMessage Response(int status, string[] locations, string body = "synthetic")
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = Body(body) };
        if (locations.Length > 0) response.Headers.TryAddWithoutValidation("Location", locations);
        return response;
    }
    public void Dispose() { services.Dispose(); Directory.Delete(directory, true); }

    private sealed class InspectThenSubstitute(SyntheticSources owner) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder); // Run ACTUAL production handler construction before substituting transport.
            bool source = builder.Name is nameof(PublicKnowledgeResearchService) or nameof(PublicKnowledgeSourceIndexService);
            bool automatic = builder.PrimaryHandler switch
            {
                HttpClientHandler h => h.AllowAutoRedirect,
                SocketsHttpHandler h => h.AllowAutoRedirect,
                _ => throw new InvalidOperationException("Unexpected native primary handler type")
            };
            owner.Constructed.Enqueue((builder.Name!, automatic));
            Check.That(source ? !automatic : automatic, $"AutoRedirect mismatch: {builder.Name}={automatic}");
            if (source) Check.That(builder.PrimaryHandler is HttpClientHandler, "Source handler type changed");
            builder.PrimaryHandler.Dispose();
            builder.PrimaryHandler = new Transport(owner, builder.Name!, source);
        };
    }
    private sealed class Transport(SyntheticSources owner, string name, bool allowed) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Check.That(allowed, "Wrong named client selected: " + name);
            token.ThrowIfCancellationRequested();
            owner.Names.Enqueue(name); owner.Requests.Enqueue(request.RequestUri!.AbsoluteUri);
            return owner.Respond(request, token);
        }
    }
}

internal sealed class TrackingContent(string text, string fault, Action? started) : HttpContent
{
    public bool Disposed { get; private set; }
    public int Reads { get; private set; }
    protected override bool TryComputeLength(out long length) { length = 0; return false; }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Write(stream, CancellationToken.None);
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token) => Write(stream, token);
    protected override Task<Stream> CreateContentReadStreamAsync() => fault == "cancel"
        ? Task.FromResult<Stream>(new CancelOnReadStream(() => { Reads++; started?.Invoke(); }))
        : base.CreateContentReadStreamAsync();
    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken token) => fault == "cancel"
        ? CreateContentReadStreamAsync() : base.CreateContentReadStreamAsync(token);
    private sealed class CancelOnReadStream(Action onRead) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            onRead(); token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Synthetic stream read cancellation token was not propagated");
        }
    }
    private async Task Write(Stream stream, CancellationToken token)
    {
        Reads++; started?.Invoke();
        if (fault == "cancel") { await Task.Delay(TimeSpan.FromSeconds(1), token); throw new InvalidOperationException("Synthetic body cancellation token was not propagated"); }
        if (fault == "throw") throw new IOException("synthetic body fault");
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text), token);
        if (fault == "truncate") throw new IOException("synthetic incomplete body");
    }
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
}

internal static class CitationAdmission
{
    private static readonly DateTime Now = new(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);
    public static bool Structured(string fetched, string cited)
    {
        var text = JsonSerializer.Serialize(new
        {
            summary = "Synthetic fixture", citations = new[] { cited },
            candidates = new[] { new { topicId = "synthetic", title = "Synthetic", summary = "Synthetic",
                reviewedAtUtc = Now, recheckBeforeUse = true, supports = new[] { "Synthetic" }, doesNotProve = new[] { "Native transport" },
                sources = new[] { new { url = cited, title = "Synthetic", publisher = "Synthetic", kind = "first-party-documentation", reviewedAtUtc = Now, supports = "Synthetic" } } } }
        });
        return PublicKnowledgeProviderOutput.TryValidate("completed", text,
            new HashSet<string>(StringComparer.Ordinal) { fetched }, Now, 14, out _, out _);
    }
    public static bool Research(string fetched, string cited)
    {
        var method = typeof(PublicKnowledgeResearchService).GetMethod("ValidateProviderResponse", BindingFlags.Static | BindingFlags.NonPublic)!;
        var warnings = (IReadOnlyList<string>)method.Invoke(null,
            [JsonSerializer.Serialize(new { citations = new[] { cited } }), new HashSet<string>(StringComparer.Ordinal) { fetched }])!;
        return warnings.Count == 0;
    }
}
