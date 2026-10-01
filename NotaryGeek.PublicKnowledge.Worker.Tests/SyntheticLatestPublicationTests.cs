using Azure;
using static NotaryGeek.PublicKnowledge.Worker.Tests.SyntheticQueuedFixture;

namespace NotaryGeek.PublicKnowledge.Worker.Tests;

public sealed class SyntheticLatestPublicationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PointerAdvanceDuringDerivedCommitCannotReturnStaleCompletion(bool digest)
    {
        using var f = new SyntheticQueuedFixture();
        await f.Storage().SaveAsync(Result("older"), "queue", "Core", Time, default, "old");
        var view = digest ? "runs/latest-needs-greg.json" : "runs/latest-index.json";
        var advanced = 0;
        string? newest = null;
        f.Store.After = async (r, token) =>
        {
            if (r.IsWrite && r.Name == view && Interlocked.CompareExchange(ref advanced, 1, 0) == 0)
                newest = (await f.Storage().SaveAsync(Result("newer"), "queue", "Core", Time.AddTicks(1), token, "new")).BlobName;
        };
        var selected = digest
            ? (await f.Storage().SaveNeedsGregReportAsync(default)).SourceRuns!.Single().BlobName
            : (await f.Storage().SaveLatestIndexAsync(default)).Items.Single().BlobName;
        Assert.Equal(1, advanced); Assert.Equal(newest, selected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentRebuildStaleEtagCannotReplaceNewerPublishedSet(bool digest)
    {
        using var f = new SyntheticQueuedFixture();
        await f.Storage().SaveAsync(Result("older"), "queue", "Core", Time, default, "old");
        var view = digest ? "runs/latest-needs-greg.json" : "runs/latest-index.json";
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = 0;
        f.Store.Before = async (r, token) =>
        {
            if (r.IsWrite && r.Name == view && Interlocked.CompareExchange(ref first, 1, 0) == 0)
            { blocked.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10), token); }
        };
        async Task<string> Publish() => digest
            ? (await f.Storage().SaveNeedsGregReportAsync(default)).SourceRuns!.Single().BlobName
            : (await f.Storage().SaveLatestIndexAsync(default)).Items.Single().BlobName;
        var oldWriter = Publish(); await blocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var newer = await f.Storage().SaveAsync(Result("newer"), "queue", "Core", Time.AddTicks(1), default, "new");
        Assert.Equal(newer.BlobName, await Publish()); release.SetResult();
        Assert.Equal(newer.BlobName, await oldWriter);
        Assert.Equal(newer.BlobName, await Publish());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InterruptedPagedEnumerationPreservesPriorDerivedBytes(bool digest)
    {
        using var f = new SyntheticQueuedFixture(); f.Store.PageSize = 1;
        await f.Storage().SaveAsync(Result(id: "case-a"), "queue", "Core", Time, default, "a");
        await f.Storage().SaveAsync(Result(id: "case-b"), "queue", "Core", Time, default, "b");
        var view = digest ? "runs/latest-needs-greg.json" : "runs/latest-index.json";
        if (digest) await f.Storage().SaveNeedsGregReportAsync(default); else await f.Storage().SaveLatestIndexAsync(default);
        var prior = f.Store.Snapshot()[view].Body;
        f.Store.Fault = r => r.Query.ContainsKey("marker") ? SyntheticBlobStore.Error(503, "SyntheticPageFailure") : null;
        Assert.NotNull(await Record.ExceptionAsync(async () =>
        {
            if (digest) await f.Storage().SaveNeedsGregReportAsync(default); else await f.Storage().SaveLatestIndexAsync(default);
        }));
        Assert.Equal(prior, f.Store.Snapshot()[view].Body);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedPointerInterruptionsExhaustBoundWithoutAuthoritativeCompletion(bool digest)
    {
        using var f = new SyntheticQueuedFixture();
        await f.Storage().SaveAsync(Result("initial"), "queue", "Core", Time, default, "initial");
        var view = digest ? "runs/latest-needs-greg.json" : "runs/latest-index.json";
        var advances = 0; string? latest = null;
        f.Store.After = async (r, token) =>
        {
            if (r.IsWrite && r.Name == view)
            {
                var version = Interlocked.Increment(ref advances);
                latest = (await f.Storage().SaveAsync(Result($"advance-{version}"), "queue", "Core", Time.AddTicks(version),
                    token, $"advance-{version}")).BlobName;
            }
        };
        Assert.NotNull(await Record.ExceptionAsync(async () =>
        {
            if (digest) await f.Storage().SaveNeedsGregReportAsync(default); else await f.Storage().SaveLatestIndexAsync(default);
        }));
        Assert.Equal(10, advances); Assert.Equal(0, f.ProviderCalls);
        f.Store.After = null;
        var selected = digest
            ? (await f.Storage().SaveNeedsGregReportAsync(default)).SourceRuns!.Single().BlobName
            : (await f.Storage().SaveLatestIndexAsync(default)).Items.Single().BlobName;
        Assert.Equal(latest, selected);
    }

    [Fact]
    public async Task CasRetryExhaustionLeavesArchiveRecoverableAndNoFalseCompletion()
    {
        using var f = new SyntheticQueuedFixture(); var m = f.Message();
        await f.Storage().CreateQueuedRunAsync(m, default);
        var conflicts = 0;
        f.Store.Fault = r =>
        {
            if (r.IsWrite && r.Name.StartsWith("runs/executions/") && r.Header("If-Match") is not null)
            { conflicts++; return SyntheticBlobStore.Error(412, "ConditionNotMet"); }
            return null;
        };
        await Assert.ThrowsAsync<RequestFailedException>(() => f.Worker().ProcessQueuedBatch(m, default));
        Assert.Equal(10, conflicts); Assert.Equal(1, f.ProviderCalls);
        var archive = NotaryGeek.PublicKnowledge.Worker.Services.PublicKnowledgeRunStorageService.GetCaseEvidenceBlobName(m, "case-a");
        var bytes = f.Store.Snapshot()[archive].Body;
        Assert.Empty((await f.Storage().ReadQueuedRunAsync(m.JobId, default))!.Receipts);
        f.Store.Fault = null; await f.Worker().ProcessQueuedBatch(m, default);
        Assert.Equal(1, f.ProviderCalls); Assert.Equal(bytes, f.Store.Snapshot()[archive].Body);
        Assert.Equal("completed", (await f.Storage().ReadQueuedRunAsync(m.JobId, default))!.Status);
    }
}
