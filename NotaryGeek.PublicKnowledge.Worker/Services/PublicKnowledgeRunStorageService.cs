using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotaryGeek.PublicKnowledge.Worker.Configuration;
using NotaryGeek.PublicKnowledge.Worker.Models;

namespace NotaryGeek.PublicKnowledge.Worker.Services;

public sealed class PublicKnowledgeRunStorageService
{
    private const string LatestNeedsGregReportBlobName = "runs/latest-needs-greg.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IConfiguration _configuration;
    private readonly PublicKnowledgeOptions _options;
    private readonly ILogger<PublicKnowledgeRunStorageService> _logger;

    public PublicKnowledgeRunStorageService(
        IConfiguration configuration,
        IOptions<PublicKnowledgeOptions> options,
        ILogger<PublicKnowledgeRunStorageService> logger)
    {
        _configuration = configuration;
        _options = options.Value;
        _logger = logger;
    }

    public PublicKnowledgeRunStorageStatus GetStatus()
    {
        var connectionString = GetConnectionString();
        return new PublicKnowledgeRunStorageStatus(
            _options.OutputStorageConnectionStringSetting,
            _options.OutputContainerName,
            !string.IsNullOrWhiteSpace(connectionString));
    }

    public async Task<PublicKnowledgeStoredRunReceipt> SaveAsync(
        PublicKnowledgeRunResult result,
        string trigger,
        string batch,
        DateTime runStartedUtc,
        CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        var caseId = string.IsNullOrWhiteSpace(result.RegressionCaseId)
            ? "ad-hoc"
            : result.RegressionCaseId;
        var safeCaseId = ToSafeBlobSegment(caseId);
        var runId = runStartedUtc.ToString("yyyyMMddTHHmmssZ");
        var blobName = $"runs/{runStartedUtc:yyyy/MM/dd}/{runId}/{safeCaseId}.json";
        var latestBlobName = $"runs/latest/{safeCaseId}.json";
        var envelope = new PublicKnowledgeStoredRunEnvelope(
            "notary-geek-public-knowledge-stored-run-v1",
            "0.1-public",
            DateTime.UtcNow,
            trigger,
            batch,
            caseId,
            blobName,
            latestBlobName,
            result);

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var headers = new BlobHttpHeaders
        {
            ContentType = "application/json; charset=utf-8"
        };

        await container
            .GetBlobClient(blobName)
            .UploadAsync(BinaryData.FromString(json), overwrite: true, cancellationToken);
        await container
            .GetBlobClient(blobName)
            .SetHttpHeadersAsync(headers, cancellationToken: cancellationToken);

        await container
            .GetBlobClient(latestBlobName)
            .UploadAsync(BinaryData.FromString(json), overwrite: true, cancellationToken);
        await container
            .GetBlobClient(latestBlobName)
            .SetHttpHeadersAsync(headers, cancellationToken: cancellationToken);

        return new PublicKnowledgeStoredRunReceipt(
            caseId,
            result.Ok,
            result.Status,
            result.OpenAiCalled,
            result.SourceCount,
            result.Warnings.Count,
            result.Errors.Count,
            blobName,
            latestBlobName,
            result.RegressionScore?.Verdict,
            result.RegressionScore?.MustHoldPassed,
            result.RegressionScore?.MustHoldTotal,
            result.RegressionScore?.FailureSignalsObserved,
            result.RegressionScore?.FailureSignalTotal,
            GetProviderName(result));
    }

    public async Task<PublicKnowledgeQueuedRunEnvelope> CreateQueuedRunAsync(
        PublicKnowledgeQueuedRunMessage message,
        CancellationToken cancellationToken)
    {
        var envelope = CreateQueuedRunEnvelope(message);
        var container = await GetContainerAsync(cancellationToken);
        var blob = container.GetBlobClient(GetQueuedRunBlobName(message.JobId));
        try
        {
            await blob.UploadAsync(
                BinaryData.FromObjectAsJson(envelope, JsonOptions),
                new BlobUploadOptions
                {
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json; charset=utf-8" },
                    Metadata = new Dictionary<string, string> { ["status"] = envelope.Status }
                },
                cancellationToken);
            return envelope;
        }
        catch (RequestFailedException ex) when (ex.Status == 409 || ex.Status == 412)
        {
            return await ReadQueuedRunAsync(message.JobId, cancellationToken)
                ?? throw new InvalidOperationException($"Queued run '{message.JobId}' exists but could not be read.");
        }
    }

    public async Task<PublicKnowledgeQueuedRunEnvelope> MarkQueuedRunSubmittedAsync(
        string jobId,
        CancellationToken cancellationToken) =>
        await UpdateQueuedRunEnvelopeAsync(
            jobId,
            existing => existing is null
                ? throw new InvalidOperationException($"Queued run '{jobId}' does not exist.")
                : existing.Status.Equals("preparing", StringComparison.OrdinalIgnoreCase)
                    ? existing with { Status = "queued" }
                    : existing,
            cancellationToken);

