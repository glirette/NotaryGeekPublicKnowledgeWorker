using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
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
        CancellationToken cancellationToken,
        string? executionId = null)
    {
        var container = await GetContainerAsync(cancellationToken);
        var caseId = string.IsNullOrWhiteSpace(result.RegressionCaseId)
            ? "ad-hoc"
            : result.RegressionCaseId;
        var safeCaseId = ToSafeBlobSegment(caseId);
        var runId = executionId is null
            ? $"{runStartedUtc:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}"
            : $"{runStartedUtc:yyyyMMddTHHmmssfffffffZ}-{Hash(executionId)}";
        var blobName = $"runs/{runStartedUtc:yyyy/MM/dd}/{runId}/{safeCaseId}.json";
        var latestBlobName = $"runs/latest/{safeCaseId}.json";
        var envelope = new PublicKnowledgeStoredRunEnvelope(
            "notary-geek-public-knowledge-stored-run-v1",
            "0.1-public",
            runStartedUtc,
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

        var archive = container.GetBlobClient(blobName);
        try
        {
            await archive.UploadAsync(BinaryData.FromString(json), new BlobUploadOptions
            {
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                HttpHeaders = headers
            }, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            var existing = await archive.DownloadContentAsync(cancellationToken);
            using var expectedJson = JsonDocument.Parse(json);
            using var actualJson = JsonDocument.Parse(existing.Value.Content);
            if (!JsonElement.DeepEquals(expectedJson.RootElement, actualJson.RootElement))
                throw new InvalidOperationException($"Conflicting immutable run evidence at '{blobName}'.");
        }

        // Compare and swap the latest pointer. An older completion cannot replace a newer run.
        var latest = container.GetBlobClient(latestBlobName);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            ETag? etag = null;
            try
            {
                var current = await latest.DownloadContentAsync(cancellationToken);
                etag = current.Value.Details.ETag;
                var previous = current.Value.Content.ToObjectFromJson<PublicKnowledgeStoredRunEnvelope>(JsonOptions)
                    ?? throw new InvalidOperationException($"Malformed latest run at '{latestBlobName}'.");
                if (previous.StoredAtUtc > envelope.StoredAtUtc ||
                    (previous.StoredAtUtc == envelope.StoredAtUtc &&
                     string.CompareOrdinal(previous.BlobName, envelope.BlobName) >= 0))
                    break;
            }
            catch (RequestFailedException ex) when (ex.Status == 404) { }

            try
            {
                await latest.UploadAsync(BinaryData.FromString(json), new BlobUploadOptions
                {
                    Conditions = etag.HasValue
                        ? new BlobRequestConditions { IfMatch = etag.Value }
                        : new BlobRequestConditions { IfNoneMatch = ETag.All },
                    HttpHeaders = headers
                }, cancellationToken);
                break;
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412 && attempt < 9) { }
        }

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

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..32];

    public static string FingerprintCase(PublicKnowledgeRegressionCase regressionCase) =>
        Hash(JsonSerializer.Serialize(regressionCase, JsonOptions));

    public static bool HasUncertainLegacyExecution(PublicKnowledgeQueuedRunEnvelope? beforeRunning) =>
        beforeRunning is null ||
        (beforeRunning.CaseFingerprints is null &&
         !beforeRunning.LegacyFanOutReady &&
         !beforeRunning.Status.Equals("preparing", StringComparison.OrdinalIgnoreCase) &&
         !beforeRunning.Status.Equals("queued", StringComparison.OrdinalIgnoreCase));

    public static string GetCaseExecutionId(PublicKnowledgeQueuedRunMessage message, string caseId) =>
        $"{message.JobId}:{caseId}";

    public static string GetCaseEvidenceBlobName(PublicKnowledgeQueuedRunMessage message, string caseId) =>
        $"runs/{message.SubmittedAtUtc:yyyy/MM/dd}/{message.SubmittedAtUtc:yyyyMMddTHHmmssfffffffZ}-{Hash(GetCaseExecutionId(message, caseId))}/{ToSafeBlobSegment(caseId)}.json";

    public async Task<PublicKnowledgeStoredRunEnvelope?> ReadStoredRunAsync(
        string blobName, CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        try
        {
            var response = await container.GetBlobClient(blobName).DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToObjectFromJson<PublicKnowledgeStoredRunEnvelope>(JsonOptions)
                ?? throw new InvalidOperationException($"Malformed stored run at '{blobName}'.");
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { return null; }
    }

    public sealed record CaseExecution(string Fingerprint, string Phase, string? EvidenceBlobName, DateTime ReservedAtUtc)
    {
        public bool CandidatesPublished { get; init; }
        public bool IndexPublished { get; init; }
        public bool DigestPublished { get; init; }
    }

    public async Task<CaseExecution> AdmitCaseAsync(
        PublicKnowledgeQueuedRunMessage message, PublicKnowledgeRegressionCase regressionCase,
        CancellationToken cancellationToken)
    {
        var queued = await ReadQueuedRunAsync(message.JobId, cancellationToken)
            ?? throw new InvalidOperationException("Queued job identity is missing.");
        ValidateQueuedIdentity(queued, message);
        if (!queued.CaseIds.Contains(regressionCase.Id, StringComparer.OrdinalIgnoreCase) ||
            !string.Equals(message.CaseId ?? regressionCase.Id, regressionCase.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Queued case is not part of the submitted job.");
        if (queued.CaseFingerprints is not null &&
            (!queued.CaseFingerprints.TryGetValue(regressionCase.Id, out var submittedFingerprint) ||
             submittedFingerprint != FingerprintCase(regressionCase)))
            throw new InvalidOperationException("Queued regression case changed since submission.");
        // The full case snapshot binds source URLs, focus and scoring parameters to this identity.
        var fingerprint = Hash(JsonSerializer.Serialize(new
        {
            message, regressionCase
        }, JsonOptions));
        var container = await GetContainerAsync(cancellationToken);
        var blob = container.GetBlobClient($"runs/executions/{Hash(message.JobId)}/{Hash(regressionCase.Id)}.json");
        var reservation = new CaseExecution(fingerprint, "provider-outcome-unknown", null, message.SubmittedAtUtc);
        try
        {
            await blob.UploadAsync(BinaryData.FromObjectAsJson(reservation, JsonOptions), new BlobUploadOptions
            {
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json; charset=utf-8" }
            }, cancellationToken);
            return reservation with { Phase = "admitted" };
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            var current = await blob.DownloadContentAsync(cancellationToken);
            var previous = current.Value.Content.ToObjectFromJson<CaseExecution>(JsonOptions)
                ?? throw new InvalidOperationException("Malformed case reservation.");
            if (previous.Fingerprint != fingerprint)
                throw new InvalidOperationException("Queued case identity has changed work parameters.");
            return previous;
        }
    }

    private static void ValidateQueuedIdentity(PublicKnowledgeQueuedRunEnvelope queued, PublicKnowledgeQueuedRunMessage message)
    {
        if (queued.JobId != message.JobId || queued.Batch != message.Batch || queued.Trigger != message.Trigger ||
            queued.Execute != message.Execute || queued.SubmittedAtUtc != message.SubmittedAtUtc ||
            !string.Equals(queued.ProviderOverride, message.ProviderOverride, StringComparison.Ordinal) ||
            (queued.RunKind is not null && queued.RunKind != message.RunKind) ||
            (queued.AuthorityLane is not null && queued.AuthorityLane != message.AuthorityLane) ||
            (queued.CaseFingerprints is not null &&
             (message.CaseFingerprints is null || !queued.CaseFingerprints.OrderBy(item => item.Key)
                 .SequenceEqual(message.CaseFingerprints.OrderBy(item => item.Key)))) ||
            !queued.CaseIds.SequenceEqual(message.CaseIds.Distinct(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Queued job identity has changed work parameters.");
    }

    public async Task<CaseExecution> RecordCaseEvidenceAsync(
        PublicKnowledgeQueuedRunMessage message, PublicKnowledgeRegressionCase regressionCase,
        CaseExecution admitted, string blobName, CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        var blob = container.GetBlobClient($"runs/executions/{Hash(message.JobId)}/{Hash(regressionCase.Id)}.json");
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var current = await blob.DownloadContentAsync(cancellationToken);
            var previous = current.Value.Content.ToObjectFromJson<CaseExecution>(JsonOptions)
                ?? throw new InvalidOperationException("Malformed case reservation.");
            if (previous.Fingerprint != admitted.Fingerprint ||
                (previous.EvidenceBlobName is not null && previous.EvidenceBlobName != blobName))
                throw new InvalidOperationException("Conflicting case evidence.");
            if (previous.Phase == "evidence-recorded") return previous;
            try
            {
                var next = previous with { Phase = "evidence-recorded", EvidenceBlobName = blobName };
                await blob.UploadAsync(BinaryData.FromObjectAsJson(next, JsonOptions), new BlobUploadOptions
                {
                    Conditions = new BlobRequestConditions { IfMatch = current.Value.Details.ETag },
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json; charset=utf-8" }
                }, cancellationToken);
                return next;
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412 && attempt < 9) { }
        }
        throw new InvalidOperationException("Could not record case evidence after concurrent writes.");
    }

    public async Task<CaseExecution> MarkCasePublicationAsync(
        PublicKnowledgeQueuedRunMessage message, PublicKnowledgeRegressionCase regressionCase,
        string phase, CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        var blob = container.GetBlobClient($"runs/executions/{Hash(message.JobId)}/{Hash(regressionCase.Id)}.json");
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var response = await blob.DownloadContentAsync(cancellationToken);
            var prior = response.Value.Content.ToObjectFromJson<CaseExecution>(JsonOptions)
                ?? throw new InvalidOperationException("Malformed case publication state.");
            if (prior.EvidenceBlobName != GetCaseEvidenceBlobName(message, regressionCase.Id))
                throw new InvalidOperationException("Publication has no matching immutable evidence.");
            var next = phase switch
            {
                "candidates" => prior with { CandidatesPublished = true },
                "index" => prior with { IndexPublished = true },
                "digest" => prior with { DigestPublished = true },
                _ => throw new ArgumentOutOfRangeException(nameof(phase))
            };
            if (next == prior) return prior;
            try
            {
                await blob.UploadAsync(BinaryData.FromObjectAsJson(next, JsonOptions), new BlobUploadOptions
                {
                    Conditions = new BlobRequestConditions { IfMatch = response.Value.Details.ETag },
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json; charset=utf-8" }
                }, cancellationToken);
                return next;
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412 && attempt < 9) { }
        }
        throw new InvalidOperationException("Could not record case publication after concurrent writes.");
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
            var existing = await ReadQueuedRunAsync(message.JobId, cancellationToken)
                ?? throw new InvalidOperationException($"Queued run '{message.JobId}' exists but could not be read.");
            ValidateQueuedIdentity(existing, message);
            return existing;
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

            var bucket = PublicKnowledgeExecutionPolicy.GetBacklogBucket(status);
            if (bucket.Equals("completed", StringComparison.Ordinal))
            {
                completed++;
            }
            else if (bucket.Equals("failed", StringComparison.Ordinal))
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
            evidence.Select(item => item.Evidence.RequestedModel)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            evidence.Select(item => item.Evidence.ServiceTier)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            evidence.Sum(item => item.Evidence.InputTokens),
            evidence.Sum(item => item.Evidence.OutputTokens),
            evidence.Sum(item => item.Evidence.ReasoningTokens),
            evidence.Length > 0 && evidence.All(item => item.Evidence.CostUsd.HasValue)
                ? evidence.Sum(item => item.Evidence.CostUsd!.Value)
                : null,
            evidence.Select(item => item.Evidence.CostEvidence)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            evidence.Select(item => item.Evidence.IncentiveEvidence)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
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
                if (existing is not null) ValidateQueuedIdentity(existing, message);
                if (IsTerminalQueuedRunStatus(envelope.Status))
                {
                    return envelope;
                }

                return envelope with
                {
                    StartedAtUtc = envelope.StartedAtUtc ?? DateTime.UtcNow,
                    Status = "running",
                    LegacyFanOutReady = envelope.LegacyFanOutReady ||
                        (existing is not null && envelope.CaseFingerprints is null &&
                         string.IsNullOrWhiteSpace(message.CaseId) && message.CaseIds.Count > 1 &&
                         (existing.Status.Equals("queued", StringComparison.OrdinalIgnoreCase) ||
                          existing.Status.Equals("preparing", StringComparison.OrdinalIgnoreCase)))
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

    public async Task<PublicKnowledgeQueuedRunEnvelope> MarkQueuedCasePublishedAsync(
        PublicKnowledgeQueuedRunMessage message, string caseId, CancellationToken cancellationToken) =>
        await UpdateQueuedRunEnvelopeAsync(message.JobId, existing =>
        {
            if (existing is null) throw new InvalidOperationException("Queued job is missing.");
            ValidateQueuedIdentity(existing, message);
            if (!existing.Receipts.Any(receipt => receipt.CaseId.Equals(caseId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Cannot publish a case without a durable receipt.");
            var published = (existing.PublishedCaseIds ?? Array.Empty<string>())
                .Append(caseId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var complete = existing.CaseIds.All(id => published.Contains(id, StringComparer.OrdinalIgnoreCase)) &&
                existing.CaseIds.All(id => existing.Receipts.Any(receipt => receipt.CaseId.Equals(id, StringComparison.OrdinalIgnoreCase)));
            return existing with
            {
                PublishedCaseIds = published,
                Status = complete
                    ? existing.Receipts.Any(receipt => !receipt.Ok) ? "completed-with-errors" : "completed"
                    : "publishing",
                CompletedAtUtc = complete ? DateTime.UtcNow : null
            };
        }, cancellationToken);

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
        var container = await GetContainerAsync(cancellationToken);
        var blob = container.GetBlobClient("runs/latest-index.json");
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var index = await BuildLatestIndexAsync(cancellationToken);
            var candidate = index.Items.Select(item => new PublicKnowledgeRunReference(
                item.CaseId, item.StoredAtUtc, item.BlobName)).ToArray();
            ETag? etag = null;
            try
            {
                var existing = await blob.DownloadContentAsync(cancellationToken);
                etag = existing.Value.Details.ETag;
                var prior = existing.Value.Content.ToObjectFromJson<PublicKnowledgeLatestRunIndex>(JsonOptions)
                    ?? throw new InvalidOperationException("Malformed latest run index.");
                var previous = prior.Items.Select(item => new PublicKnowledgeRunReference(
                    item.CaseId, item.StoredAtUtc, item.BlobName));
                if (!SnapshotIncludes(candidate, previous))
                    continue; // A pointer changed while we read the latest set. Rebuild it.
                if (SnapshotIncludes(previous, candidate)) return prior;
            }
            catch (RequestFailedException ex) when (ex.Status == 404) { }

            try
            {
                await blob.UploadAsync(BinaryData.FromObjectAsJson(index, JsonOptions), new BlobUploadOptions
                {
                    Conditions = etag.HasValue
                        ? new BlobRequestConditions { IfMatch = etag.Value }
                        : new BlobRequestConditions { IfNoneMatch = ETag.All },
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json; charset=utf-8" }
                }, cancellationToken);
                return index;
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412 && attempt < 9) { }
        }
        throw new InvalidOperationException("Could not publish a current latest run index after concurrent updates.");
    }

    public sealed record PublicKnowledgeRunReference(string CaseId, DateTime StoredAtUtc, string BlobName);

    public static bool SnapshotIncludes(IEnumerable<PublicKnowledgeRunReference> candidate,
        IEnumerable<PublicKnowledgeRunReference> previous)
    {
        var selected = candidate.ToDictionary(item => item.CaseId, StringComparer.OrdinalIgnoreCase);
        return previous.All(old => selected.TryGetValue(old.CaseId, out var next) &&
            (next.StoredAtUtc > old.StoredAtUtc ||
             (next.StoredAtUtc == old.StoredAtUtc &&
              string.CompareOrdinal(next.BlobName, old.BlobName) >= 0)));
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
            operatorNextActions)
        {
            SourceRuns = envelopes.Select(item => new PublicKnowledgeRunReference(
                item.CaseId, item.StoredAtUtc, item.BlobName)).ToArray()
        };
    }

    public async Task<PublicKnowledgeNeedsGregReport> SaveNeedsGregReportAsync(
        CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        var blob = container.GetBlobClient(LatestNeedsGregReportBlobName);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var report = await BuildNeedsGregReportAsync(cancellationToken);
            ETag? etag = null;
            try
            {
                var existing = await blob.DownloadContentAsync(cancellationToken);
                etag = existing.Value.Details.ETag;
                var prior = existing.Value.Content.ToObjectFromJson<PublicKnowledgeNeedsGregReport>(JsonOptions)
                    ?? throw new InvalidOperationException("Malformed latest digest.");
                if (prior.SourceRuns is not null)
                {
                    if (!SnapshotIncludes(report.SourceRuns!, prior.SourceRuns))
                        continue;
                    if (SnapshotIncludes(prior.SourceRuns, report.SourceRuns!)) return prior;
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404) { }

            try
            {
                await blob.UploadAsync(BinaryData.FromObjectAsJson(report, JsonOptions), new BlobUploadOptions
                {
                    Conditions = etag.HasValue
                        ? new BlobRequestConditions { IfMatch = etag.Value }
                        : new BlobRequestConditions { IfNoneMatch = ETag.All },
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json; charset=utf-8" }
                }, cancellationToken);
                return report;
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412 && attempt < 9) { }
        }
        throw new InvalidOperationException("Could not publish a current latest digest after concurrent updates.");
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
                if (existing is not null) ValidateQueuedIdentity(existing, message);
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
                        if (merged.TryGetValue(receipt.CaseId, out var previous) && previous.Ok && !receipt.Ok)
                            continue;
                        if (merged.TryGetValue(receipt.CaseId, out previous) && previous.Ok && receipt.Ok &&
                            previous.BlobName != receipt.BlobName)
                            throw new InvalidOperationException("Conflicting successful case receipts.");
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
                var published = envelope.PublishedCaseIds ?? Array.Empty<string>();
                var nextStatus = !isComplete ? "running" :
                    knownCaseIds.All(id => published.Contains(id, StringComparer.OrdinalIgnoreCase))
                        ? hasErrors ? "completed-with-errors" : "completed"
                        : "publishing";

                return envelope with
                {
                    StartedAtUtc = envelope.StartedAtUtc ?? DateTime.UtcNow,
                    CompletedAtUtc = IsTerminalQueuedRunStatus(nextStatus) ? DateTime.UtcNow : null,
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
            Conditions = etag is null
                ? new BlobRequestConditions { IfNoneMatch = ETag.All }
                : new BlobRequestConditions { IfMatch = etag.Value }
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
            null) { CaseFingerprints = message.CaseFingerprints,
                RunKind = message.CaseFingerprints is null ? null : message.RunKind,
                AuthorityLane = message.CaseFingerprints is null ? null : message.AuthorityLane };

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
    IReadOnlyList<string> RequestedModels,
    IReadOnlyList<string> ServiceTiers,
    int InputTokens,
    int OutputTokens,
    int ReasoningTokens,
    decimal? VerifiedCostUsd,
    IReadOnlyList<string> CostEvidence,
    IReadOnlyList<string> IncentiveEvidence,
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
    string? Error)
{
    public IReadOnlyDictionary<string, string>? CaseFingerprints { get; init; }
    public string? RunKind { get; init; }
    public string? AuthorityLane { get; init; }
    public IReadOnlyList<string>? PublishedCaseIds { get; init; }
    public bool LegacyFanOutReady { get; init; }
}

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
    IReadOnlyList<string>? OperatorNextActions = null)
{
    public IReadOnlyList<PublicKnowledgeRunStorageService.PublicKnowledgeRunReference>? SourceRuns { get; init; }
}

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
