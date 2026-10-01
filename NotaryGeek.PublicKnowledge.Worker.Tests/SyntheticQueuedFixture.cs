using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotaryGeek.PublicKnowledge.Worker.Configuration;
using NotaryGeek.PublicKnowledge.Worker.Functions;
using NotaryGeek.PublicKnowledge.Worker.Models;
using NotaryGeek.PublicKnowledge.Worker.Services;

namespace NotaryGeek.PublicKnowledge.Worker.Tests;

internal sealed class SyntheticQueuedFixture : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static readonly DateTime Time = new(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
    public static PublicKnowledgeRegressionCase Case(string id = "case-a") =>
        new(id, "public fixture", "storage contract", ["source checked"], [], ["https://source.invalid/document"]);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "w02-" + Guid.NewGuid().ToString("N"));
    private readonly IOptions<PublicKnowledgeOptions> _options;
    private readonly IConfiguration _configuration = new ConfigurationBuilder().Build(); // Never loads environment or cloud credentials.
    private readonly ProviderHandler _handler;
    public SyntheticBlobStore Store { get; } = new();
    public IReadOnlyList<PublicKnowledgeRegressionCase> Cases { get; }
    public int ProviderCalls => _handler.Calls;
    public Func<CancellationToken, Task>? ProviderBarrier { get => _handler.Barrier; set => _handler.Barrier = value; }
    public bool ProviderFails { get => _handler.Fail; set => _handler.Fail = value; }
    public SyntheticQueuedFixture(params PublicKnowledgeRegressionCase[] cases)
    {
        Cases = cases.Length == 0 ? [Case()] : cases;
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "matrix.json"), JsonSerializer.Serialize(new { cases = Cases }, Json));
        File.WriteAllText(Path.Combine(_directory, "manifest.json"), "{\"sourceSets\":[]}");
        _options = Options.Create(new PublicKnowledgeOptions
        {
            Enabled = true, LocalRegressionMatrixPath = Path.Combine(_directory, "matrix.json"),
            LocalManifestPath = Path.Combine(_directory, "manifest.json"), AllowedSourceHosts = "source.invalid",
            OutputContainerName = "fixture", PublicBaseUrl = "https://source.invalid", PublicCorpusManifestUrl = ""
        });
        _handler = new ProviderHandler();
    }
    public PublicKnowledgeRunStorageService Storage() => new(_configuration, _options,
        NullLogger<PublicKnowledgeRunStorageService>.Instance, Store.Client());
    public PublicKnowledgePromotionService Promotion() => new(_configuration, _options, Store.Client());
    public PublicKnowledgeResearchFunction Worker() => new(
        new PublicKnowledgeResearchService(new Factory(_handler), _options,
            Options.Create(new OpenAiOptions { BaseUrl = "https://provider.invalid", PublicSourceApiKey = "synthetic-only" }),
            Options.Create(new StraicoOptions()), NullLogger<PublicKnowledgeResearchService>.Instance),
        Storage(), new PublicKnowledgeQueueService(_configuration, _options, Store.QueueClient()), Promotion(), _options,
        NullLogger<PublicKnowledgeResearchFunction>.Instance);
    public PublicKnowledgeQueuedRunMessage Message(string batch = "Core", bool legacy = false) =>
        new("job-" + Guid.NewGuid().ToString("N"), batch, "queued-batch", true, Cases.Select(x => x.Id).ToArray(), Time,
            Cases.Count == 1 ? Cases[0].Id : null, RunKind: batch == "Core" ? "regression" : "authority-generation",
            CaseFingerprints: legacy ? null : Cases.ToDictionary(x => x.Id, PublicKnowledgeRunStorageService.FingerprintCase));
    public static PublicKnowledgeRunResult Result(string status = "completed", string id = "case-a", DateTime? time = null) =>
        new(true, true, true, false, status, time ?? Time, Case(id).Focus, id, Case(id), 1, 10, 3, "synthetic-model",
            [], null, "completed", null, [], []);
    public void Dispose() { Store.Dispose(); _handler.Dispose(); Directory.Delete(_directory, true); }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class ProviderHandler : HttpMessageHandler
    {
        public int Calls;
        public bool Fail;
        public Func<CancellationToken, Task>? Barrier;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (request.RequestUri!.AbsoluteUri == "https://source.invalid/document" && request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = new StringContent("source checked; public synthetic fixture") };
            if (request.RequestUri.AbsoluteUri != "https://provider.invalid/v1/responses" || request.Method != HttpMethod.Post ||
                request.Headers.Authorization?.Parameter != "synthetic-only")
                throw new InvalidOperationException("Unexpected source/provider request.");
            Interlocked.Increment(ref Calls);
            if (Barrier is not null) await Barrier(token);
            if (Fail) return new(HttpStatusCode.InternalServerError) { Content = new StringContent("synthetic unavailable") };
            var now = DateTime.UtcNow;
            var output = new PublicKnowledgeStructuredOutput("source checked", [], [], [], [], [], [],
                ["https://source.invalid/document"], [new("synthetic-topic", "Public fixture", "source checked", now, true,
                    [new("https://source.invalid/document", "Fixture", "Synthetic publisher", "official-agency-page", now, "source checked")],
                    ["source checked"], ["no real-service conclusion"])]);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            {
                status = "completed", model = "synthetic-model", output_text = JsonSerializer.Serialize(output, Json),
                usage = new { input_tokens = 3, output_tokens = 4 }
            }, Json)) };
        }
    }
}