    public async Task<PublicKnowledgeQueuedRunEnvelope?> ReadQueuedRunAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        var (envelope, _) = await ReadQueuedRunWithEtagAsync(jobId, cancellationToken);
        return envelope;
    }

    public async Task<IReadOnlyList<PublicKnowledgeQueuedRunSummary>> ListQueuedRunsAsync(
        int take,
        string? status,
        CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        var summaries = new List<PublicKnowledgeQueuedRunSummary>();

        await foreach (var blob in container.GetBlobsAsync(prefix: "runs/jobs/", cancellationToken: cancellationToken))
        {
            try
            {
                var client = container.GetBlobClient(blob.Name);
                var response = await client.DownloadContentAsync(cancellationToken);
                var envelope = response.Value.Content.ToObjectFromJson<PublicKnowledgeQueuedRunEnvelope>(JsonOptions);
                if (envelope is null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(status) &&
                    !envelope.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                summaries.Add(BuildQueuedRunSummary(envelope, blob.Name));
            }
            catch (Exception ex) when (ex is JsonException or RequestFailedException)
            {
                _logger.LogWarning(ex, "Could not read public knowledge queued job blob {BlobName}.", blob.Name);
            }
        }

        return summaries
            .OrderByDescending(item => item.SubmittedAtUtc)
            .Take(Math.Clamp(take, 1, 100))
            .ToArray();
    }

    public async Task<PublicKnowledgeBacklogStatus> GetBacklogStatusAsync(CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        var total = 0;
        var running = 0;
        var completed = 0;
        var failed = 0;
        var stale = 0;
        var unknown = 0;
        await foreach (var blob in container.GetBlobsAsync(
                           traits: BlobTraits.Metadata,
                           prefix: "runs/jobs/",
                           cancellationToken: cancellationToken))
        {
            total++;
            if (!blob.Metadata.TryGetValue("status", out var status))
            {
                unknown++;
                continue;
            }

            if (status.Equals("completed", StringComparison.OrdinalIgnoreCase))
            {
                completed++;
            }
            else if (status.Equals("failed", StringComparison.OrdinalIgnoreCase))
            {
                failed++;
            }
            else
            {
                running++;
                stale += blob.Properties.LastModified < DateTimeOffset.UtcNow.AddHours(-1) ? 1 : 0;
            }
        }

        return new PublicKnowledgeBacklogStatus(total, running, completed, failed, stale, unknown, DateTime.UtcNow);
    }

    public async Task<PublicKnowledgeProviderHealth> GetProviderHealthAsync(CancellationToken cancellationToken)
    {
        var runs = await ListLatestEnvelopesAsync(cancellationToken);
        var evidence = runs
            .Where(item => item.Result.ProviderEvidence is not null)
            .Select(item => new { item.StoredAtUtc, item.Result.Ok, Evidence = item.Result.ProviderEvidence! })
            .ToArray();
        return new PublicKnowledgeProviderHealth(
            evidence.Length,
            evidence.Count(item => item.Ok),
            evidence.Count(item => !item.Ok),
            evidence.Where(item => item.Ok).Select(item => (DateTime?)item.StoredAtUtc).Max(),
            evidence.Select(item => item.Evidence.Provider).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            evidence.Select(item => item.Evidence.AuthMode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            evidence.Select(item => item.Evidence.Model).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            evidence.Sum(item => item.Evidence.InputTokens),
            evidence.Sum(item => item.Evidence.OutputTokens),
            evidence.Sum(item => item.Evidence.ReasoningTokens),
            evidence
                .Where(item => !item.Ok && !string.IsNullOrWhiteSpace(item.Evidence.FailureReason))
                .OrderByDescending(item => item.StoredAtUtc)
                .Select(item => item.Evidence.FailureReason!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray());
    }

    public async Task<PublicKnowledgeQueuedRunEnvelope> MarkQueuedRunRunningAsync(
        PublicKnowledgeQueuedRunMessage message,
        CancellationToken cancellationToken)
    {
        return await UpdateQueuedRunEnvelopeAsync(
            message.JobId,
            existing =>
            {
                var envelope = existing ?? CreateQueuedRunEnvelope(message);
                if (IsTerminalQueuedRunStatus(envelope.Status))
                {
                    return envelope;
                }

                return envelope with
                {
                    StartedAtUtc = envelope.StartedAtUtc ?? DateTime.UtcNow,
                    Status = "running"
                };
            },
            cancellationToken);
    }

    public async Task<PublicKnowledgeQueuedRunEnvelope> CompleteQueuedRunAsync(
        PublicKnowledgeQueuedRunMessage message,
        IReadOnlyList<PublicKnowledgeStoredRunReceipt> receipts,
        CancellationToken cancellationToken)
    {
        return await MergeQueuedRunReceiptsAsync(message, receipts, error: null, cancellationToken);
    }

    public async Task<PublicKnowledgeQueuedRunEnvelope> FailQueuedRunAsync(
        PublicKnowledgeQueuedRunMessage message,
        string error,
        CancellationToken cancellationToken)
    {
        var existing = await ReadQueuedRunAsync(message.JobId, cancellationToken);
        var receipt = new PublicKnowledgeStoredRunReceipt(
            "job",
            false,
            "failed",
            false,
            0,
            0,
            1,
            $"runs/jobs/{ToSafeBlobSegment(message.JobId)}.json",
            $"runs/jobs/{ToSafeBlobSegment(message.JobId)}.json",
            Provider: GetProviderName(message.ProviderOverride));
        var envelope = (existing ?? new PublicKnowledgeQueuedRunEnvelope(
                "notary-geek-public-knowledge-queued-run-v1",
                "0.1-public",
                message.JobId,
                message.Batch,
                message.Trigger,
                message.Execute,
                message.CaseIds,
                message.SubmittedAtUtc,
                message.ProviderOverride,
                null,
                null,
                "running",
                0,
                message.CaseIds.Count,
                [],
                null))
            with
            {
                CompletedAtUtc = DateTime.UtcNow,
                Status = "failed",
                Error = error,
                Receipts = [receipt]
            };

        await SaveQueuedRunEnvelopeAsync(envelope, cancellationToken);
        return envelope;
    }

    public async Task<PublicKnowledgeQueuedRunEnvelope> FailQueuedRunCaseAsync(
        PublicKnowledgeQueuedRunMessage message,
        string error,
        CancellationToken cancellationToken)
    {
        var caseId = !string.IsNullOrWhiteSpace(message.CaseId)
            ? message.CaseId
            : message.CaseIds.FirstOrDefault() ?? "job";
        var jobBlobName = GetQueuedRunBlobName(message.JobId);
        var receipt = new PublicKnowledgeStoredRunReceipt(
            caseId,
            false,
            "failed",
            false,
            0,
            0,
            1,
            jobBlobName,
            jobBlobName,
            Provider: GetProviderName(message.ProviderOverride));

        return await MergeQueuedRunReceiptsAsync(message, [receipt], error, cancellationToken);
    }

    public async Task<PublicKnowledgeStoredRunEnvelope?> ReadLatestAsync(
        string caseId,
        CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        var safeCaseId = ToSafeBlobSegment(caseId);
        var blob = container.GetBlobClient($"runs/latest/{safeCaseId}.json");
        if (!await blob.ExistsAsync(cancellationToken))
        {
            return null;
        }

        var response = await blob.DownloadContentAsync(cancellationToken);
        return response.Value.Content.ToObjectFromJson<PublicKnowledgeStoredRunEnvelope>(JsonOptions);
    }

    public async Task<bool> HasFreshSuccessfulRunAsync(
        string caseId,
        string expectedBatch,
        string expectedLane,
        TimeSpan freshness,
        CancellationToken cancellationToken)
    {
        var latest = await ReadLatestAsync(caseId, cancellationToken);
        return PublicKnowledgeExecutionPolicy.IsFreshAuthorityRun(
            latest,
            expectedBatch,
            expectedLane,
            DateTime.UtcNow.Subtract(freshness));
    }

    public async Task<bool> HasDailySelectionAsync(
        string batch,
        CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        var blob = container.GetBlobClient(GetDailySelectionBlobName(batch, DateTime.UtcNow));
        return await blob.ExistsAsync(cancellationToken);
    }

    public async Task CompleteDailySelectionAsync(
        string batch,
        string jobId,
        CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var blob = container.GetBlobClient(GetDailySelectionBlobName(batch, now));
        var payload = BinaryData.FromObjectAsJson(new
        {
            schema = "public-authority-daily-selection/v1",
            batch,
            jobId,
            acceptedAtUtc = now
        }, JsonOptions);
        try
        {
            await blob.UploadAsync(
                payload,
                new BlobUploadOptions
                {
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json; charset=utf-8" }
                },
                cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 409 || ex.Status == 412)
        {
            // A matching UTC-day marker is the durable idempotency receipt.
        }
    }

    public async Task<IReadOnlyList<PublicKnowledgeStoredRunSummary>> ListLatestAsync(
        CancellationToken cancellationToken)
    {
        var envelopes = await ListLatestEnvelopesAsync(cancellationToken);
        var summaries = envelopes
            .Select(envelope => new PublicKnowledgeStoredRunSummary(
                envelope.CaseId,
                envelope.StoredAtUtc,
                envelope.Trigger,
                envelope.Batch,
                envelope.Result.Ok,
                envelope.Result.Status,
                envelope.Result.OpenAiCalled,
                envelope.Result.SourceCount,
                envelope.Result.Warnings.Count,
                envelope.Result.Errors.Count,
                envelope.BlobName,
                envelope.LatestBlobName,
                envelope.Result.RegressionScore?.Verdict,
                envelope.Result.RegressionScore?.MustHoldPassed,
                envelope.Result.RegressionScore?.MustHoldTotal,
                envelope.Result.RegressionScore?.FailureSignalsObserved,
                envelope.Result.RegressionScore?.FailureSignalTotal,
                GetProviderName(envelope.Result)))
            .ToArray();

        return summaries
            .OrderBy(item => item.CaseId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<PublicKnowledgeLatestRunIndex> BuildLatestIndexAsync(
        CancellationToken cancellationToken)
    {
        var envelopes = await ListLatestEnvelopesAsync(cancellationToken);
        var items = envelopes
            .OrderBy(envelope => envelope.CaseId, StringComparer.OrdinalIgnoreCase)
            .Select(envelope => BuildIndexItem(envelope))
            .ToArray();

        return new PublicKnowledgeLatestRunIndex(
            "notary-geek-public-knowledge-latest-run-index-v1",
            "0.1-public",
            DateTime.UtcNow,
            _options.OutputContainerName,
            "runs/latest-index.json",
            items.Length,
            items);
    }

    public async Task<PublicKnowledgeLatestRunIndex> SaveLatestIndexAsync(
        CancellationToken cancellationToken)
    {
        var index = await BuildLatestIndexAsync(cancellationToken);
        var container = await GetContainerAsync(cancellationToken);
        var json = JsonSerializer.Serialize(index, JsonOptions);
        var blob = container.GetBlobClient(index.LatestIndexBlobName);
        await blob.UploadAsync(BinaryData.FromString(json), overwrite: true, cancellationToken);
        await blob.SetHttpHeadersAsync(
            new BlobHttpHeaders { ContentType = "application/json; charset=utf-8" },
            cancellationToken: cancellationToken);

        return index;
    }

    public async Task<PublicKnowledgeNeedsGregReport> BuildNeedsGregReportAsync(
        CancellationToken cancellationToken)
    {
        var envelopes = await ListLatestEnvelopesAsync(cancellationToken);
        var items = envelopes
            .Select(BuildNeedsGregItem)
            .Where(item => item.NeedsAttention)
            .OrderBy(item => item.Priority)
            .ThenBy(item => item.CaseId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var passCount = envelopes.Count(IsPassingLatestRun);
        var needsReviewCount = envelopes.Count(envelope =>
            envelope.Result.RegressionScore?.Verdict?.Equals("needs-review", StringComparison.OrdinalIgnoreCase) == true);
        var failCount = envelopes.Count(envelope =>
            envelope.Result.RegressionScore?.Verdict?.Equals("fail", StringComparison.OrdinalIgnoreCase) == true ||
            !envelope.Result.Ok ||
            envelope.Result.Errors.Count > 0);
        var notScoredCount = envelopes.Count(envelope =>
            string.IsNullOrWhiteSpace(envelope.Result.RegressionScore?.Verdict) ||
            envelope.Result.RegressionScore.Verdict.Equals("not-scored", StringComparison.OrdinalIgnoreCase));
        var warningRunCount = envelopes.Count(envelope => envelope.Result.Warnings.Count > 0);
        var errorRunCount = envelopes.Count(envelope => envelope.Result.Errors.Count > 0);
        var summary = items.Length == 0
            ? "No latest public knowledge runs need Greg review based on current stored scores, warnings, and errors."
            : $"{items.Length} latest public knowledge run(s) need Greg review.";
        IReadOnlyList<string> operatorNextActions = items.Length == 0
            ? ["No action needed from the latest stored public knowledge runs."]
            : items
                .Take(5)
                .Select(item => $"{item.CaseId}: {item.SuggestedNextAction}")
                .ToArray();

        return new PublicKnowledgeNeedsGregReport(
            "notary-geek-public-knowledge-needs-greg-report-v1",
            "0.1-public",
            DateTime.UtcNow,
            envelopes.Count,
            items.Length == 0,
            summary,
            passCount,
            needsReviewCount,
            failCount,
            notScoredCount,
            warningRunCount,
            errorRunCount,
            items,
            LatestNeedsGregReportBlobName,
            items.FirstOrDefault()?.Priority,
            operatorNextActions);
    }

    public async Task<PublicKnowledgeNeedsGregReport> SaveNeedsGregReportAsync(
        CancellationToken cancellationToken)
    {
        var report = await BuildNeedsGregReportAsync(cancellationToken);
        var container = await GetContainerAsync(cancellationToken);
        var json = JsonSerializer.Serialize(report, JsonOptions);
        var blob = container.GetBlobClient(report.LatestReportBlobName);
        await blob.UploadAsync(BinaryData.FromString(json), overwrite: true, cancellationToken);
        await blob.SetHttpHeadersAsync(
            new BlobHttpHeaders { ContentType = "application/json; charset=utf-8" },
            cancellationToken: cancellationToken);

        return report;
    }

    public async Task<PublicKnowledgeNeedsGregReport?> ReadSavedNeedsGregReportAsync(
        CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        var blob = container.GetBlobClient(LatestNeedsGregReportBlobName);
        if (!await blob.ExistsAsync(cancellationToken))
        {
            return null;
        }

        var response = await blob.DownloadContentAsync(cancellationToken);
        return response.Value.Content.ToObjectFromJson<PublicKnowledgeNeedsGregReport>(JsonOptions);
    }

    private async Task<PublicKnowledgeQueuedRunEnvelope> MergeQueuedRunReceiptsAsync(
        PublicKnowledgeQueuedRunMessage message,
        IReadOnlyList<PublicKnowledgeStoredRunReceipt> receipts,
        string? error,
        CancellationToken cancellationToken)
    {
        return await UpdateQueuedRunEnvelopeAsync(
            message.JobId,
            existing =>
            {
                var envelope = existing ?? CreateQueuedRunEnvelope(message);
                var knownCaseIds = envelope.CaseIds.Count > 0
                    ? envelope.CaseIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                    : message.CaseIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var merged = new Dictionary<string, PublicKnowledgeStoredRunReceipt>(StringComparer.OrdinalIgnoreCase);

                foreach (var receipt in envelope.Receipts)
                {
                    if (!string.IsNullOrWhiteSpace(receipt.CaseId))
                    {
                        merged[receipt.CaseId] = receipt;
                    }
                }

                foreach (var receipt in receipts)
                {
                    if (!string.IsNullOrWhiteSpace(receipt.CaseId))
                    {
                        merged[receipt.CaseId] = receipt;
                    }
                }

                var orderedReceipts = knownCaseIds
                    .Where(merged.ContainsKey)
                    .Select(caseId => merged[caseId])
                    .Concat(merged
                        .Where(item => !knownCaseIds.Contains(item.Key, StringComparer.OrdinalIgnoreCase))
                        .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(item => item.Value))
                    .ToArray();
                var totalCount = knownCaseIds.Length;
                var completedCount = totalCount == 0
                    ? orderedReceipts.Length
                    : knownCaseIds.Count(caseId => merged.ContainsKey(caseId));
                var hasErrors = orderedReceipts.Any(item => !item.Ok);
                var isComplete = totalCount == 0 || completedCount >= totalCount;
                var nextStatus = isComplete
                    ? orderedReceipts.Length == 0
                        ? "completed-empty"
                        : hasErrors
                            ? "completed-with-errors"
                            : "completed"
                    : "running";

                return envelope with
                {
                    StartedAtUtc = envelope.StartedAtUtc ?? DateTime.UtcNow,
                    CompletedAtUtc = isComplete ? DateTime.UtcNow : null,
                    Status = nextStatus,
                    CompletedCount = completedCount,
                    TotalCount = totalCount,
                    Receipts = orderedReceipts,
                    Error = MergeError(envelope.Error, error)
                };
            },
            cancellationToken);
    }

    private async Task<PublicKnowledgeQueuedRunEnvelope> UpdateQueuedRunEnvelopeAsync(
        string jobId,
        Func<PublicKnowledgeQueuedRunEnvelope?, PublicKnowledgeQueuedRunEnvelope> update,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var (existing, etag) = await ReadQueuedRunWithEtagAsync(jobId, cancellationToken);
            var envelope = update(existing);
            try
            {
                await SaveQueuedRunEnvelopeAsync(envelope, cancellationToken, etag);
                return envelope;
            }
            catch (RequestFailedException ex) when ((ex.Status == 409 || ex.Status == 412) && attempt < maxAttempts)
            {
                _logger.LogInformation(
                    ex,
                    "Queued run {JobId} was updated concurrently; retrying status merge attempt {Attempt}/{MaxAttempts}.",
                    jobId,
                    attempt + 1,
                    maxAttempts);
                await Task.Delay(TimeSpan.FromMilliseconds(75 * attempt), cancellationToken);
            }
        }

        throw new InvalidOperationException($"Could not update queued run '{jobId}' after concurrent writes.");
    }

    private async Task<(PublicKnowledgeQueuedRunEnvelope? Envelope, ETag? ETag)> ReadQueuedRunWithEtagAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        var blob = container.GetBlobClient(GetQueuedRunBlobName(jobId));
        if (!await blob.ExistsAsync(cancellationToken))
        {
            return (null, null);
        }

        var response = await blob.DownloadContentAsync(cancellationToken);
        var envelope = response.Value.Content.ToObjectFromJson<PublicKnowledgeQueuedRunEnvelope>(JsonOptions);
        return (envelope, response.Value.Details.ETag);
    }

    private async Task<IReadOnlyList<PublicKnowledgeStoredRunEnvelope>> ListLatestEnvelopesAsync(
        CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        var envelopes = new List<PublicKnowledgeStoredRunEnvelope>();

        await foreach (var blob in container.GetBlobsAsync(prefix: "runs/latest/", cancellationToken: cancellationToken))
        {
            if (blob.Name.Equals("runs/latest-index.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var client = container.GetBlobClient(blob.Name);
                var response = await client.DownloadContentAsync(cancellationToken);
                var envelope = response.Value.Content.ToObjectFromJson<PublicKnowledgeStoredRunEnvelope>(JsonOptions);
                if (envelope is not null)
                {
                    envelopes.Add(envelope);
                }
            }
            catch (Exception ex) when (ex is JsonException or Azure.RequestFailedException)
            {
                _logger.LogWarning(ex, "Could not read public knowledge latest run blob {BlobName}.", blob.Name);
            }
        }

        return envelopes;
    }

    private async Task<BlobContainerClient> GetContainerAsync(CancellationToken cancellationToken)
    {
        var connectionString = GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Storage setting '{_options.OutputStorageConnectionStringSetting}' is not configured.");
        }

        var container = new BlobContainerClient(connectionString, _options.OutputContainerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        return container;
    }

    private async Task SaveQueuedRunEnvelopeAsync(
        PublicKnowledgeQueuedRunEnvelope envelope,
        CancellationToken cancellationToken,
        ETag? etag = null)
    {
        var container = await GetContainerAsync(cancellationToken);
        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        var blob = container.GetBlobClient(GetQueuedRunBlobName(envelope.JobId));
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json; charset=utf-8" },
            Metadata = new Dictionary<string, string> { ["status"] = envelope.Status.ToLowerInvariant() },
            Conditions = etag is null ? null : new BlobRequestConditions { IfMatch = etag.Value }
        };
        await blob.UploadAsync(BinaryData.FromString(json), options, cancellationToken);
    }

    private static string GetQueuedRunBlobName(string jobId) =>
        $"runs/jobs/{ToSafeBlobSegment(jobId)}.json";

    private static string GetDailySelectionBlobName(string batch, DateTime utcNow) =>
        $"runs/selection/{utcNow:yyyy/MM/dd}/{ToSafeBlobSegment(batch)}.json";

    private string? GetConnectionString() =>
        _configuration[_options.OutputStorageConnectionStringSetting];

    private static string ToSafeBlobSegment(string value)
    {
        var safe = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-')
            .ToArray());

        return string.IsNullOrWhiteSpace(safe) ? "unknown" : safe;
    }

    private static string GetProviderName(PublicKnowledgeRunResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Provider))
        {
            return result.Provider;
        }

        if (result.Model.Contains('/', StringComparison.Ordinal))
        {
            return "Straico";
        }

        return "OpenAI";
    }

    private static string? GetProviderName(string? providerOverride)
    {
        if (string.IsNullOrWhiteSpace(providerOverride) ||
            providerOverride.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return providerOverride;
    }

    private static PublicKnowledgeQueuedRunEnvelope CreateQueuedRunEnvelope(
        PublicKnowledgeQueuedRunMessage message) =>
        new(
            "notary-geek-public-knowledge-queued-run-v1",
            "0.1-public",
            message.JobId,
            message.Batch,
            message.Trigger,
            message.Execute,
            message.CaseIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            message.SubmittedAtUtc,
            message.ProviderOverride,
            null,
            null,
            "preparing",
            0,
            message.CaseIds.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            [],
            null);

    private static bool IsTerminalQueuedRunStatus(string status) =>
        status.Equals("completed", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("completed-with-errors", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("completed-empty", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("failed", StringComparison.OrdinalIgnoreCase);

    private static PublicKnowledgeQueuedRunSummary BuildQueuedRunSummary(
        PublicKnowledgeQueuedRunEnvelope envelope,
        string blobName)
    {
        var isTerminal = IsTerminalQueuedRunStatus(envelope.Status);
        var activeSinceUtc = envelope.StartedAtUtc ?? envelope.SubmittedAtUtc;
        var activeAgeMinutes = Math.Max(0, (DateTime.UtcNow - activeSinceUtc).TotalMinutes);
        var isStale = !isTerminal && activeAgeMinutes >= 45;

        return new PublicKnowledgeQueuedRunSummary(
            envelope.JobId,
            envelope.Batch,
            envelope.Trigger,
            envelope.Execute,
            envelope.SubmittedAtUtc,
            envelope.ProviderOverride,
            envelope.StartedAtUtc,
            envelope.CompletedAtUtc,
            envelope.Status,
            envelope.CompletedCount,
            envelope.TotalCount,
            envelope.Receipts.Count(item => item.Ok),
            envelope.Receipts.Count(item => !item.Ok),
            isTerminal,
            isStale,
            Math.Round(activeAgeMinutes, 1),
            envelope.Error,
            blobName);
    }

    private static string? MergeError(string? existing, string? incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming))
        {
            return existing;
        }

        if (string.IsNullOrWhiteSpace(existing))
        {
            return incoming;
        }

        return existing.Contains(incoming, StringComparison.OrdinalIgnoreCase)
            ? existing
            : $"{existing} | {incoming}";
    }

    private static PublicKnowledgeLatestRunIndexItem BuildIndexItem(
        PublicKnowledgeStoredRunEnvelope envelope)
    {
        var response = ParseResponseText(envelope.Result.ResponseText);
        return new PublicKnowledgeLatestRunIndexItem(
            envelope.CaseId,
            envelope.StoredAtUtc,
            envelope.Trigger,
            envelope.Batch,
            envelope.Result.Ok,
            envelope.Result.Status,
            envelope.Result.OpenAiCalled,
            envelope.Result.SourceCount,
            envelope.Result.Warnings.Count,
            envelope.Result.Errors.Count,
            envelope.BlobName,
            envelope.LatestBlobName,
            response.Summary,
            response.RouteFindings,
            response.SourceQualityFindings,
            response.SuggestedPublicReplies,
            response.WebsiteBriefs,
            response.LawRefreshCandidates,
            response.Risks,
            response.Citations,
            envelope.Result.RegressionScore?.Verdict,
            envelope.Result.RegressionScore?.MustHoldPassed,
            envelope.Result.RegressionScore?.MustHoldTotal,
            envelope.Result.RegressionScore?.FailureSignalsObserved,
            envelope.Result.RegressionScore?.FailureSignalTotal,
            GetProviderName(envelope.Result));
    }

    private static PublicKnowledgeNeedsGregItem BuildNeedsGregItem(
        PublicKnowledgeStoredRunEnvelope envelope)
    {
        var score = envelope.Result.RegressionScore;
        var verdict = score?.Verdict;
        var warningCount = envelope.Result.Warnings.Count;
        var errorCount = envelope.Result.Errors.Count;
        var needsAttention = !envelope.Result.Ok ||
            errorCount > 0 ||
            warningCount > 0 ||
            string.IsNullOrWhiteSpace(verdict) ||
            verdict.Equals("fail", StringComparison.OrdinalIgnoreCase) ||
            verdict.Equals("needs-review", StringComparison.OrdinalIgnoreCase) ||
            verdict.Equals("not-scored", StringComparison.OrdinalIgnoreCase);
        var priority = GetNeedsGregPriority(envelope);
        var reason = GetNeedsGregReason(envelope);
        var suggestedNextAction = GetNeedsGregSuggestedAction(envelope);

        return new PublicKnowledgeNeedsGregItem(
            envelope.CaseId,
            envelope.StoredAtUtc,
            envelope.Trigger,
            envelope.Batch,
            envelope.Result.Ok,
            envelope.Result.Status,
            verdict,
            score?.MustHoldPassed,
            score?.MustHoldTotal,
            score?.FailureSignalsObserved,
            score?.FailureSignalTotal,
            envelope.Result.OpenAiCalled,
            envelope.Result.SourceCount,
            warningCount,
            errorCount,
            priority,
            needsAttention,
            reason,
            suggestedNextAction,
            envelope.BlobName,
            envelope.LatestBlobName,
            GetProviderName(envelope.Result));
    }

    private static bool IsPassingLatestRun(PublicKnowledgeStoredRunEnvelope envelope) =>
        envelope.Result.Ok &&
        envelope.Result.Errors.Count == 0 &&
        envelope.Result.RegressionScore?.Verdict?.Equals("pass", StringComparison.OrdinalIgnoreCase) == true;

    private static int GetNeedsGregPriority(PublicKnowledgeStoredRunEnvelope envelope)
    {
        var verdict = envelope.Result.RegressionScore?.Verdict;
        if (!envelope.Result.Ok || envelope.Result.Errors.Count > 0)
        {
            return 10;
        }

        if (verdict?.Equals("fail", StringComparison.OrdinalIgnoreCase) == true)
        {
            return 20;
        }

        if (verdict?.Equals("needs-review", StringComparison.OrdinalIgnoreCase) == true)
        {
            return 30;
        }

        if (string.IsNullOrWhiteSpace(verdict) ||
            verdict.Equals("not-scored", StringComparison.OrdinalIgnoreCase))
        {
            return 40;
        }

        return envelope.Result.Warnings.Count > 0 ? 50 : 90;
    }

    private static string GetNeedsGregReason(PublicKnowledgeStoredRunEnvelope envelope)
    {
        var score = envelope.Result.RegressionScore;
        if (!envelope.Result.Ok || envelope.Result.Errors.Count > 0)
        {
            return "Run returned an error or provider/preflight failure.";
        }

        if (score?.Verdict?.Equals("fail", StringComparison.OrdinalIgnoreCase) == true)
        {
            return score.FailureSignalsObserved > 0
                ? "Scorer observed one or more failure-signal patterns."
                : "Scorer marked the answer failed because must-hold coverage was too weak.";
        }

        if (score?.Verdict?.Equals("needs-review", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Scorer found partial must-hold coverage and no observed failure signal.";
        }

        if (string.IsNullOrWhiteSpace(score?.Verdict) ||
            score.Verdict.Equals("not-scored", StringComparison.OrdinalIgnoreCase))
        {
            return "Run is not scored, usually because it was a dry run or no model response was stored.";
        }

        if (envelope.Result.Warnings.Count > 0)
        {
            return "Run passed scoring but produced warning(s), usually citation/source hygiene.";
        }

        return "No current review reason.";
    }

    private static string GetNeedsGregSuggestedAction(PublicKnowledgeStoredRunEnvelope envelope)
    {
        var score = envelope.Result.RegressionScore;
        if (!envelope.Result.Ok || envelope.Result.Errors.Count > 0)
        {
            return "Check the stored run errors first; fix source fetch, prompt size, provider, or storage configuration before judging content.";
        }

        if (score?.Verdict?.Equals("fail", StringComparison.OrdinalIgnoreCase) == true)
        {
            return score.FailureSignalsObserved > 0
                ? "Review whether this is a true model failure or scorer false positive; patch the scorer, sources, or regression case before promotion."
                : "Review missing must-hold coverage; strengthen public sources or prompt boundaries if the model answer is weak.";
        }

        if (score?.Verdict?.Equals("needs-review", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "Review the missing must-hold checks; promote only if the answer is substantively correct despite surface scoring gaps.";
        }

        if (string.IsNullOrWhiteSpace(score?.Verdict) ||
            score.Verdict.Equals("not-scored", StringComparison.OrdinalIgnoreCase))
        {
            return "Run an execute=true case if a model answer is needed; otherwise ignore dry-run diagnostics.";
        }

        if (envelope.Result.Warnings.Count > 0)
        {
            return "Review warning(s) for citation hygiene or add fetch candidates to the manifest when appropriate.";
        }

        return "No action needed.";
    }

    private static ParsedProviderResponse ParseResponseText(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return ParsedProviderResponse.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            return new ParsedProviderResponse(
                TryGetString(root, "summary"),
                TryGetStringArray(root, "routeFindings"),
                TryGetStringArray(root, "sourceQualityFindings"),
                TryGetStringArray(root, "suggestedPublicReplies"),
                TryGetStringArray(root, "websiteBriefs"),
                TryGetStringArray(root, "lawRefreshCandidates"),
                TryGetStringArray(root, "risks"),
                TryGetStringArray(root, "citations"));
        }
        catch (JsonException)
        {
            return ParsedProviderResponse.Empty;
        }
    }

    private static string? TryGetString(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.GetRawText();
    }

    private static IReadOnlyList<string> TryGetStringArray(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property
            .EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        foreach (var candidate in element.EnumerateObject())
        {
            if (candidate.NameEquals(propertyName) || candidate.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private sealed record ParsedProviderResponse(
        string? Summary,
        IReadOnlyList<string> RouteFindings,
        IReadOnlyList<string> SourceQualityFindings,
        IReadOnlyList<string> SuggestedPublicReplies,
        IReadOnlyList<string> WebsiteBriefs,
        IReadOnlyList<string> LawRefreshCandidates,
        IReadOnlyList<string> Risks,
        IReadOnlyList<string> Citations)
    {
        public static ParsedProviderResponse Empty { get; } = new(null, [], [], [], [], [], [], []);
    }
}

public sealed record PublicKnowledgeRunStorageStatus(
    string ConnectionStringSetting,
    string ContainerName,
    bool HasConnectionString);

public sealed record PublicKnowledgeBacklogStatus(
    int JobEnvelopeCount,
    int ActiveCount,
    int CompletedCount,
    int FailedCount,
    int StaleCount,
    int UnknownLegacyStatusCount,
    DateTime CheckedAtUtc);

public sealed record PublicKnowledgeProviderHealth(
    int RunCount,
    int UsableOutputCount,
    int FailedOutputCount,
    DateTime? LastUsableOutputUtc,
    IReadOnlyList<string> Providers,
    IReadOnlyList<string> AuthModes,
    IReadOnlyList<string> Models,
    int InputTokens,
    int OutputTokens,
    int ReasoningTokens,
    IReadOnlyList<string> ActionableFailureReasons);

public sealed record PublicKnowledgeStoredRunEnvelope(
    string Schema,
    string Version,
    DateTime StoredAtUtc,
    string Trigger,
    string Batch,
    string CaseId,
    string BlobName,
    string LatestBlobName,
    PublicKnowledgeRunResult Result);

public sealed record PublicKnowledgeStoredRunReceipt(
    string CaseId,
    bool Ok,
    string Status,
    bool OpenAiCalled,
    int SourceCount,
    int WarningCount,
    int ErrorCount,
    string BlobName,
    string LatestBlobName,
    string? ScoreVerdict = null,
    int? MustHoldPassed = null,
    int? MustHoldTotal = null,
    int? FailureSignalsObserved = null,
    int? FailureSignalTotal = null,
    string? Provider = null)
{
    public bool ProviderCalled => OpenAiCalled;
}

public sealed record PublicKnowledgeQueuedRunEnvelope(
    string Schema,
    string Version,
    string JobId,
    string Batch,
    string Trigger,
    bool Execute,
    IReadOnlyList<string> CaseIds,
    DateTime SubmittedAtUtc,
    string? ProviderOverride,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string Status,
    int CompletedCount,
    int TotalCount,
    IReadOnlyList<PublicKnowledgeStoredRunReceipt> Receipts,
    string? Error);

public sealed record PublicKnowledgeQueuedRunSummary(
    string JobId,
    string Batch,
    string Trigger,
    bool Execute,
    DateTime SubmittedAtUtc,
    string? ProviderOverride,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string Status,
    int CompletedCount,
    int TotalCount,
    int OkReceiptCount,
    int FailedReceiptCount,
    bool IsTerminal,
    bool IsStale,
    double ActiveAgeMinutes,
    string? Error,
    string BlobName);

public sealed record PublicKnowledgeStoredRunSummary(
    string CaseId,
    DateTime StoredAtUtc,
    string Trigger,
    string Batch,
    bool Ok,
    string Status,
    bool OpenAiCalled,
    int SourceCount,
    int WarningCount,
    int ErrorCount,
    string BlobName,
    string LatestBlobName,
    string? ScoreVerdict = null,
    int? MustHoldPassed = null,
    int? MustHoldTotal = null,
    int? FailureSignalsObserved = null,
    int? FailureSignalTotal = null,
    string? Provider = null)
{
    public bool ProviderCalled => OpenAiCalled;
}

public sealed record PublicKnowledgeLatestRunIndex(
    string Schema,
    string Version,
    DateTime GeneratedAtUtc,
    string ContainerName,
    string LatestIndexBlobName,
    int RunCount,
    IReadOnlyList<PublicKnowledgeLatestRunIndexItem> Items);

public sealed record PublicKnowledgeLatestRunIndexItem(
    string CaseId,
    DateTime StoredAtUtc,
    string Trigger,
    string Batch,
    bool Ok,
    string Status,
    bool OpenAiCalled,
    int SourceCount,
    int WarningCount,
    int ErrorCount,
    string BlobName,
    string LatestBlobName,
    string? Summary,
    IReadOnlyList<string> RouteFindings,
    IReadOnlyList<string> SourceQualityFindings,
    IReadOnlyList<string> SuggestedPublicReplies,
    IReadOnlyList<string> WebsiteBriefs,
    IReadOnlyList<string> LawRefreshCandidates,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Citations,
    string? ScoreVerdict = null,
    int? MustHoldPassed = null,
    int? MustHoldTotal = null,
    int? FailureSignalsObserved = null,
    int? FailureSignalTotal = null,
    string? Provider = null)
{
    public bool ProviderCalled => OpenAiCalled;
}

public sealed record PublicKnowledgeNeedsGregReport(
    string Schema,
    string Version,
    DateTime GeneratedAtUtc,
    int RunCount,
    bool Healthy,
    string Summary,
    int PassingCount,
    int NeedsReviewCount,
    int FailCount,
    int NotScoredCount,
    int WarningRunCount,
    int ErrorRunCount,
    IReadOnlyList<PublicKnowledgeNeedsGregItem> Items,
    string LatestReportBlobName = "runs/latest-needs-greg.json",
    int? HighestPriority = null,
    IReadOnlyList<string>? OperatorNextActions = null);

public sealed record PublicKnowledgeNeedsGregItem(
    string CaseId,
    DateTime StoredAtUtc,
    string Trigger,
    string Batch,
    bool Ok,
    string Status,
    string? ScoreVerdict,
    int? MustHoldPassed,
    int? MustHoldTotal,
    int? FailureSignalsObserved,
    int? FailureSignalTotal,
    bool OpenAiCalled,
    int SourceCount,
    int WarningCount,
    int ErrorCount,
    int Priority,
    bool NeedsAttention,
    string Reason,
    string SuggestedNextAction,
    string BlobName,
    string LatestBlobName,
    string? Provider = null)
{
    public bool ProviderCalled => OpenAiCalled;
}
