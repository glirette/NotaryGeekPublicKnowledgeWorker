using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotaryGeek.PublicKnowledge.Worker.Configuration;

namespace NotaryGeek.PublicKnowledge.Worker.Services;

public sealed class PublicKnowledgeSourceIndexService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string PublishedLawSourceCachePath = "/law-source-cache/source-cache-manifest.json";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PublicKnowledgeOptions _options;
    private readonly ILogger<PublicKnowledgeSourceIndexService> _logger;

    public PublicKnowledgeSourceIndexService(
        IHttpClientFactory httpClientFactory,
        IOptions<PublicKnowledgeOptions> options,
        ILogger<PublicKnowledgeSourceIndexService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PublicLawSourceIndex> ReadLawSourceIndexAsync(CancellationToken cancellationToken)
    {
        var localPath = Path.Combine(AppContext.BaseDirectory, _options.LocalLawSourceIndexPath);
        if (!File.Exists(localPath))
        {
            return new PublicLawSourceIndex(
                "notary-geek-public-law-source-index-missing",
                "missing",
                DateTime.UtcNow.ToString("O"),
                "Notary Law Source Index",
                $"Law source index was not found at {localPath}.",
                true,
                true,
                "missing",
                "Public sources only.",
                "Prefer official sources.",
                [],
                []);
        }

        var json = await File.ReadAllTextAsync(localPath, cancellationToken);
        return JsonSerializer.Deserialize<PublicLawSourceIndex>(json, JsonOptions)
            ?? throw new JsonException("Law source index JSON could not be parsed.");
    }

    public async Task<PublicLawSourceIndexSummary> BuildLawSourceIndexSummaryAsync(
        string? state,
        CancellationToken cancellationToken)
    {
        var index = await ReadLawSourceIndexAsync(cancellationToken);
        var jurisdictions = FilterJurisdictions(index.Jurisdictions, state).ToArray();
        var sourceCount = jurisdictions.Sum(item => item.Sources.Count);

        return new PublicLawSourceIndexSummary(
            "notary-geek-public-law-source-index-summary-v1",
            "0.1-public",
            DateTime.UtcNow,
            index.Version,
            index.Status,
            NormalizeState(state),
            jurisdictions.Length,
            sourceCount,
            jurisdictions);
    }

    public async Task<PublicLawSourceHealthReport> CheckLawSourceHealthAsync(
        string? state,
        int take,
        CancellationToken cancellationToken)
    {
        var index = await ReadLawSourceIndexAsync(cancellationToken);
        var selectedSources = FilterJurisdictions(index.Jurisdictions, state)
            .SelectMany(jurisdiction => jurisdiction.Sources.Select(source => (Jurisdiction: jurisdiction, Source: source)))
            .Take(Math.Clamp(take, 1, 100))
            .ToArray();

        var client = _httpClientFactory.CreateClient(nameof(PublicKnowledgeSourceIndexService));
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);

        var checks = new List<PublicLawSourceHealthCheck>();
        foreach (var item in selectedSources)
        {
            checks.Add(await CheckSourceAsync(client, item.Jurisdiction, item.Source, cancellationToken));
        }

        var okCount = checks.Count(item => item.Ok);
        return new PublicLawSourceHealthReport(
            "notary-geek-public-law-source-health-report-v1",
            "0.1-public",
            DateTime.UtcNow,
            NormalizeState(state),
            checks.Count,
            okCount,
            checks.Count - okCount,
            checks);
    }

    public async Task<PublicLawSourceCacheStatusReport> BuildPublishedLawSourceCacheStatusAsync(
        string? jurisdiction,
        int take,
        CancellationToken cancellationToken)
    {
        var generatedAtUtc = DateTime.UtcNow;
        var normalizedJurisdiction = NormalizeState(jurisdiction);
        var manifestUrl = BuildPublicUrl(PublishedLawSourceCachePath);

        if (!TryBuildAllowedPublicUri(manifestUrl, out var uri, out var reason))
        {
            return PublicLawSourceCacheStatusReport.Failed(
                generatedAtUtc,
                manifestUrl,
                normalizedJurisdiction,
                reason);
        }

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(PublicKnowledgeSourceIndexService));
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var statusCode = (int)response.StatusCode;
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return PublicLawSourceCacheStatusReport.Failed(
                    generatedAtUtc,
                    manifestUrl,
                    normalizedJurisdiction,
                    $"HTTP {statusCode}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var manifest = await JsonSerializer.DeserializeAsync<PublishedLawSourceCacheManifest>(stream, JsonOptions, cancellationToken);
            if (manifest is null)
            {
                return PublicLawSourceCacheStatusReport.Failed(
                    generatedAtUtc,
                    manifestUrl,
                    normalizedJurisdiction,
                    "Published law-source cache manifest could not be parsed.");
            }

            var selected = FilterPublishedSources(manifest.Sources, normalizedJurisdiction)
                .Select(source => BuildPublishedSourceStatus(source, generatedAtUtc))
                .OrderByDescending(item => item.NeedsReview)
                .ThenBy(item => item.NextReviewDue)
                .ThenBy(item => item.Jurisdiction)
                .ThenBy(item => item.SourceKey)
                .Take(Math.Clamp(take, 1, 250))
                .ToArray();

            return new PublicLawSourceCacheStatusReport(
                "notary-geek-published-law-source-cache-status-v1",
                "0.1-public",
                generatedAtUtc,
                manifestUrl,
                true,
                "reachable",
                normalizedJurisdiction,
                manifest.Schema,
                manifest.LastUpdatedUtc,
                manifest.DefaultReviewCadenceDays,
                manifest.Rules.RefreshAfterDays,
                manifest.Sources.Count,
                selected.Length,
                selected.Count(item => item.NeedsReview),
                selected.Count(item => item.IsFresh),
                selected);
        }
        catch (Exception ex) when (ex is (HttpRequestException or JsonException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not read published law-source cache manifest {ManifestUrl}.", manifestUrl);
            return PublicLawSourceCacheStatusReport.Failed(
                generatedAtUtc,
                manifestUrl,
                normalizedJurisdiction,
                ex.Message);
        }
    }

    private async Task<PublicLawSourceHealthCheck> CheckSourceAsync(
        HttpClient client,
        PublicLawJurisdiction jurisdiction,
        PublicLawSource source,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryBuildAllowedPublicUri(source.Url, out var uri, out var reason))
            {
                return new PublicLawSourceHealthCheck(
                    jurisdiction.Id,
                    jurisdiction.State,
                    source.Id,
                    source.Title,
                    source.SourceType,
                    source.Publisher,
                    source.Url,
                    false,
                    0,
                    null,
                    null,
                    reason);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var statusCode = (int)response.StatusCode;
            var ok = response.StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.BadRequest;
            return new PublicLawSourceHealthCheck(
                jurisdiction.Id,
                jurisdiction.State,
                source.Id,
                source.Title,
                source.SourceType,
                source.Publisher,
                source.Url,
                ok,
                statusCode,
                response.Content.Headers.ContentType?.ToString(),
                response.RequestMessage?.RequestUri?.ToString(),
                ok ? "reachable" : $"HTTP {statusCode}");
        }
        catch (Exception ex) when (ex is (HttpRequestException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not check public law source {Url}.", source.Url);
            return new PublicLawSourceHealthCheck(
                jurisdiction.Id,
                jurisdiction.State,
                source.Id,
                source.Title,
                source.SourceType,
                source.Publisher,
                source.Url,
                false,
                0,
                null,
                null,
                ex.Message);
        }
    }

    private static IEnumerable<PublicLawJurisdiction> FilterJurisdictions(
        IReadOnlyList<PublicLawJurisdiction> jurisdictions,
        string? state)
    {
        var normalized = NormalizeState(state);
        return string.IsNullOrWhiteSpace(normalized)
            ? jurisdictions
            : jurisdictions.Where(item => item.State.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<PublishedLawSourceRecord> FilterPublishedSources(
        IReadOnlyList<PublishedLawSourceRecord> sources,
        string? jurisdiction)
    {
        return string.IsNullOrWhiteSpace(jurisdiction)
            ? sources
            : sources.Where(item =>
                item.Jurisdiction.Equals(jurisdiction, StringComparison.OrdinalIgnoreCase) ||
                StateNameToCode(item.Jurisdiction).Equals(jurisdiction, StringComparison.OrdinalIgnoreCase));
    }

    private static PublicLawSourceCacheRecordStatus BuildPublishedSourceStatus(
        PublishedLawSourceRecord source,
        DateTime generatedAtUtc)
    {
        var lastCheckedUtc = source.LastCheckedUtc?.UtcDateTime;
        int? ageDays = lastCheckedUtc is null
            ? null
            : Math.Max(0, (int)Math.Floor((generatedAtUtc - lastCheckedUtc.Value).TotalDays));
        var nextReviewDue = TryParseDate(source.NextReviewDue);
        var dueByDate = nextReviewDue is not null && nextReviewDue.Value.Date <= generatedAtUtc.Date;
        var dueByAge = ageDays is not null && ageDays.Value >= Math.Max(1, source.ReviewCadenceDays);
        var needsReview = dueByDate || dueByAge || lastCheckedUtc is null;
        var isFresh = !needsReview;

        return new PublicLawSourceCacheRecordStatus(
            source.SourceKey,
            source.Jurisdiction,
            StateNameToCode(source.Jurisdiction),
            source.Topic,
            source.SourceType,
            source.OfficialSourceName,
            source.OfficialSourceUrl,
            source.Status,
            lastCheckedUtc,
            ageDays,
            source.ReviewCadenceDays,
            nextReviewDue,
            source.BillWatchRequired,
            source.BillWatchLastCheckedUtc?.UtcDateTime,
            isFresh,
            needsReview,
            BuildReviewReason(lastCheckedUtc, ageDays, source.ReviewCadenceDays, nextReviewDue, generatedAtUtc));
    }

    private string BuildPublicUrl(string path) =>
        $"{_options.PublicBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private static string? NormalizeState(string? state) =>
        string.IsNullOrWhiteSpace(state) ? null : state.Trim().ToUpperInvariant();

    private static DateTime? TryParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParse(value, out var parsed)
            ? parsed.Date
            : null;
    }

    private static string BuildReviewReason(
        DateTime? lastCheckedUtc,
        int? ageDays,
        int reviewCadenceDays,
        DateTime? nextReviewDue,
        DateTime generatedAtUtc)
    {
        if (lastCheckedUtc is null)
        {
            return "missing last checked date";
        }

        if (nextReviewDue is not null && nextReviewDue.Value.Date <= generatedAtUtc.Date)
        {
            return $"review due {nextReviewDue.Value:yyyy-MM-dd}";
        }

        if (ageDays is not null && ageDays.Value >= Math.Max(1, reviewCadenceDays))
        {
            return $"checked {ageDays.Value} day(s) ago; cadence is {reviewCadenceDays} day(s)";
        }

        return "within review cadence";
    }

    private static string StateNameToCode(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "california" => "CA",
            "florida" => "FL",
            "new york" => "NY",
            "texas" => "TX",
            "virginia" => "VA",
            "wyoming" => "WY",
            _ => value.Trim().ToUpperInvariant()
        };
    }

    private bool TryBuildAllowedPublicUri(string url, out Uri? uri, out string reason)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
        {
            reason = "URL is not absolute.";
            return false;
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            reason = "Only HTTPS URLs are allowed.";
            return false;
        }

        var allowedHosts = SplitList(_options.AllowedSourceHosts);
        if (!allowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            reason = $"Host '{uri.Host}' is not allowlisted.";
            return false;
        }

        reason = "allowed";
        return true;
    }

    private static IReadOnlyList<string> SplitList(string value) =>
        value
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}

