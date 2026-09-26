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
            var candidateId = CreateCandidateId(destination, draft, evidence);
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
            var expected = BinaryData.FromObjectAsJson(candidate, JsonOptions);
            var blob = container.GetBlobClient(blobName);
            try
            {
                await blob.UploadAsync(
                    expected,
                    new BlobUploadOptions
                    {
                        Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                        HttpHeaders = new BlobHttpHeaders { ContentType = "application/json; charset=utf-8" }
                    },
                    cancellationToken);
                saved++;
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412)
            {
                var existing = await blob.DownloadContentAsync(cancellationToken);
                using var actualDocument = JsonDocument.Parse(existing.Value.Content);
                using var expectedDocument = JsonDocument.Parse(expected);
                if (!JsonElement.DeepEquals(actualDocument.RootElement, expectedDocument.RootElement))
                    throw new InvalidOperationException("Conflicting immutable promotion candidate.");
            }
        }

        return saved;
    }

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

    private static string CreateCandidateId(string destination, PublicAuthorityCandidateDraft candidate,
        PublicAuthorityGeneratorEvidence evidence)
    {
        var canonical = JsonSerializer.Serialize(new { destination, candidate, evidence }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string GetDestination(string authorityLane) =>
        authorityLane.Equals("technical", StringComparison.OrdinalIgnoreCase)
            ? "technical-review"
            : "glirette/NotaryGeekPublicKnowledgeWorker";

    private static string ToSafeSegment(string value) =>
        new(value.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray());
}
