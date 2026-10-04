using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs.Models;
using NotaryGeek.PublicKnowledge.Worker.Services;
using static NotaryGeek.PublicKnowledge.Worker.Tests.SyntheticQueuedFixture;

namespace NotaryGeek.PublicKnowledge.Worker.Tests;

public sealed class SyntheticQueuedStorageTests
{
    [Fact]
    public async Task SdkRoundtripsBodiesConditionsEtagsAndPagedEnumeration()
    {
        using var f = new SyntheticQueuedFixture();
        var blob = f.Store.Client().GetBlobClient("proof");
        await blob.UploadAsync(BinaryData.FromString("original"), new BlobUploadOptions
        { Conditions = new() { IfNoneMatch = ETag.All } });
        var downloaded = await blob.DownloadContentAsync();
        Assert.Equal("original", downloaded.Value.Content.ToString());
        await blob.UploadAsync(BinaryData.FromString("changed"), new BlobUploadOptions
        { Conditions = new() { IfMatch = downloaded.Value.Details.ETag } });
        var stale = await Assert.ThrowsAsync<RequestFailedException>(() => blob.UploadAsync(BinaryData.FromString("lost"),
            new BlobUploadOptions { Conditions = new() { IfMatch = downloaded.Value.Details.ETag } }));
        Assert.Equal(412, stale.Status);
        Assert.Equal("ConditionNotMet", stale.ErrorCode);
        Assert.Equal("changed", (await blob.DownloadContentAsync()).Value.Content.ToString());
        f.Store.Seed("proof-2", "2"); f.Store.Seed("proof-3", "3");
        var names = new List<string>();
        await foreach (var item in f.Store.Client().GetBlobsAsync(prefix: "proof")) names.Add(item.Name);
        Assert.Equal(new[] { "proof", "proof-2", "proof-3" }, names);
        Assert.Contains(f.Store.Requests, r => r.Query.ContainsKey("marker"));
        Assert.Contains(f.Store.Requests, r => r.Header("If-None-Match") == "*" && Encoding.UTF8.GetString(r.Body) == "original");
    }

    [Fact]
    public async Task ConcurrentAdmissionAndIndependentCasesUseCreateOnlySdkWrites()
    {
        using var f = new SyntheticQueuedFixture(Case(), Case("case-b"));
        var message = f.Message();
        await f.Storage().CreateQueuedRunAsync(message, default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        f.Store.Before = async (r, token) =>
        {
            if (r.IsWrite && r.Name.StartsWith("runs/executions/"))
            {
                if (Interlocked.Increment(ref writes) == 12) entered.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            }
        };
        var attempts = Enumerable.Range(0, 12).Select(_ => f.Storage().AdmitCaseAsync(message with { CaseId = "case-a" }, Case(), default)).ToArray();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); release.SetResult();
        var outcomes = await Task.WhenAll(attempts);
        Assert.Single(outcomes, x => x.Phase == "admitted");
        Assert.Equal(11, outcomes.Count(x => x.Phase == "provider-outcome-unknown"));
        f.Store.Before = null;
        Assert.Equal("admitted", (await f.Storage().AdmitCaseAsync(message with { CaseId = "case-b" }, Case("case-b"), default)).Phase);
        Assert.Equal("provider-outcome-unknown", (await f.Storage().AdmitCaseAsync(message with { CaseId = "case-a" }, Case(), default)).Phase);
        Assert.Equal(2, f.Store.Snapshot().Keys.Count(x => x.StartsWith("runs/executions/")));
        Assert.All(f.Store.Requests.Where(r => r.IsWrite && r.Name.StartsWith("runs/executions/")),
            r => Assert.Equal("*", r.Header("If-None-Match")));
    }

    [Fact]
    public async Task ChangedJobAndCaseParametersCannotReuseIdentity()
    {
        using var f = new SyntheticQueuedFixture();
        var m = f.Message(); await f.Storage().CreateQueuedRunAsync(m, default);
        foreach (var changed in new[] { m with { ProviderOverride = "different" }, m with { SubmittedAtUtc = Time.AddTicks(1) },
            m with { Execute = false }, m with { Batch = "other" }, m with { RunKind = "other" }, m with { AuthorityLane = "other" } })
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Storage().AdmitCaseAsync(changed, Case(), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Storage().AdmitCaseAsync(m, Case() with { Focus = "changed" }, default));
        Assert.DoesNotContain(f.Store.Snapshot().Keys, x => x.StartsWith("runs/executions/"));
    }