public sealed record PublicLawSourceIndex(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("reviewedUtc")] string ReviewedUtc,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("purpose")] string Purpose,
    [property: JsonPropertyName("notLegalAdvice")] bool NotLegalAdvice,
    [property: JsonPropertyName("sourceAuthorityFirst")] bool SourceAuthorityFirst,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("publicBoundary")] string PublicBoundary,
    [property: JsonPropertyName("sourceQualityRule")] string SourceQualityRule,
    [property: JsonPropertyName("jurisdictions")] IReadOnlyList<PublicLawJurisdiction> Jurisdictions,
    [property: JsonPropertyName("nextIndexingTargets")] IReadOnlyList<string> NextIndexingTargets);

public sealed record PublicLawJurisdiction(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("country")] string Country,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("sources")] IReadOnlyList<PublicLawSource> Sources);

public sealed record PublicLawSource(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("sourceType")] string SourceType,
    [property: JsonPropertyName("publisher")] string Publisher,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("notes")] string Notes);

public sealed record PublicLawSourceIndexSummary(
    string Schema,
    string Version,
    DateTime GeneratedAtUtc,
    string IndexVersion,
    string IndexStatus,
    string? State,
    int JurisdictionCount,
    int SourceCount,
    IReadOnlyList<PublicLawJurisdiction> Jurisdictions);

