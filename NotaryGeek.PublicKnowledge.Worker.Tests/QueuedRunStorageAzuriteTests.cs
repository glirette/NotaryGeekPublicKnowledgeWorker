using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotaryGeek.PublicKnowledge.Worker.Configuration;
using NotaryGeek.PublicKnowledge.Worker.Models;
using NotaryGeek.PublicKnowledge.Worker.Services;
using Azure.Storage.Blobs;

namespace NotaryGeek.PublicKnowledge.Worker.Tests;

/// <summary>
/// Run with PK_TEST_STORAGE_CONNECTION=UseDevelopmentStorage=true and a disposable Azurite instance.
/// These tests exercise the real Blob SDK's conditional writes; without that opt-in they skip.
/// </summary>
public sealed class QueuedRunStorageAzuriteTests
{
    private static readonly PublicKnowledgeRegressionCase Case = new(
        "synthetic-case", "public fixture", "storage contract", ["must hold"], [],
        ["https://example.invalid/public-fixture"]);

    [AzuriteFact]
    public async Task ConcurrentAdmissionPermitsOneCallerAndReservesUnknownOutcome()
    {
        await WithStorageAsync(async storage =>
        {
            var message = Message();
            await storage.CreateQueuedRunAsync(message, CancellationToken.None);
            var attempts = await Task.WhenAll(Enumerable.Range(0, 12)
                .Select(_ => storage.AdmitCaseAsync(message with { CaseId = Case.Id }, Case, CancellationToken.None)));
            Assert.Single(attempts, item => item.Phase == "admitted");
            Assert.Equal(11, attempts.Count(item => item.Phase == "provider-outcome-unknown"));

            // A new worker sees uncertainty and cannot acquire another provider admission.
            var replay = await storage.AdmitCaseAsync(message with { CaseId = Case.Id }, Case, CancellationToken.None);
            Assert.Equal("provider-outcome-unknown", replay.Phase);
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.AdmitCaseAsync(
                message with { CaseId = Case.Id, ProviderOverride = "different-provider" }, Case, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.AdmitCaseAsync(
                message with { CaseId = Case.Id, SubmittedAtUtc = message.SubmittedAtUtc.AddMinutes(1) },
                Case, CancellationToken.None));
        });
    }

    [AzuriteFact]
    public async Task ImmutableArchiveReconcilesReplayAndRejectsConflictingResult()
    {
        await WithStorageAsync(async storage =>
        {
            var started = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
            var result = Result("first");
            var first = await storage.SaveAsync(result, "queue", "Core", started, CancellationToken.None, "job:case");
            var replay = await storage.SaveAsync(result, "queue", "Core", started, CancellationToken.None, "job:case");
            Assert.Equal(first.BlobName, replay.BlobName);
            var stored = await storage.ReadStoredRunAsync(first.BlobName, CancellationToken.None);
            Assert.Equal("first", stored?.Result.Status);

            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SaveAsync(
                Result("conflicting"), "queue", "Core", started, CancellationToken.None, "job:case"));
            Assert.Equal("first", (await storage.ReadStoredRunAsync(first.BlobName, CancellationToken.None))?.Result.Status);
        });
    }

    [AzuriteFact]
    public async Task OlderCompletionAndSameSecondArchivesCannotReplaceNewerLatest()
    {
        await WithStorageAsync(async storage =>
        {
            var firstTime = new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc);
            var laterTime = firstTime.AddTicks(1);
            var newer = await storage.SaveAsync(Result("newer"), "queue", "Core", laterTime,
                CancellationToken.None, "job-new:case");
            var older = await storage.SaveAsync(Result("older"), "queue", "Core", firstTime,
                CancellationToken.None, "job-old:case");
            Assert.NotEqual(newer.BlobName, older.BlobName);
            Assert.Equal("older", (await storage.ReadStoredRunAsync(older.BlobName, CancellationToken.None))?.Result.Status);
            Assert.Equal("newer", (await storage.ReadLatestAsync(Case.Id, CancellationToken.None))?.Result.Status);
        });
    }

    [AzuriteFact]
    public async Task CompletedReceiptSurvivesDuplicateAndLateFailure()
    {
        await WithStorageAsync(async storage =>
        {
            var message = Message() with { CaseId = Case.Id };
            await storage.CreateQueuedRunAsync(message, CancellationToken.None);
            var receipt = await storage.SaveAsync(Result("completed"), "queue", "Core",
                message.SubmittedAtUtc, CancellationToken.None,
                PublicKnowledgeRunStorageService.GetCaseExecutionId(message, Case.Id));
            var completed = await storage.CompleteQueuedRunAsync(message, [receipt], CancellationToken.None);
            Assert.True(Assert.Single(completed.Receipts).Ok);

            await storage.FailQueuedRunCaseAsync(message, "synthetic later index failure", CancellationToken.None);
            var replay = await storage.CompleteQueuedRunAsync(message, [receipt], CancellationToken.None);
            Assert.True(Assert.Single(replay.Receipts).Ok);
            Assert.Equal("publishing", replay.Status);
            await storage.MarkQueuedCasePublishedAsync(message, Case.Id, CancellationToken.None);
            Assert.Equal("completed", (await storage.ReadQueuedRunAsync(message.JobId, CancellationToken.None))?.Status);
        });
    }

    private static PublicKnowledgeQueuedRunMessage Message() =>
        new(Guid.NewGuid().ToString("N"), "Core", "queue", true, [Case.Id],
            new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc));

    private static PublicKnowledgeRunResult Result(string status) =>
        new(true, true, true, false, status, new DateTime(2026, 9, 26, 12, 0, 0, DateTimeKind.Utc),
            Case.Focus, Case.Id, Case, 0, 0, 0, "synthetic-model", [], null, null,
            null, [], [], RunKind: "regression");

    private static async Task WithStorageAsync(Func<PublicKnowledgeRunStorageService, Task> test)
    {
        var connection = Environment.GetEnvironmentVariable("PK_TEST_STORAGE_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Azurite connection must be present when an AzuriteFact is executed.");

        var containerName = $"pk-test-{Guid.NewGuid():N}";
        var options = new PublicKnowledgeOptions { OutputContainerName = containerName };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [options.OutputStorageConnectionStringSetting] = connection
        }).Build();
        var client = new BlobContainerClient(connection, containerName);
        var storage = new PublicKnowledgeRunStorageService(config, Options.Create(options),
            NullLogger<PublicKnowledgeRunStorageService>.Instance);
        try { await test(storage); }
        finally { await client.DeleteIfExistsAsync(); }
    }
}

public sealed class AzuriteFactAttribute : FactAttribute
{
    public AzuriteFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PK_TEST_STORAGE_CONNECTION")))
            Skip = "Requires disposable Azurite via PK_TEST_STORAGE_CONNECTION.";
    }
}
