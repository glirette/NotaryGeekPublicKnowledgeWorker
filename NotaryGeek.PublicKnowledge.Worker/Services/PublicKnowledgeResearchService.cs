using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotaryGeek.PublicKnowledge.Worker.Configuration;
using NotaryGeek.PublicKnowledge.Worker.Models;

namespace NotaryGeek.PublicKnowledge.Worker.Services;

public sealed class PublicKnowledgeResearchService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PublicKnowledgeOptions _knowledgeOptions;
    private readonly OpenAiOptions _openAiOptions;
    private readonly ILogger<PublicKnowledgeResearchService> _logger;

    public PublicKnowledgeResearchService(
        IHttpClientFactory httpClientFactory,
        IOptions<PublicKnowledgeOptions> knowledgeOptions,
        IOptions<OpenAiOptions> openAiOptions,
        ILogger<PublicKnowledgeResearchService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _knowledgeOptions = knowledgeOptions.Value;
        _openAiOptions = openAiOptions.Value;
        _logger = logger;
    }

    public PublicKnowledgeStatus GetStatus()
    {
        var hosts = SplitList(_knowledgeOptions.AllowedSourceHosts);
        return new PublicKnowledgeStatus(
            DateTime.UtcNow,
            _knowledgeOptions.Enabled,
            _knowledgeOptions.TimerEnabled,
            _knowledgeOptions.PublicBaseUrl,
            string.IsNullOrWhiteSpace(_knowledgeOptions.PublicCorpusManifestUrl) ? "bundled-local" : _knowledgeOptions.PublicCorpusManifestUrl,
            _openAiOptions.BaseUrl,
            _openAiOptions.EndpointPath,
            _openAiOptions.Model,
            !string.IsNullOrWhiteSpace(_openAiOptions.ApiKey),
            _knowledgeOptions.MaxSourcesPerRun,
            _knowledgeOptions.MaxEstimatedInputTokens,
            hosts);
    }

    public async Task<PublicKnowledgeRunResult> RunAsync(
        PublicKnowledgeRunCommand command,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var errors = new List<string>();
        var sourceResults = new List<PublicKnowledgeSourceResult>();
        var sourceBodies = new List<SourceBody>();

        var manifest = await LoadManifestAsync(warnings, cancellationToken);
        var urls = SelectSourceUrls(manifest, command.RequestedUrls, warnings);
        var client = CreateFetchClient();

        foreach (var url in urls.Take(_knowledgeOptions.MaxSourcesPerRun))
        {
            if (!IsAllowedPublicUrl(url, out var reason))
            {
                sourceResults.Add(new PublicKnowledgeSourceResult(url, false, 0, null, 0, reason));
                warnings.Add($"Skipped {url}: {reason}");
                continue;
            }

            var fetched = await FetchSourceAsync(client, url, cancellationToken);
            sourceResults.Add(fetched.Result);
            if (fetched.Body is not null)
            {
                sourceBodies.Add(fetched.Body);
            }
        }

        var prompt = BuildPrompt(manifest, command.Focus, sourceBodies);
        var promptCharacters = prompt.Length;
        var estimatedInputTokens = EstimateTokens(promptCharacters);

        if (estimatedInputTokens > _knowledgeOptions.MaxEstimatedInputTokens)
        {
            errors.Add($"Estimated input tokens {estimatedInputTokens} exceed configured limit {_knowledgeOptions.MaxEstimatedInputTokens}.");
        }

        var shouldCallOpenAi = command.Execute && errors.Count == 0;
        if (shouldCallOpenAi && !_knowledgeOptions.Enabled)
        {
            errors.Add("PublicKnowledge__Enabled is false. Dry-run is allowed, but OpenAI calls are disabled.");
        }

        if (shouldCallOpenAi && string.IsNullOrWhiteSpace(_openAiOptions.ApiKey))
        {
            errors.Add("OpenAI__ApiKey is not configured.");
        }

        if (!shouldCallOpenAi || errors.Count > 0)
        {
            return new PublicKnowledgeRunResult(
                Ok: errors.Count == 0,
                Execute: command.Execute,
                OpenAiCalled: false,
                Skipped: errors.Count > 0 || !command.Execute,
                Status: errors.Count > 0 ? "preflight-failed" : "dry-run",
                DateTime.UtcNow,
                command.Focus,
                sourceBodies.Count,
                promptCharacters,
                estimatedInputTokens,
                _openAiOptions.Model,
                sourceResults,
                ResponseText: null,
                ProviderStatus: null,
                ProviderUsageJson: null,
                warnings,
                errors);
        }

        var provider = await CallOpenAiAsync(prompt, cancellationToken);
        if (!string.IsNullOrWhiteSpace(provider.ResponseText))
        {
            var fetchedSourceUrls = sourceBodies
                .Select(source => source.Url)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            warnings.AddRange(ValidateProviderResponse(provider.ResponseText, fetchedSourceUrls));
        }

        if (!provider.Ok)
        {
            errors.Add(provider.Error ?? "OpenAI call failed.");
        }

        return new PublicKnowledgeRunResult(
            Ok: provider.Ok && errors.Count == 0,
            Execute: command.Execute,
            OpenAiCalled: true,
            Skipped: false,
            Status: provider.Ok ? "completed" : "provider-failed",
            DateTime.UtcNow,
            command.Focus,
            sourceBodies.Count,
            promptCharacters,
            estimatedInputTokens,
            _openAiOptions.Model,
            sourceResults,
            provider.ResponseText,
            provider.Status,
            provider.UsageJson,
            warnings,
            errors);
    }

    private HttpClient CreateFetchClient()
    {
        var client = _httpClientFactory.CreateClient(nameof(PublicKnowledgeResearchService));
        client.Timeout = TimeSpan.FromSeconds(45);
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(_knowledgeOptions.UserAgent);
        return client;
    }

    private async Task<PublicKnowledgeManifest> LoadManifestAsync(
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var manifestUrl = _knowledgeOptions.PublicCorpusManifestUrl;
        var reason = string.Empty;
        if (!string.IsNullOrWhiteSpace(manifestUrl) && IsAllowedPublicUrl(manifestUrl, out reason))
        {
            try
            {
                var client = CreateFetchClient();
                var json = await client.GetStringAsync(manifestUrl, cancellationToken);
                return ParseManifest(json);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                warnings.Add($"Remote manifest failed; using bundled manifest. Reason: {ex.Message}");
            }
        }
        else if (!string.IsNullOrWhiteSpace(manifestUrl))
        {
            warnings.Add($"Remote manifest ignored: {reason}");
        }

        var localPath = Path.Combine(AppContext.BaseDirectory, _knowledgeOptions.LocalManifestPath);
        if (!File.Exists(localPath))
        {
            warnings.Add("Bundled manifest was not found; using built-in defaults.");
            return BuildDefaultManifest();
        }

        var localJson = await File.ReadAllTextAsync(localPath, cancellationToken);
        return ParseManifest(localJson);
    }

    private PublicKnowledgeManifest ParseManifest(string json)
    {
        var dto = JsonSerializer.Deserialize<PublicKnowledgeManifestDto>(json, JsonOptions)
            ?? throw new JsonException("Manifest JSON could not be parsed.");

        var policy = new PublicKnowledgePolicy(
            dto.PublicOnlyPolicy?.PublicOnly ?? true,
            dto.PublicOnlyPolicy?.Summary ?? "Public source corpus only.");

        var sourceSets = (dto.SourceSets ?? [])
            .Select(item => new PublicKnowledgeSourceSet(
                item.Name ?? "Unnamed",
                item.Use ?? string.Empty,
                item.Urls ?? []))
            .ToArray();

        return new PublicKnowledgeManifest(
            dto.Schema ?? "notary-geek-public-knowledge-manifest-v1",
            dto.Version ?? "0.1-public",
            dto.Purpose ?? "Public source-of-truth corpus.",
            dto.CanonicalRoutingModel ?? BuildAbsoluteUrl("/notarial-routing-model.json"),
            policy,
            sourceSets,
            dto.StrictExclusions ?? []);
    }

    private PublicKnowledgeManifest BuildDefaultManifest()
    {
        var urls = SplitList(_knowledgeOptions.DefaultSourcePaths)
            .Select(BuildAbsoluteUrl)
            .ToArray();

        return new PublicKnowledgeManifest(
            "notary-geek-public-knowledge-manifest-v1",
            "0.1-public",
            "Fallback public source corpus.",
            BuildAbsoluteUrl("/notarial-routing-model.json"),
            new PublicKnowledgePolicy(true, "Public source corpus only."),
            [new PublicKnowledgeSourceSet("Default sources", "Fallback public corpus", urls)],
            ["Customer data", "Persona data", "Payment data", "Private correspondence"]);
    }

    private IReadOnlyList<string> SelectSourceUrls(
        PublicKnowledgeManifest manifest,
        IReadOnlyList<string> requestedUrls,
        List<string> warnings)
    {
        var urls = new List<string>();

        if (requestedUrls.Count > 0)
        {
            foreach (var requestedUrl in requestedUrls)
            {
                if (Uri.TryCreate(requestedUrl, UriKind.Absolute, out _))
                {
                    urls.Add(requestedUrl);
                }
                else if (requestedUrl.StartsWith("/", StringComparison.Ordinal))
                {
                    urls.Add(BuildAbsoluteUrl(requestedUrl));
                }
                else
                {
                    warnings.Add($"Ignored requested URL '{requestedUrl}' because it is not absolute or rooted.");
                }
            }
        }

        if (urls.Count == 0)
        {
            urls.AddRange(
                manifest.SourceSets
                    .SelectMany(set => set.Urls)
                    .Where(url => !string.IsNullOrWhiteSpace(url)));
        }

        return urls
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(_knowledgeOptions.MaxSourcesPerRun)
            .ToArray();
    }

    private async Task<(PublicKnowledgeSourceResult Result, SourceBody? Body)> FetchSourceAsync(
        HttpClient client,
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(url, cancellationToken);
            var contentType = response.Content.Headers.ContentType?.ToString();
            var statusCode = (int)response.StatusCode;

            if (response.StatusCode != HttpStatusCode.OK)
            {
                return (new PublicKnowledgeSourceResult(url, false, statusCode, contentType, 0, $"HTTP {statusCode}"), null);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length > _knowledgeOptions.MaxBytesPerSource)
            {
                return (new PublicKnowledgeSourceResult(url, false, statusCode, contentType, bytes.Length, $"Too large: {bytes.Length} bytes"), null);
            }

            var content = Encoding.UTF8.GetString(bytes);
            if (string.IsNullOrWhiteSpace(content))
            {
                return (new PublicKnowledgeSourceResult(url, false, statusCode, contentType, 0, "Empty response"), null);
            }

            var normalized = NormalizeSourceText(content, _knowledgeOptions.MaxCharactersPerSource, out var truncated);
            var note = truncated ? $"ok-truncated:{content.Length}->{normalized.Length}" : "ok";
            var result = new PublicKnowledgeSourceResult(url, true, statusCode, contentType, normalized.Length, note);
            return (result, new SourceBody(url, normalized));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Could not fetch public knowledge source {Url}.", url);
            return (new PublicKnowledgeSourceResult(url, false, 0, null, 0, ex.Message), null);
        }
    }

    private string BuildPrompt(
        PublicKnowledgeManifest manifest,
        string focus,
        IReadOnlyList<SourceBody> sources)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are the Notary Geek public knowledge research worker.");
        builder.AppendLine("Use only the public sources included below. Do not infer from private customer data.");
        builder.AppendLine("Apply the Notary Geek Routing Model as the decision layer: route first, platform last, source quality first.");
        builder.AppendLine("Marketing claims are evidence only of what a provider claims to sell, not evidence of lawful completion or acceptance.");
        builder.AppendLine("There is no private-source safe harbor. Return to controlling law, official sources, actual route evidence, and transaction date.");
        builder.AppendLine("For apostille work, distinguish Hague Apostille Convention finality from outside-Apostille-Convention authentication/legalization chains.");
        builder.AppendLine("For Hague destinations such as Spain, a proper apostille from the competent authority ends the legalization chain; do not add embassy or consular legalization after a valid apostille.");
        builder.AppendLine("Keep recipient document requirements separate from legalization. Translation, certified translation, filing, original/certified-copy, and wording requirements are not extra legalization steps.");
        builder.AppendLine("For a new U.S. private document that has not yet been notarized, the notary public's commissioning state/public-official signature controls the state apostille route; the document subject or named state does not automatically control.");
        builder.AppendLine("Citations must be exact fetched source URLs listed in this run. Do not cite URLs merely discovered inside an index or source document unless that URL was fetched in this run.");
        builder.AppendLine("Produce compact JSON with keys: summary, routeFindings, sourceQualityFindings, suggestedPublicReplies, websiteBriefs, lawRefreshCandidates, risks, citations.");
        builder.AppendLine("Keep every array to 4 or fewer items. Keep each item concise. Do not quote long passages.");
        builder.AppendLine("Every citation must use one of the source URLs below.");
        builder.AppendLine();
        builder.AppendLine($"Focus: {focus}");
        builder.AppendLine($"Manifest version: {manifest.Version}");
        builder.AppendLine($"Canonical routing model: {manifest.CanonicalRoutingModel}");
        builder.AppendLine("Strict exclusions:");
        foreach (var exclusion in manifest.StrictExclusions)
        {
            builder.AppendLine($"- {exclusion}");
        }

        var header = builder.ToString();
        var sourceBuilder = new StringBuilder();
        sourceBuilder.AppendLine();
        sourceBuilder.AppendLine("Sources:");
        var sourceBudget = Math.Max(1_000, _knowledgeOptions.MaxInputCharacters - header.Length - 1_000);
        var perSourceBudget = sources.Count == 0
            ? sourceBudget
            : Math.Max(1_000, sourceBudget / sources.Count);

        foreach (var source in sources)
        {
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine($"--- SOURCE: {source.Url}");
            if (source.Content.Length > perSourceBudget)
            {
                sourceBuilder.AppendLine($"[prompt-snippet-truncated:{source.Content.Length}->{perSourceBudget}]");
                sourceBuilder.AppendLine(source.Content[..perSourceBudget]);
            }
            else
            {
                sourceBuilder.AppendLine(source.Content);
            }
        }

        var prompt = header + sourceBuilder;
        if (prompt.Length <= _knowledgeOptions.MaxInputCharacters)
        {
            return prompt;
        }

        return prompt[.._knowledgeOptions.MaxInputCharacters];
    }

    private async Task<OpenAiProviderResult> CallOpenAiAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("OpenAI");
        client.Timeout = TimeSpan.FromSeconds(Math.Max(10, _openAiOptions.TimeoutSeconds));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAiOptions.ApiKey);

        var endpoint = BuildOpenAiEndpoint();
        var payload = new Dictionary<string, object?>
        {
            ["model"] = _openAiOptions.Model,
            ["input"] = new object[]
            {
                new
                {
                    role = "user",
                    content = prompt
                }
            },
            ["max_output_tokens"] = _knowledgeOptions.MaxOutputTokens
        };

        if (!string.IsNullOrWhiteSpace(_openAiOptions.ReasoningEffort))
        {
            payload["reasoning"] = new
            {
                effort = _openAiOptions.ReasoningEffort
            };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            var status = $"{(int)response.StatusCode} {response.StatusCode}";

            if (!response.IsSuccessStatusCode)
            {
                return new OpenAiProviderResult(false, null, status, null, responseText);
            }

            using var document = JsonDocument.Parse(responseText);
            var responseStatus = TryGetStringProperty(document.RootElement, "status");
            var statusWithResponse = string.IsNullOrWhiteSpace(responseStatus)
                ? status
                : $"{status}; response={responseStatus}";
            var outputText = ExtractOutputText(document.RootElement);
            var usageJson = TryGetRawProperty(document.RootElement, "usage");
            return new OpenAiProviderResult(true, outputText, statusWithResponse, usageJson, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "OpenAI public knowledge request failed.");
            return new OpenAiProviderResult(false, null, "exception", null, ex.Message);
        }
    }

    private Uri BuildOpenAiEndpoint()
    {
        var baseUrl = _openAiOptions.BaseUrl.TrimEnd('/');
        var path = string.IsNullOrWhiteSpace(_openAiOptions.EndpointPath) ? "/v1/responses" : _openAiOptions.EndpointPath;
        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            path = "/" + path;
        }

        return new Uri(baseUrl + path);
    }

    private string BuildAbsoluteUrl(string pathOrUrl)
    {
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out _))
        {
            return pathOrUrl;
        }

        return $"{_knowledgeOptions.PublicBaseUrl.TrimEnd('/')}/{pathOrUrl.TrimStart('/')}";
    }

    private bool IsAllowedPublicUrl(string url, out string reason)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            reason = "URL is not absolute.";
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            reason = "Only HTTPS URLs are allowed.";
            return false;
        }

        var allowedHosts = SplitList(_knowledgeOptions.AllowedSourceHosts);
        if (!allowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            reason = $"Host '{uri.Host}' is not allowlisted.";
            return false;
        }

        reason = "allowed";
        return true;
    }

    private static string ExtractOutputText(JsonElement root)
    {
        if (TryGetProperty(root, "output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString() ?? string.Empty;
        }

        if (!TryGetProperty(root, "output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return root.GetRawText();
        }

        var builder = new StringBuilder();
        foreach (var outputItem in output.EnumerateArray())
        {
            if (!TryGetProperty(outputItem, "content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (TryGetProperty(contentItem, "text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    builder.AppendLine(text.GetString());
                }
            }
        }

        return builder.ToString().Trim();
    }

    private static string? TryGetRawProperty(JsonElement root, string propertyName) =>
        TryGetProperty(root, propertyName, out var property) ? property.GetRawText() : null;

    private static string? TryGetStringProperty(JsonElement root, string propertyName) =>
        TryGetProperty(root, propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

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

    private static string NormalizeSourceText(string content, int maxCharacters, out bool truncated)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var safeMaxCharacters = Math.Max(1_000, maxCharacters);
        truncated = normalized.Length > safeMaxCharacters;
        return truncated ? normalized[..safeMaxCharacters] : normalized;
    }

    private static int EstimateTokens(int characterCount) =>
        Math.Max(1, (int)Math.Ceiling(characterCount / 4.0));

    private static IReadOnlyList<string> ValidateProviderResponse(
        string responseText,
        IReadOnlySet<string> fetchedSourceUrls)
    {
        if (fetchedSourceUrls.Count == 0)
        {
            return [];
        }

        return ExtractHttpsUrls(responseText)
            .Where(url => !fetchedSourceUrls.Contains(url))
            .Select(url => $"Provider cited URL not in fetched source list: {url}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> ExtractHttpsUrls(string text)
    {
        foreach (Match match in Regex.Matches(text, "https://[^\\s\"'<>\\\\]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var url = match.Value.TrimEnd('.', ',', ';', ':', ')', ']', '}');
            if (!string.IsNullOrWhiteSpace(url))
            {
                yield return url;
            }
        }
    }

    private static IReadOnlyList<string> SplitList(string value) =>
        value
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private sealed record SourceBody(string Url, string Content);

    private sealed record OpenAiProviderResult(
        bool Ok,
        string? ResponseText,
        string? Status,
        string? UsageJson,
        string? Error);
}

public sealed record PublicKnowledgeStatus(
    DateTime CheckedAtUtc,
    bool Enabled,
    bool TimerEnabled,
    string PublicBaseUrl,
    string ManifestSource,
    string OpenAiBaseUrl,
    string OpenAiEndpointPath,
    string Model,
    bool HasOpenAiApiKey,
    int MaxSourcesPerRun,
    int MaxEstimatedInputTokens,
    IReadOnlyList<string> AllowedSourceHosts);
