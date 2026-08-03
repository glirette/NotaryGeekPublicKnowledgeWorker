using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NotaryGeek.PublicKnowledge.Worker.Configuration;
using NotaryGeek.PublicKnowledge.Worker.Models;

namespace NotaryGeek.PublicKnowledge.Worker.Services;

public sealed class PublicKnowledgePromotionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IConfiguration _configuration;
    private readonly PublicKnowledgeOptions _options;

    public PublicKnowledgePromotionService(
        IConfiguration configuration,
        IOptions<PublicKnowledgeOptions> options)
    {
        _configuration = configuration;
        _options = options.Value;
    }

    public async Task<int> SaveValidatedCandidatesAsync(
        PublicKnowledgeRunResult result,
        string runId,
        CancellationToken cancellationToken)
    {
        if (!result.Ok || result.StructuredOutput is null || result.ProviderEvidence is null)
        {
            return 0;
        }

        var destination = GetDestination(result.AuthorityLane);
        var evidence = new PublicAuthorityGeneratorEvidence(
            result.ProviderEvidence.Provider.ToLowerInvariant(),
            result.ProviderEvidence.AuthMode,
            result.ProviderEvidence.Model,
            runId,
            result.CheckedAtUtc,
            new PublicAuthorityUsage(
                result.ProviderEvidence.InputTokens,
                result.ProviderEvidence.OutputTokens,
                result.ProviderEvidence.ReasoningTokens));
        var container = await GetContainerAsync(cancellationToken);
        var saved = 0;
        foreach (var draft in result.StructuredOutput.Candidates)
        {
            var candidateId = CreateCandidateId(destination, draft);
            var candidate = new PublicAuthorityCandidate(
                candidateId,
                destination,
                draft.TopicId,
                draft.Title,
                draft.Summary,
                draft.ReviewedAtUtc,
                draft.RecheckBeforeUse,
                draft.Sources,
                draft.Supports,
                draft.DoesNotProve,
                evidence);
            var blobName = $"promotion/candidates/{ToSafeSegment(destination)}/{candidateId}.json";
            try
            {
                await container.GetBlobClient(blobName).UploadAsync(
                    BinaryData.FromObjectAsJson(candidate, JsonOptions),
                    new BlobUploadOptions
                    {
                        Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                        HttpHeaders = new BlobHttpHeaders { ContentType = "application/json; charset=utf-8" }
                    },
                    cancellationToken);
                saved++;
            }
            catch (RequestFailedException ex) when (ex.Status == 409 || ex.Status == 412)
            {
                // Deterministic candidate IDs make duplicate daily selections a no-op.
            }
        }

        return saved;
    }

    public async Task<PublicAuthorityCandidateFeed> ReadFeedAsync(
        string destination,
        CancellationToken cancellationToken)
    {
        var normalizedDestination = NormalizeDestination(destination);
        var container = await GetContainerAsync(cancellationToken);
        var promotedCandidateIds = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var blob in container.GetBlobsAsync(
                           prefix: $"promotion/promotions/{ToSafeSegment(normalizedDestination)}/",
                           cancellationToken: cancellationToken))
        {
            promotedCandidateIds.Add(Path.GetFileNameWithoutExtension(blob.Name));
        }

        var candidates = new List<PublicAuthorityCandidate>();
        await foreach (var blob in container.GetBlobsAsync(
                           prefix: $"promotion/candidates/{ToSafeSegment(normalizedDestination)}/",
                           cancellationToken: cancellationToken))
        {
            try
            {
                var content = await container.GetBlobClient(blob.Name).DownloadContentAsync(cancellationToken);
                var candidate = content.Value.Content.ToObjectFromJson<PublicAuthorityCandidate>(JsonOptions);
                if (candidate is not null &&
                    !promotedCandidateIds.Contains(candidate.CandidateId) &&
                    IsSafeForPublicFeed(candidate, normalizedDestination))
                {
                    candidates.Add(candidate);
                }
            }
            catch (Exception ex) when (ex is JsonException or RequestFailedException)
            {
                // A corrupt candidate never enters the public feed.
            }
        }

        var selected = candidates
            .OrderByDescending(item => item.GeneratorEvidence.GeneratedAtUtc)
            .Take(Math.Clamp(_options.PromotionFeedMaxCandidates, 1, 20))
            .ToArray();
        return new PublicAuthorityCandidateFeed(
            GetFeedSchema(normalizedDestination),
            DateTime.UtcNow,
            selected);
    }

    public async Task RecordPromotionAsync(
        PublicAuthorityPromotionReceipt receipt,
        CancellationToken cancellationToken)
    {
        var destination = ValidateReceiptIdentity(
            receipt.CandidateId,
            receipt.Destination,
            receipt.PromotedAtUtc,
            receipt.PullRequestUrl,
            "Promotion");
        var container = await GetContainerAsync(cancellationToken);
        await ValidateStoredCandidateAsync(container, receipt.CandidateId, destination, cancellationToken);
        var blobName = $"promotion/promotions/{ToSafeSegment(destination)}/{receipt.CandidateId}.json";
        await UploadReceiptOnceAsync(
            container,
            blobName,
            receipt,
            existing => existing.CandidateId == receipt.CandidateId &&
                        existing.Destination == destination &&
                        existing.PullRequestUrl == receipt.PullRequestUrl,
            cancellationToken);
    }

    public async Task RecordPublicationAsync(
        PublicAuthorityPublicationReceipt receipt,
        CancellationToken cancellationToken)
    {
        var destination = ValidateReceiptIdentity(
            receipt.CandidateId,
            receipt.Destination,
            receipt.PublishedAtUtc,
            receipt.PullRequestUrl,
            "Publication");
        var container = await GetContainerAsync(cancellationToken);
        await ValidateStoredCandidateAsync(container, receipt.CandidateId, destination, cancellationToken);

        var promotionBlob = container.GetBlobClient(
            $"promotion/promotions/{ToSafeSegment(destination)}/{receipt.CandidateId}.json");
        PublicAuthorityPromotionReceipt promotion;
        try
        {
            var content = await promotionBlob.DownloadContentAsync(cancellationToken);
            promotion = content.Value.Content.ToObjectFromJson<PublicAuthorityPromotionReceipt>(JsonOptions)
                ?? throw new ArgumentException("Publication acknowledgement requires an existing promotion receipt.");
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new ArgumentException("Publication acknowledgement requires an existing promotion receipt.");
        }

        if (promotion.CandidateId != receipt.CandidateId ||
            promotion.Destination != destination ||
            promotion.PullRequestUrl != receipt.PullRequestUrl)
        {
            throw new ArgumentException("Publication acknowledgement must match the promoted candidate, destination, and pull request URL.");
        }

        var blobName = $"promotion/publications/{ToSafeSegment(destination)}/{receipt.CandidateId}.json";
        await UploadReceiptOnceAsync(
            container,
            blobName,
            receipt,
            existing => existing.CandidateId == receipt.CandidateId &&
                        existing.Destination == destination &&
                        existing.PullRequestUrl == receipt.PullRequestUrl,
            cancellationToken);
    }

    public async Task<PublicAuthorityPromotionStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        var candidateCount = 0;
        var promotedCandidateCount = 0;
        var publishedCandidateCount = 0;
        DateTime? lastCandidateUtc = null;
        DateTime? lastPromotionUtc = null;
        DateTime? lastPublicationUtc = null;
        await foreach (var blob in container.GetBlobsAsync(prefix: "promotion/", cancellationToken: cancellationToken))
        {
            if (blob.Name.StartsWith("promotion/candidates/", StringComparison.Ordinal))
            {
                candidateCount++;
                lastCandidateUtc = Max(lastCandidateUtc, blob.Properties.LastModified?.UtcDateTime);
            }
            else if (blob.Name.StartsWith("promotion/promotions/", StringComparison.Ordinal))
            {
                try
                {
                    var content = await container.GetBlobClient(blob.Name).DownloadContentAsync(cancellationToken);
                    var receipt = content.Value.Content.ToObjectFromJson<PublicAuthorityPromotionReceipt>(JsonOptions);
                    if (receipt is not null)
                    {
                        promotedCandidateCount++;
                        lastPromotionUtc = Max(lastPromotionUtc, receipt.PromotedAtUtc);
                    }
                }
                catch (Exception ex) when (ex is JsonException or RequestFailedException)
                {
                    // Invalid receipts are not reported as successful promotions.
                }
            }
            else if (blob.Name.StartsWith("promotion/publications/", StringComparison.Ordinal))
            {
                try
                {
                    var content = await container.GetBlobClient(blob.Name).DownloadContentAsync(cancellationToken);
                    var receipt = content.Value.Content.ToObjectFromJson<PublicAuthorityPublicationReceipt>(JsonOptions);
                    if (receipt is not null)
                    {
                        publishedCandidateCount++;
                        lastPublicationUtc = Max(lastPublicationUtc, receipt.PublishedAtUtc);
                    }
                }
                catch (Exception ex) when (ex is JsonException or RequestFailedException)
                {
                    // Invalid receipts are not reported as successful publications.
                }
            }
        }

        return new PublicAuthorityPromotionStatus(
            candidateCount,
            promotedCandidateCount,
            publishedCandidateCount,
            lastCandidateUtc,
            lastPromotionUtc,
            lastPublicationUtc,
            _options.RunHistoryRetentionDays,
            _options.JobEnvelopeRetentionDays,
            "policy-only-no-deletion");
    }

    public static string GetDestination(string authorityLane) =>
        authorityLane.Equals("technical", StringComparison.OrdinalIgnoreCase)
            ? "glirette/thisstuffiswaytootech"
            : "glirette/NotaryGeekPublicKnowledgeWorker";

    private async Task<BlobContainerClient> GetContainerAsync(CancellationToken cancellationToken)
    {
        var connectionString = _configuration[_options.OutputStorageConnectionStringSetting];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Storage setting '{_options.OutputStorageConnectionStringSetting}' is not configured.");
        }

        var container = new BlobContainerClient(connectionString, _options.OutputContainerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        return container;
    }

    private static string CreateCandidateId(string destination, PublicAuthorityCandidateDraft candidate)
    {
        var canonical = string.Join("\n", new[]
        {
            destination.ToLowerInvariant(),
            candidate.TopicId.Trim().ToLowerInvariant(),
            candidate.Summary.Trim(),
            string.Join("\n", candidate.Sources.Select(item => item.Url.Trim()).Order(StringComparer.OrdinalIgnoreCase))
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string NormalizeDestination(string destination)
    {
        var known = new[] { GetDestination("notary"), GetDestination("technical") };
        return known.FirstOrDefault(item => item.Equals(destination, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Unknown public-authority destination.");
    }

    public static string ValidateReceiptIdentity(
        string candidateId,
        string destination,
        DateTime eventUtc,
        string pullRequestUrl,
        string receiptKind)
    {
        var normalizedDestination = NormalizeDestination(destination);
        if (!destination.Equals(normalizedDestination, StringComparison.Ordinal))
        {
            throw new ArgumentException($"{receiptKind} acknowledgement destination must use the canonical repository name.");
        }

        if (string.IsNullOrWhiteSpace(candidateId) ||
            candidateId.Length != 64 ||
            candidateId.Any(ch => !char.IsAsciiHexDigit(ch) || char.IsUpper(ch)))
        {
            throw new ArgumentException($"{receiptKind} acknowledgement requires a lowercase SHA-256 candidate ID.");
        }

        if (eventUtc == default || eventUtc > DateTime.UtcNow.AddMinutes(5))
        {
            throw new ArgumentException($"{receiptKind} acknowledgement timestamp is invalid.");
        }

        if (!Uri.TryCreate(pullRequestUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException($"{receiptKind} acknowledgement requires an exact public GitHub pull-request URL.");
        }

        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 4 ||
            !parts[0].Equals(normalizedDestination.Split('/')[0], StringComparison.OrdinalIgnoreCase) ||
            !parts[1].Equals(normalizedDestination.Split('/')[1], StringComparison.OrdinalIgnoreCase) ||
            parts[2] != "pull" ||
            !int.TryParse(parts[3], out var pullNumber) ||
            pullNumber < 1)
        {
            throw new ArgumentException($"{receiptKind} acknowledgement pull-request URL does not match the destination repository.");
        }

        return normalizedDestination;
    }

    private static async Task ValidateStoredCandidateAsync(
        BlobContainerClient container,
        string candidateId,
        string destination,
        CancellationToken cancellationToken)
    {
        var blob = container.GetBlobClient(
            $"promotion/candidates/{ToSafeSegment(destination)}/{candidateId}.json");
        PublicAuthorityCandidate candidate;
        try
        {
            var content = await blob.DownloadContentAsync(cancellationToken);
            candidate = content.Value.Content.ToObjectFromJson<PublicAuthorityCandidate>(JsonOptions)
                ?? throw new ArgumentException("Acknowledgement candidate does not exist in validated promotion storage.");
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            throw new ArgumentException("Acknowledgement candidate does not exist in validated promotion storage.");
        }

        if (candidate.CandidateId != candidateId || candidate.Destination != destination)
        {
            throw new ArgumentException("Acknowledgement candidate identity does not match validated promotion storage.");
        }
    }

    private static async Task UploadReceiptOnceAsync<T>(
        BlobContainerClient container,
        string blobName,
        T receipt,
        Func<T, bool> isSameReceipt,
        CancellationToken cancellationToken)
    {
        var blob = container.GetBlobClient(blobName);
        try
        {
            await blob.UploadAsync(
                BinaryData.FromObjectAsJson(receipt, JsonOptions),
                new BlobUploadOptions
                {
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json; charset=utf-8" }
                },
                cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            var content = await blob.DownloadContentAsync(cancellationToken);
            var existing = content.Value.Content.ToObjectFromJson<T>(JsonOptions);
            if (existing is null || !isSameReceipt(existing))
            {
                throw new ArgumentException("A conflicting acknowledgement already exists for this candidate.");
            }
        }
    }

    private static string GetFeedSchema(string destination) =>
        destination.Equals(GetDestination("technical"), StringComparison.OrdinalIgnoreCase)
            ? "technical-source-candidate-feed/v1"
            : "notary-public-authority-candidate-feed/v1";

    private static bool IsSafeForPublicFeed(PublicAuthorityCandidate candidate, string destination) =>
        candidate.Destination.Equals(destination, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(candidate.CandidateId) &&
        !string.IsNullOrWhiteSpace(candidate.TopicId) &&
        !string.IsNullOrWhiteSpace(candidate.Title) &&
        !string.IsNullOrWhiteSpace(candidate.Summary) &&
        candidate.RecheckBeforeUse &&
        candidate.Sources is { Count: >= 1 and <= 12 } &&
        candidate.Sources.All(source =>
            source is not null &&
            Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            !string.IsNullOrWhiteSpace(source.Title) &&
            !string.IsNullOrWhiteSpace(source.Publisher) &&
            !string.IsNullOrWhiteSpace(source.Supports)) &&
        candidate.GeneratorEvidence is not null &&
        candidate.GeneratorEvidence.Provider.Equals("openai", StringComparison.OrdinalIgnoreCase) &&
        candidate.GeneratorEvidence.AuthMode.Equals("dedicated_public_source_key", StringComparison.Ordinal);

    private static string ToSafeSegment(string value) =>
        new(value.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray());

    private static DateTime? Max(DateTime? current, DateTime? candidate) =>
        !current.HasValue || candidate > current ? candidate : current;
}
