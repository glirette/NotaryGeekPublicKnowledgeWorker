using System.Net;
using System.Reflection;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotaryGeek.PublicKnowledge.Worker.Configuration;
using NotaryGeek.PublicKnowledge.Worker.Functions;
using NotaryGeek.PublicKnowledge.Worker.Models;
using NotaryGeek.PublicKnowledge.Worker.Services;

namespace NotaryGeek.PublicKnowledge.Worker.Tests;

public sealed class QueuedWorkerOrchestrationTests
{
    [Fact]
    public void TechnicalPromotionUsesPublicSafeReviewLane()
    {
        var method = typeof(PublicKnowledgePromotionService).GetMethod("GetDestination",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal("technical-review", method.Invoke(null, ["technical"]));
        Assert.Equal("glirette/NotaryGeekPublicKnowledgeWorker", method.Invoke(null, ["notary"]));
    }

    [Fact]
    public void OlderOrPartiallyObservedSnapshotsCannotReplaceNewerDerivedView()
    {
        var earlier = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
        var later = earlier.AddMinutes(1);
        var first = new PublicKnowledgeRunStorageService.PublicKnowledgeRunReference("case-a", earlier, "runs/a-old.json");
        var second = new PublicKnowledgeRunStorageService.PublicKnowledgeRunReference("case-b", earlier, "runs/b-old.json");
        var newFirst = first with { StoredAtUtc = later, BlobName = "runs/a-new.json" };
        var newSecond = second with { StoredAtUtc = later, BlobName = "runs/b-new.json" };

        Assert.True(PublicKnowledgeRunStorageService.SnapshotIncludes([newFirst, newSecond], [first, second]));
        Assert.False(PublicKnowledgeRunStorageService.SnapshotIncludes([first, second], [newFirst, newSecond]));
        Assert.False(PublicKnowledgeRunStorageService.SnapshotIncludes([newFirst, second], [first, newSecond]));
        Assert.False(PublicKnowledgeRunStorageService.SnapshotIncludes([first, newSecond], [newFirst, second]));
        Assert.False(PublicKnowledgeRunStorageService.SnapshotIncludes([newFirst], [newFirst, newSecond]));
    }

    [Theory]
    [InlineData("preparing", false)]
    [InlineData("queued", false)]
    [InlineData("running", true)]
    [InlineData("completed", true)]
    [InlineData("failed", true)]
    public void LegacyEnvelopeWithoutReceiptHasConservativeAdmission(string status, bool blocked)
    {
        var before = new PublicKnowledgeQueuedRunEnvelope("legacy", "1", "job", "Core", "queued-batch",
            true, ["case"], DateTime.UtcNow, null, null, null, status, 0, 1, [], null);
        Assert.Equal(blocked, PublicKnowledgeRunStorageService.HasUncertainLegacyExecution(before));
        if (status == "running")
            Assert.False(PublicKnowledgeRunStorageService.HasUncertainLegacyExecution(before with { LegacyFanOutReady = true }));
        Assert.True(PublicKnowledgeRunStorageService.HasUncertainLegacyExecution(null));
    }

    [Fact]
    public async Task QueuedProviderFailureUsesOneSyntheticAttempt()
    {
        var calls = 0;
        var handler = new SyntheticHandler(request =>
        {
            if (request.RequestUri!.Host == "source.example")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("public fixture") };
            Interlocked.Increment(ref calls);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("synthetic unavailable")
            };
        });
        var options = Options.Create(new PublicKnowledgeOptions
        {
            Enabled = true, AllowedSourceHosts = "source.example",
            LocalManifestPath = "missing-synthetic-manifest.json"
        });
        var service = new PublicKnowledgeResearchService(new SyntheticFactory(handler), options,
            Options.Create(new OpenAiOptions { PublicSourceApiKey = "synthetic-only", MaxProviderAttempts = 2 }),
            Options.Create(new StraicoOptions()), NullLogger<PublicKnowledgeResearchService>.Instance);
        var regressionCase = new PublicKnowledgeRegressionCase("synthetic", "public fixture", "public fixture",
            ["must hold"], [], ["https://source.example/document"]);
        var result = await service.RunAsync(new PublicKnowledgeRunCommand(true, false, regressionCase.Focus,
            regressionCase.SourceUrls, regressionCase.Id, regressionCase, QueuedSingleAttempt: true), CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.True(result.OpenAiCalled);
        Assert.False(result.Ok);
    }