public sealed record PublicLawSourceHealthReport(
    string Schema,
    string Version,
    DateTime GeneratedAtUtc,
    string? State,
    int CheckedCount,
    int OkCount,
    int FailedCount,
    IReadOnlyList<PublicLawSourceHealthCheck> Checks);

public sealed record PublicLawSourceHealthCheck(
    string JurisdictionId,
    string State,
    string SourceId,
    string Title,
    string SourceType,
    string Publisher,
    string Url,
    bool Ok,
    int StatusCode,
    string? ContentType,
    string? FinalUrl,
    string Note);

public sealed record PublishedLawSourceCacheManifest(
    string Schema,
    DateTimeOffset LastUpdatedUtc,
    int DefaultReviewCadenceDays,
    PublishedLawSourceCacheRules Rules,
    IReadOnlyList<PublishedLawSourceRecord> Sources);

public sealed record PublishedLawSourceCacheRules(
    bool LocalFirst,
    int RefreshAfterDays,
    IReadOnlyList<string> RefreshEarlierWhen,
    IReadOnlyList<string> ContentOutputTargets);

public sealed record PublishedLawSourceRecord(
    string SourceKey,
    string Jurisdiction,
    string Topic,
    string SourceType,
    string OfficialSourceName,
    string OfficialSourceUrl,
    DateTimeOffset? LastCheckedUtc,
    string VisibleSourceDate,
    string EffectiveDate,
    int ReviewCadenceDays,
    string NextReviewDue,
    bool BillWatchRequired,
    DateTimeOffset? BillWatchLastCheckedUtc,
    string Status,
    IReadOnlyList<string> ContentUses,
    IReadOnlyList<string> ClaimsSupported,
    IReadOnlyList<string> DoNotSay,
    IReadOnlyList<string> LocalContentTargets,
    IReadOnlyList<string> SourceNotes);

