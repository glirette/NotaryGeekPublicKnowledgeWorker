using System.Text.Json;
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
            latestBlobName);
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
                envelope.LatestBlobName))
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
            response.Citations);
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
    string LatestBlobName);

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
    string LatestBlobName);

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
    IReadOnlyList<string> Citations);
