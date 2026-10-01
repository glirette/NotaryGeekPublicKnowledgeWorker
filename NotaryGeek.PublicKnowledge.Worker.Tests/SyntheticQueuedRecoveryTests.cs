using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure;
using NotaryGeek.PublicKnowledge.Worker.Services;
using static NotaryGeek.PublicKnowledge.Worker.Tests.SyntheticQueuedFixture;

namespace NotaryGeek.PublicKnowledge.Worker.Tests;

public sealed class SyntheticQueuedRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentDuplicateWorkersAndFreshReplayHaveOneProviderCall(bool providerFails)
    {
        using var f = new SyntheticQueuedFixture(); f.ProviderFails = providerFails;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.ProviderBarrier = async token => { entered.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10), token); };
        var m = f.Message(); await f.Storage().CreateQueuedRunAsync(m, default);
        var owner = f.Worker().ProcessQueuedBatch(m, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Worker().ProcessQueuedBatch(m, default)));
        Assert.Equal(1, f.ProviderCalls);
        release.SetResult(); await owner;
        await f.Worker().ProcessQueuedBatch(m, default);
        Assert.Equal(1, f.ProviderCalls);
        var job = (await f.Storage().ReadQueuedRunAsync(m.JobId, default))!;
        var receipt = Assert.Single(job.Receipts);
        Assert.Equal(!providerFails, receipt.Ok); Assert.True(receipt.OpenAiCalled);
        Assert.Equal(providerFails ? "completed-with-errors" : "completed", job.Status);
        Assert.Equal(PublicKnowledgeRunStorageService.GetCaseEvidenceBlobName(m, "case-a"), receipt.BlobName);
        Assert.NotNull(await f.Storage().ReadStoredRunAsync(receipt.BlobName, default));
    }

    [Theory]
    [InlineData("reservation", 0, false)]
    [InlineData("archive", 1, true)]
    [InlineData("phase", 1, true)]
    [InlineData("latest-pointer", 1, true)]
    [InlineData("receipt", 1, true)]
    [InlineData("candidate", 1, true)]
    [InlineData("publication-marker", 1, true)]
    public async Task LostCommittedWriteAcknowledgementIsReconciledByFreshInvocation(string point, int calls, bool completed)
    {
        using var f = new SyntheticQueuedFixture();
        var m = f.Message(point == "candidate" ? "DailySourceIngestion" : "Core");
        await f.Storage().CreateQueuedRunAsync(m, default);
        var fired = 0;
        f.Store.After = (r, token) =>
        {
            var matches = r.IsWrite && (point switch
            {
                "reservation" => r.Name.StartsWith("runs/executions/") && r.Header("If-None-Match") == "*",
                "archive" => r.Name == PublicKnowledgeRunStorageService.GetCaseEvidenceBlobName(m, "case-a"),
                "phase" => r.Name.StartsWith("runs/executions/") && Encoding.UTF8.GetString(r.Body).Contains("evidence-recorded"),
                "latest-pointer" => r.Name == "runs/latest/case-a.json",
                "receipt" => r.Name.StartsWith("runs/jobs/") && JsonNode.Parse(r.Body)?["receipts"]?.AsArray().Count > 0,
                "candidate" => r.Name.StartsWith("promotion/candidates/"),
                _ => r.Name.StartsWith("runs/executions/") && JsonNode.Parse(r.Body)?["candidatesPublished"]?.GetValue<bool>() == true
            });
            if (matches && Interlocked.CompareExchange(ref fired, 1, 0) == 0)
                throw new IOException("Synthetic lost committed write acknowledgement.");
            return Task.CompletedTask;
        };
        Assert.NotNull(await Record.ExceptionAsync(() => f.Worker().ProcessQueuedBatch(m, default)));
        Assert.Equal(1, fired); f.Store.After = null;
        var committed = f.Store.Snapshot();
        await f.Worker().ProcessQueuedBatch(m, default);
        await f.Worker().ProcessQueuedBatch(m, default);
        Assert.Equal(calls, f.ProviderCalls);
        var job = (await f.Storage().ReadQueuedRunAsync(m.JobId, default))!;
        Assert.Equal(completed ? "completed" : "running", job.Status);
        if (completed)
        {
            var receipt = Assert.Single(job.Receipts); Assert.True(receipt.Ok);
            Assert.Equal(committed[receipt.BlobName].Body, f.Store.Snapshot()[receipt.BlobName].Body);
        }
        else
        {
            Assert.Empty(job.Receipts);
            var reservation = Assert.Single(committed, x => x.Key.StartsWith("runs/executions/"));
            Assert.Contains("provider-outcome-unknown", Encoding.UTF8.GetString(reservation.Value.Body));
        }
    }

    [Theory]
    [InlineData("promotion/candidates/")]
    [InlineData("runs/latest-index.json")]
    [InlineData("runs/latest-needs-greg.json")]
    public async Task InterruptedDerivedPublicationPreservesSuccessfulEvidenceAndRecoversWithoutProvider(string point)
    {
        using var f = new SyntheticQueuedFixture(); var m = f.Message("DailySourceIngestion");
        await f.Storage().CreateQueuedRunAsync(m, default);
        f.Store.Fault = r => r.IsWrite && r.Name.StartsWith(point) ? SyntheticBlobStore.Error(503, "SyntheticUnavailable") : null;
        await Assert.ThrowsAsync<RequestFailedException>(() => f.Worker().ProcessQueuedBatch(m, default));
        Assert.Equal(1, f.ProviderCalls);
        var partial = (await f.Storage().ReadQueuedRunAsync(m.JobId, default))!;
        Assert.Equal("publishing", partial.Status); Assert.True(Assert.Single(partial.Receipts).Ok);
        var original = f.Store.Snapshot()[partial.Receipts[0].BlobName].Body;
        // Another interruption must retain the exact archive and receipt.
        await Assert.ThrowsAsync<RequestFailedException>(() => f.Worker().ProcessQueuedBatch(m, default));
        Assert.Equal(1, f.ProviderCalls); f.Store.Fault = null;
        await f.Worker().ProcessQueuedBatch(m, default);
        var job = (await f.Storage().ReadQueuedRunAsync(m.JobId, default))!;
        Assert.Equal("completed", job.Status); Assert.True(Assert.Single(job.Receipts).Ok);
        Assert.Equal(original, f.Store.Snapshot()[job.Receipts[0].BlobName].Body);
        Assert.Equal(1, f.ProviderCalls);
        Assert.Single(f.Store.Snapshot().Keys, x => x.StartsWith("promotion/candidates/"));
        Assert.Contains("runs/latest-index.json", f.Store.Snapshot().Keys);
        Assert.Contains("runs/latest-needs-greg.json", f.Store.Snapshot().Keys);
    }

    [Theory]
    [InlineData("admitted", null, false)]
    [InlineData("evidence-recorded", null, false)]
    [InlineData("provider-outcome-unknown", "runs/wrong.json", false)]
    [InlineData("provider-outcome-unknown", null, true)]
    [InlineData("unrecognized", null, false)]
    public async Task ContradictoryReservationCannotAuthorizeExecutionOrPublication(string phase, string? evidence, bool published)
    {
        using var f = new SyntheticQueuedFixture(); var m = f.Message();
        await f.Storage().CreateQueuedRunAsync(m, default);
        await f.Storage().AdmitCaseAsync(m, Case(), default);
        var reservation = Assert.Single(f.Store.Snapshot(), x => x.Key.StartsWith("runs/executions/"));
        var state = JsonNode.Parse(reservation.Value.Body)!;
        state["phase"] = phase; state["evidenceBlobName"] = evidence; state["digestPublished"] = published;
        f.Store.Seed(reservation.Key, state.ToJsonString());
        await Record.ExceptionAsync(() => f.Worker().ProcessQueuedBatch(m, default));
        Assert.Equal(0, f.ProviderCalls);
        var job = (await f.Storage().ReadQueuedRunAsync(m.JobId, default))!;
        Assert.Empty(job.Receipts);
        Assert.DoesNotContain("runs/latest-index.json", f.Store.Snapshot().Keys);
        Assert.NotNull(await Record.ExceptionAsync(() => f.Storage().AdmitCaseAsync(m, Case(), default)));
    }

    [Fact]
    public async Task PartialLegacyFanoutAndDuplicateChildrenHaveOneCallPerCase()
    {
        using var f = new SyntheticQueuedFixture(Case(), Case("case-b")); var m = f.Message(legacy: true);
        await f.Storage().CreateQueuedRunAsync(m, default);
        var sent = 0;
        f.Store.After = (r, token) =>
        {
            if (r.Method == "POST" && Interlocked.Increment(ref sent) == 1) throw new IOException("Synthetic lost child ack.");
            return Task.CompletedTask;
        };
        Assert.NotNull(await Record.ExceptionAsync(() => f.Worker().ProcessQueuedBatch(m, default)));
        Assert.Single(f.Store.QueueBodies); f.Store.After = null;
        await f.Worker().ProcessQueuedBatch(m, default); Assert.Equal(3, f.Store.QueueBodies.Count);
        var children = f.Store.QueueBodies.Select(x => JsonSerializer.Deserialize<NotaryGeek.PublicKnowledge.Worker.Models.PublicKnowledgeQueuedRunMessage>(x, Json)!).ToArray();
        // Sequential delivery across fresh workers challenges restart recovery without racing legacy migration.
        foreach (var child in children) await f.Worker().ProcessQueuedBatch(child, default);
        foreach (var child in children) await f.Worker().ProcessQueuedBatch(child, default);
        Assert.Equal(2, f.ProviderCalls);
        var job = (await f.Storage().ReadQueuedRunAsync(m.JobId, default))!;
        Assert.Equal(2, job.Receipts.Count); Assert.All(job.Receipts, x => Assert.True(x.Ok));
        Assert.Equal("completed", job.Status); Assert.True(job.LegacyFanOutReady);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdmittedLegacyEvidenceCanRecoverInterruptedDerivedPublication(bool fanout)
    {
        using var f = fanout ? new SyntheticQueuedFixture(Case(), Case("case-b")) : new SyntheticQueuedFixture();
        var parent = f.Message(legacy: true);
        await f.Storage().CreateQueuedRunAsync(parent, default);
        if (fanout) await f.Worker().ProcessQueuedBatch(parent, default);
        var child = parent with { CaseId = "case-a" };
        f.Store.Fault = r => r.IsWrite && r.Name == "runs/latest-index.json"
            ? SyntheticBlobStore.Error(503, "SyntheticUnavailable") : null;
        await Assert.ThrowsAsync<RequestFailedException>(() => f.Worker().ProcessQueuedBatch(child, default));
        var partial = (await f.Storage().ReadQueuedRunAsync(parent.JobId, default))!;
        Assert.True(Assert.Single(partial.Receipts).Ok);
        var original = f.Store.Snapshot()[partial.Receipts[0].BlobName].Body;
        f.Store.Fault = null; await f.Worker().ProcessQueuedBatch(child, default);
        Assert.Equal(1, f.ProviderCalls);
        var recovered = (await f.Storage().ReadQueuedRunAsync(parent.JobId, default))!;
        Assert.Contains("case-a", recovered.PublishedCaseIds ?? []);
        Assert.Equal(original, f.Store.Snapshot()[recovered.Receipts[0].BlobName].Body);
        Assert.Contains("runs/latest-index.json", f.Store.Snapshot().Keys);
        Assert.Contains("runs/latest-needs-greg.json", f.Store.Snapshot().Keys);
    }

    [Fact]
    public async Task PreexistingUncertainLegacyParentMustNotFanOut()
    {
        using var f = new SyntheticQueuedFixture(Case(), Case("case-b")); var m = f.Message(legacy: true);
        await f.Storage().CreateQueuedRunAsync(m, default);
        var job = Assert.Single(f.Store.Snapshot()); var state = JsonNode.Parse(job.Value.Body)!;
        state["status"] = "running"; f.Store.Seed(job.Key, state.ToJsonString());
        await f.Worker().ProcessQueuedBatch(m, default);
        Assert.Empty(f.Store.QueueBodies); Assert.Equal(0, f.ProviderCalls);
        Assert.False((await f.Storage().ReadQueuedRunAsync(m.JobId, default))!.LegacyFanOutReady);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("completed-with-errors")]
    [InlineData("failed")]
    [InlineData("unrecognized")]
    public async Task ContradictoryQueuedEnvelopeCannotCreateFreshAdmission(string status)
    {
        using var f = new SyntheticQueuedFixture(); var m = f.Message();
        await f.Storage().CreateQueuedRunAsync(m, default);
        var job = Assert.Single(f.Store.Snapshot());
        var state = JsonNode.Parse(job.Value.Body)!; state["status"] = status;
        f.Store.Seed(job.Key, state.ToJsonString());
        await Record.ExceptionAsync(() => f.Worker().ProcessQueuedBatch(m, default));
        Assert.Equal(0, f.ProviderCalls);
        Assert.DoesNotContain(f.Store.Snapshot().Keys, name => name.StartsWith("runs/executions/"));
        Assert.Equal(status, (await f.Storage().ReadQueuedRunAsync(m.JobId, default))!.Status);
    }

    [Fact]
    public async Task DistinctCasesExecuteIndependentlyWhileDuplicatesRemainBlocked()
    {
        using var f = new SyntheticQueuedFixture(Case(), Case("case-b")); var parent = f.Message();
        await f.Storage().CreateQueuedRunAsync(parent, default);
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        f.ProviderBarrier = async token =>
        {
            if (Interlocked.Increment(ref count) == 2) bothEntered.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
        };
        var children = parent.CaseIds.Select(id => parent with { CaseId = id }).ToArray();
        var owners = children.Select(child => f.Worker().ProcessQueuedBatch(child, default)).ToArray();
        await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        foreach (var child in children) await f.Worker().ProcessQueuedBatch(child, default);
        Assert.Equal(2, f.ProviderCalls); release.SetResult(); await Task.WhenAll(owners);
        foreach (var child in children) await f.Worker().ProcessQueuedBatch(child, default);
        var job = (await f.Storage().ReadQueuedRunAsync(parent.JobId, default))!;
        Assert.Equal("completed", job.Status); Assert.Equal(2, job.Receipts.Count);
        Assert.All(job.Receipts, receipt => Assert.True(receipt.Ok));
        Assert.Equal(2, job.Receipts.Select(receipt => receipt.BlobName).Distinct().Count());
        Assert.Equal(2, f.ProviderCalls);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{truncated")]
    public async Task MalformedReservationAndArchiveCannotTriggerProviderReplay(string body)
    {
        using var f = new SyntheticQueuedFixture(); var m = f.Message();
        await f.Storage().CreateQueuedRunAsync(m, default); await f.Storage().AdmitCaseAsync(m, Case(), default);
        var reservation = Assert.Single(f.Store.Snapshot(), x => x.Key.StartsWith("runs/executions/"));
        f.Store.Seed(reservation.Key, body);
        Assert.NotNull(await Record.ExceptionAsync(() => f.Worker().ProcessQueuedBatch(m, default)));
        Assert.Equal(0, f.ProviderCalls); Assert.Equal(body, Encoding.UTF8.GetString(f.Store.Snapshot()[reservation.Key].Body));
        using var other = new SyntheticQueuedFixture(); var second = other.Message();
        await other.Storage().CreateQueuedRunAsync(second, default); await other.Storage().AdmitCaseAsync(second, Case(), default);
        var archive = PublicKnowledgeRunStorageService.GetCaseEvidenceBlobName(second, "case-a");
        other.Store.Seed(archive, body);
        Assert.NotNull(await Record.ExceptionAsync(() => other.Worker().ProcessQueuedBatch(second, default)));
        Assert.Equal(0, other.ProviderCalls); Assert.Equal(body, Encoding.UTF8.GetString(other.Store.Snapshot()[archive].Body));
        Assert.Empty((await other.Storage().ReadQueuedRunAsync(second.JobId, default))!.Receipts);
    }

    [Fact]
    public async Task UnreadableOtherLatestCaseKeepsWorkerPublicationPending()
    {
        using var f = new SyntheticQueuedFixture(); var m = f.Message();
        await f.Storage().CreateQueuedRunAsync(m, default);
        f.Store.Seed("runs/latest/other.json", "null");
        Assert.NotNull(await Record.ExceptionAsync(() => f.Worker().ProcessQueuedBatch(m, default)));
        var job = (await f.Storage().ReadQueuedRunAsync(m.JobId, default))!;
        Assert.True(Assert.Single(job.Receipts).Ok); Assert.Equal("publishing", job.Status);
        Assert.Equal(1, f.ProviderCalls);
        Assert.NotNull(await Record.ExceptionAsync(() => f.Worker().ProcessQueuedBatch(m, default)));
        Assert.Equal(1, f.ProviderCalls); Assert.Equal("publishing", (await f.Storage().ReadQueuedRunAsync(m.JobId, default))!.Status);
    }

    [Fact]
    public async Task CancelledProviderAndPrecommitArchiveFailureNeverRepeatUncertainCall()
    {
        using var f = new SyntheticQueuedFixture(); var m = f.Message();
        await f.Storage().CreateQueuedRunAsync(m, default);
        using var cancel = new CancellationTokenSource();
        f.ProviderBarrier = token => { cancel.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; };
        Assert.NotNull(await Record.ExceptionAsync(() => f.Worker().ProcessQueuedBatch(m, cancel.Token)));
        f.ProviderBarrier = null; await f.Worker().ProcessQueuedBatch(m, default);
        Assert.Equal(1, f.ProviderCalls); Assert.Empty((await f.Storage().ReadQueuedRunAsync(m.JobId, default))!.Receipts);
        using var other = new SyntheticQueuedFixture(); var second = other.Message();
        await other.Storage().CreateQueuedRunAsync(second, default);
        other.Store.Before = (r, token) =>
        {
            if (r.IsWrite && r.Name == PublicKnowledgeRunStorageService.GetCaseEvidenceBlobName(second, "case-a"))
                throw new IOException("Synthetic interruption before atomic commit.");
            return Task.CompletedTask;
        };
        Assert.NotNull(await Record.ExceptionAsync(() => other.Worker().ProcessQueuedBatch(second, default)));
        other.Store.Before = null; await other.Worker().ProcessQueuedBatch(second, default);
        Assert.Equal(1, other.ProviderCalls); Assert.Empty((await other.Storage().ReadQueuedRunAsync(second.JobId, default))!.Receipts);
        Assert.DoesNotContain(PublicKnowledgeRunStorageService.GetCaseEvidenceBlobName(second, "case-a"), other.Store.Snapshot().Keys);
    }
}