public sealed record PublicLawSourceCacheStatusReport(
    string Schema,
    string Version,
    DateTime GeneratedAtUtc,
    string ManifestUrl,
    bool Reachable,
    string Status,
    string? Jurisdiction,
    string? ManifestSchema,
    DateTimeOffset? ManifestLastUpdatedUtc,
    int DefaultReviewCadenceDays,
    int RefreshAfterDays,
    int ManifestSourceCount,
    int ReturnedSourceCount,
    int NeedsReviewCount,
    int FreshCount,
    IReadOnlyList<PublicLawSourceCacheRecordStatus> Sources)
{
    public static PublicLawSourceCacheStatusReport Failed(
        DateTime generatedAtUtc,
        string manifestUrl,
        string? jurisdiction,
        string status) =>
        new(
            "notary-geek-published-law-source-cache-status-v1",
            "0.1-public",
            generatedAtUtc,
            manifestUrl,
            false,
            status,
            jurisdiction,
            null,
            null,
            0,
            0,
            0,
            0,
            0,
            0,
            []);
}

public sealed record PublicLawSourceCacheRecordStatus(
    string SourceKey,
    string Jurisdiction,
    string State,
    string Topic,
    string SourceType,
    string OfficialSourceName,
    string OfficialSourceUrl,
    string Status,
    DateTime? LastCheckedUtc,
    int? AgeDays,
    int ReviewCadenceDays,
    DateTime? NextReviewDue,
    bool BillWatchRequired,
    DateTime? BillWatchLastCheckedUtc,
    bool IsFresh,
    bool NeedsReview,
    string ReviewReason);