    [Fact]
    public async Task ImmutableArchiveExactReplayConflictAndSameSecondOrdering()
    {
        using var f = new SyntheticQueuedFixture();
        var first = await f.Storage().SaveAsync(Result(), "queue", "Core", Time, default, "a");
        var original = f.Store.Snapshot()[first.BlobName];
        var replay = await f.Storage().SaveAsync(Result(), "queue", "Core", Time, default, "a");
        Assert.Equal(first, replay); Assert.Equal(original.ETag, f.Store.Snapshot()[first.BlobName].ETag);
        Assert.Equal(original.Body, f.Store.Snapshot()[first.BlobName].Body);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Storage().SaveAsync(Result("conflict"), "queue", "Core", Time, default, "a"));
        Assert.Equal(original.Body, f.Store.Snapshot()[first.BlobName].Body);
        var newer = await f.Storage().SaveAsync(Result("newer"), "queue", "Core", Time.AddTicks(1), default, "b");
        await f.Storage().SaveAsync(Result("older"), "queue", "Core", Time, default, "c");
        Assert.NotEqual(first.BlobName, newer.BlobName);
        Assert.Equal(newer.BlobName, (await f.Storage().ReadLatestAsync("case-a", default))!.BlobName);
        var tie1 = await f.Storage().SaveAsync(Result("tie-1"), "queue", "Core", Time.AddSeconds(1), default, "d");
        var tie2 = await f.Storage().SaveAsync(Result("tie-2"), "queue", "Core", Time.AddSeconds(1), default, "e");
        Assert.Equal(new[] { tie1.BlobName, tie2.BlobName }.Max(StringComparer.Ordinal),
            (await f.Storage().ReadLatestAsync("case-a", default))!.BlobName);
    }

    [Fact]
    public async Task SuccessfulReceiptSurvivesLateFailureAndReplay()
    {
        using var f = new SyntheticQueuedFixture(); var m = f.Message();
        await f.Storage().CreateQueuedRunAsync(m, default);
        var receipt = await f.Storage().SaveAsync(Result(), m.Trigger, m.Batch, Time, default,
            PublicKnowledgeRunStorageService.GetCaseExecutionId(m, "case-a"));
        await f.Storage().CompleteQueuedRunAsync(m, [receipt], default);
        await f.Storage().FailQueuedRunCaseAsync(m, "synthetic later failure", default);
        var job = await f.Storage().CompleteQueuedRunAsync(m, [receipt], default);
        Assert.True(Assert.Single(job.Receipts).Ok); Assert.Equal(receipt.BlobName, job.Receipts[0].BlobName);
        Assert.Equal("publishing", job.Status);
        await f.Storage().MarkQueuedCasePublishedAsync(m, "case-a", default);
        Assert.Equal("completed", (await f.Storage().ReadQueuedRunAsync(m.JobId, default))!.Status);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("invalid-json")]
    public async Task MalformedLatestObservationCannotPublishHealthyEmptyViews(string body)
    {
        using var f = new SyntheticQueuedFixture(); f.Store.Seed("runs/latest/case-a.json", body);
        Assert.NotNull(await Record.ExceptionAsync(() => f.Storage().SaveLatestIndexAsync(default)));
        Assert.NotNull(await Record.ExceptionAsync(() => f.Storage().SaveNeedsGregReportAsync(default)));
        Assert.DoesNotContain("runs/latest-index.json", f.Store.Snapshot().Keys);
        Assert.DoesNotContain("runs/latest-needs-greg.json", f.Store.Snapshot().Keys);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(403)]
    [InlineData(500)]
    public async Task UnreadableLatestObservationCannotBeReportedAsCompleted(int status)
    {
        using var f = new SyntheticQueuedFixture();
        await f.Storage().SaveAsync(Result(), "queue", "Core", Time, default, "a");
        f.Store.Fault = r => r.Method == "GET" && r.Name == "runs/latest/case-a.json" ? SyntheticBlobStore.Error(status, "SyntheticReadFailure") : null;
        Assert.NotNull(await Record.ExceptionAsync(() => f.Storage().SaveLatestIndexAsync(default)));
        Assert.NotNull(await Record.ExceptionAsync(() => f.Storage().SaveNeedsGregReportAsync(default)));
        Assert.DoesNotContain("runs/latest-index.json", f.Store.Snapshot().Keys);
        Assert.DoesNotContain("runs/latest-needs-greg.json", f.Store.Snapshot().Keys);
    }
}