    [AzuriteFact]
    public async Task DuplicateAndConcurrentDeliveriesDoNotRepeatAmbiguousProviderResponse()
    {
        var connection = Environment.GetEnvironmentVariable("PK_TEST_STORAGE_CONNECTION")!;
        var caseId = "synthetic-queue-case";
        var regressionCase = new PublicKnowledgeRegressionCase(caseId, "public fixture", "queue fixture",
            ["source checked"], [], ["https://source.example/document"]);
        var matrixName = $"matrix-{Guid.NewGuid():N}.json";
        var matrixPath = Path.Combine(AppContext.BaseDirectory, matrixName);
        await File.WriteAllTextAsync(matrixPath, JsonSerializer.Serialize(new
        {
            schema = "synthetic-matrix", version = "1", cases = new[] { regressionCase }
        }));
        var containerName = $"fixture{Guid.NewGuid():N}";
        var options = new PublicKnowledgeOptions
        {
            Enabled = true, LocalRegressionMatrixPath = matrixName,
            LocalManifestPath = "nonexistent-synthetic-manifest.json",
            AllowedSourceHosts = "source.example", OutputContainerName = containerName
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [options.OutputStorageConnectionStringSetting] = connection
        }).Build();
        var calls = 0;
        var handler = new SyntheticHandler(request =>
        {
            if (request.RequestUri!.Host == "source.example")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("public source") };
            Interlocked.Increment(ref calls);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("synthetic unavailable")
            };
        });
        var knowledge = Options.Create(options);
        var service = new PublicKnowledgeResearchService(new SyntheticFactory(handler), knowledge,
            Options.Create(new OpenAiOptions { PublicSourceApiKey = "synthetic-only", MaxProviderAttempts = 2 }),
            Options.Create(new StraicoOptions()), NullLogger<PublicKnowledgeResearchService>.Instance);
        var storage = new PublicKnowledgeRunStorageService(configuration, knowledge,
            NullLogger<PublicKnowledgeRunStorageService>.Instance);
        var worker = new PublicKnowledgeResearchFunction(service, storage,
            new PublicKnowledgeQueueService(configuration, knowledge),
            new PublicKnowledgePromotionService(configuration, knowledge), knowledge,
            NullLogger<PublicKnowledgeResearchFunction>.Instance);
        var message = new PublicKnowledgeQueuedRunMessage(Guid.NewGuid().ToString("N"), "Core", "queued-batch",
            true, [caseId], DateTime.UtcNow, caseId,
            CaseFingerprints: new Dictionary<string, string>
            {
                [caseId] = PublicKnowledgeRunStorageService.FingerprintCase(regressionCase)
            });
        var container = new BlobContainerClient(connection, containerName);
        try
        {
            await storage.CreateQueuedRunAsync(message, CancellationToken.None);
            await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => worker.ProcessQueuedBatch(message, CancellationToken.None)));
            await worker.ProcessQueuedBatch(message, CancellationToken.None);
            Assert.Equal(1, calls);
            var queued = await storage.ReadQueuedRunAsync(message.JobId, CancellationToken.None);
            Assert.Equal("completed-with-errors", queued?.Status);
            Assert.Single(queued!.Receipts);
            Assert.True(queued.Receipts[0].OpenAiCalled);
            Assert.NotNull(await storage.ReadStoredRunAsync(queued.Receipts[0].BlobName, CancellationToken.None));
        }
        finally
        {
            await container.DeleteIfExistsAsync();
            File.Delete(matrixPath);
        }
    }

    private sealed class SyntheticFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class SyntheticHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
