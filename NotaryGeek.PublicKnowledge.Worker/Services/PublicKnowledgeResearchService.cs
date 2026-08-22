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
    private static readonly HashSet<string> RuleScoringStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about",
        "actual",
        "after",
        "again",
        "against",
        "alone",
        "also",
        "another",
        "automatically",
        "because",
        "before",
        "being",
        "calls",
        "check",
        "does",
        "doing",
        "every",
        "fails",
        "first",
        "from",
        "give",
        "have",
        "into",
        "itself",
        "lists",
        "must",
        "only",
        "proof",
        "require",
        "requires",
        "says",
        "should",
        "shows",
        "signals",
        "simply",
        "starts",
        "still",
        "that",
        "their",
        "then",
        "there",
        "these",
        "this",
        "treat",
        "treats",
        "uses",
        "when",
        "where",
        "which",
        "while",
        "with",
        "without"
    };

    private const string FailureModalIndependentClauseBoundaryPattern =
        @"(?:\band\s+(?:[a-z0-9]+\s+){1,6}(?:that|which|who|whom|whose)\s+" +
        @"(?:(?!(?:requires|proves)\b)[a-z0-9]+\s+){0,5}?[a-z0-9]+(?:s|ed|ing)\s+" +
        @"(?:[a-z0-9]+\s+){0,3}(?:requires|proves)\b|" +
        @"(?:\band\s+(?:(?:a|an|the|this|that|these|those)\s+)?" +
        @"(?:(?!(?:that|which|who|whom|whose)\b)[a-z0-9]+\s+){1,3}|" +
        @"\b(?:but|yet|while|whereas|although|though|because)\s+" +
        @"(?:(?:a|an|the|this|that|these|those)\s+)?" +
        @"(?:(?!(?:but|yet|while|whereas|although|though|because)\b)[a-z0-9]+\s+)+?)" +
        @"(?:is|are|was|were|has|have|had|does|do|did|can|could|will|would|shall|should|must|may|might|" +
        @"requires?|proves?)\b)";

    private static readonly string[] FailureNegationMarkers =
    [
        " no ",
        " not ",
        " never ",
        " cannot ",
        " do not ",
        " does not ",
        " should not ",
        " must not ",
        " is not ",
        " are not ",
        " isn't ",
        " aren't ",
        " don't ",
        " doesn't ",
        " avoid ",
        " reject ",
        " rejects ",
        " rejected ",
        " not required ",
        " no additional ",
        " no further ",
        " do not add ",
        " do not call ",
        " do not treat ",
        " do not use ",
        " do not recommend "
    ];

    private static readonly string[] FailureCorrectiveContextMarkers =
    [
        " bad route ",
        " can cause ",
        " can create ",
        " causes delay ",
        " creates delay ",
        " create delay ",
        " creates confusion ",
        " create confusion ",
        " correction ",
        " corrective ",
        " delay and route confusion ",
        " do not describe ",
        " does not mean ",
        " is wrong ",
        " are wrong ",
        " incorrect ",
        " misstate ",
        " misstates ",
        " misstated ",
        " route confusion ",
        " route error ",
        " risk ",
        " risks ",
        " separate from ",
        " should be avoided ",
        " should be treated as ",
        " treat that as ",
        " wrong route ",
        " would cause ",
        " would create "
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PublicKnowledgeOptions _knowledgeOptions;
    private readonly OpenAiOptions _openAiOptions;
    private readonly StraicoOptions _straicoOptions;
    private readonly ILogger<PublicKnowledgeResearchService> _logger;

    public PublicKnowledgeResearchService(
        IHttpClientFactory httpClientFactory,
        IOptions<PublicKnowledgeOptions> knowledgeOptions,
        IOptions<OpenAiOptions> openAiOptions,
        IOptions<StraicoOptions> straicoOptions,
        ILogger<PublicKnowledgeResearchService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _knowledgeOptions = knowledgeOptions.Value;
        _openAiOptions = openAiOptions.Value;
        _straicoOptions = straicoOptions.Value;
        _logger = logger;
    }

    public PublicKnowledgeStatus GetStatus()
    {
        var hosts = SplitList(_knowledgeOptions.AllowedSourceHosts);
        return new PublicKnowledgeStatus(
            DateTime.UtcNow,
            _knowledgeOptions.Enabled,
            _knowledgeOptions.TimerEnabled,
            _knowledgeOptions.PumpTimerEnabled,
            _knowledgeOptions.TimerBatch,
            SplitList(_knowledgeOptions.TimerBatches).DefaultIfEmpty(_knowledgeOptions.TimerBatch).ToArray(),
            NormalizeStatusProvider(_knowledgeOptions.TimerProvider),
            SplitList(_knowledgeOptions.PumpTimerBatches),
            NormalizeStatusProvider(_knowledgeOptions.PumpTimerProvider),
            _knowledgeOptions.PublicBaseUrl,
            _knowledgeOptions.QueueName,
            string.IsNullOrWhiteSpace(_knowledgeOptions.PublicCorpusManifestUrl) ? "bundled-local" : _knowledgeOptions.PublicCorpusManifestUrl,
            _knowledgeOptions.LocalLawSourceIndexPath,
            GetConfiguredProviderName(),
            _openAiOptions.BaseUrl,
            _openAiOptions.EndpointPath,
            _openAiOptions.Model,
            !string.IsNullOrWhiteSpace(_openAiOptions.ApiKey),
            !string.IsNullOrWhiteSpace(_openAiOptions.PublicSourceApiKey),
            true,
            _straicoOptions.BaseUrl,
            _straicoOptions.DefaultChatModel,
            !string.IsNullOrWhiteSpace(_straicoOptions.ApiKey),
            _knowledgeOptions.MaxOutputTokens,
            IsHighCostModel(_openAiOptions.Model) && !_openAiOptions.AllowHighCostMode,
            _openAiOptions.HighCostMaxOutputTokens,
            _openAiOptions.HighCostReasoningEffort,
            _openAiOptions.AuthorityReasoningEffort,
            _openAiOptions.AuthorityMaxOutputTokens,
            _openAiOptions.RepairMaxOutputTokens,
            Math.Clamp(_openAiOptions.MaxProviderAttempts, 1, 2),
            _knowledgeOptions.MaxSourcesPerRun,
            _knowledgeOptions.SourceFetchConcurrency,
            _knowledgeOptions.MaxEstimatedInputTokens,
            hosts);
    }

    public PublicKnowledgeRegressionMatrix GetRegressionMatrix()
    {
        var localPath = Path.Combine(AppContext.BaseDirectory, _knowledgeOptions.LocalRegressionMatrixPath);
        if (!File.Exists(localPath))
        {
            return BuildMissingRegressionMatrix(localPath);
        }

        try
        {
            return ParseRegressionMatrix(File.ReadAllText(localPath));
        }
        catch (JsonException ex)
        {
            return new PublicKnowledgeRegressionMatrix(
                "notary-geek-public-knowledge-regression-matrix-error",
                "error",
                DateTime.UtcNow.ToString("O"),
                $"Regression matrix could not be parsed: {ex.Message}",
                "No case data loaded.",
                []);
        }
    }

    public bool TryGetRegressionCase(string caseId, out PublicKnowledgeRegressionCase? regressionCase)
    {
        regressionCase = GetRegressionMatrix()
            .Cases
            .FirstOrDefault(item => item.Id.Equals(caseId, StringComparison.OrdinalIgnoreCase));

        return regressionCase is not null;
    }

    public IReadOnlyList<PublicKnowledgeRegressionCase> GetRegressionCasesForBatch(string batch)
    {
        var cases = GetRegressionMatrix().Cases;
        if (batch.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return cases;
        }

        var ids = GetRegressionCaseIdsForBatch(batch);
        if (ids.Count == 0)
        {
            return [];
        }

        return ids
            .Select(id => cases.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .Where(item => item is not null)
            .Cast<PublicKnowledgeRegressionCase>()
            .ToArray();
    }

    public IReadOnlyList<string> GetRegressionBatchNames() =>
    [
        "All",
        "Core",
        "DailySourceIngestion",
        "TechnicalSourceIngestion",
        "Platform",
        "NNA",
        "Apostille",
        "Recipient"
    ];

    public async Task<PublicKnowledgeRunResult> RunAsync(
        PublicKnowledgeRunCommand command,
        CancellationToken cancellationToken)
    {
        var preparedSources = await PrepareSourcesAsync(command.RequestedUrls, cancellationToken);
        return await RunWithPreparedSourcesAsync(command, preparedSources, cancellationToken);
    }

    public async Task<IReadOnlyList<PublicKnowledgeRunResult>> RunBatchAsync(
        IReadOnlyList<PublicKnowledgeRunCommand> commands,
        CancellationToken cancellationToken)
    {
        if (commands.Count == 0)
        {
            return [];
        }

        if (!CommandsUseSameSourceSet(commands))
        {
            var sequentialResults = new List<PublicKnowledgeRunResult>();
            foreach (var command in commands)
            {
                sequentialResults.Add(await RunAsync(command, cancellationToken));
            }

            return sequentialResults;
        }

        var preparedSources = await PrepareSourcesAsync(commands[0].RequestedUrls, cancellationToken);
        var results = new List<PublicKnowledgeRunResult>();
        foreach (var command in commands)
        {
            results.Add(await RunWithPreparedSourcesAsync(command, preparedSources, cancellationToken));
        }

        return results;
    }

    private async Task<PreparedSources> PrepareSourcesAsync(
        IReadOnlyList<string> requestedUrls,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var sourceResults = new List<PublicKnowledgeSourceResult>();
        var sourceBodies = new List<SourceBody>();

        var manifest = await LoadManifestAsync(warnings, cancellationToken);
        var urls = SelectSourceUrls(manifest, requestedUrls, warnings);
        var client = CreateFetchClient();
        var selectedUrls = urls.Take(_knowledgeOptions.MaxSourcesPerRun).ToArray();
        var sourceFetchConcurrency = Math.Clamp(
            _knowledgeOptions.SourceFetchConcurrency,
            1,
            Math.Max(1, selectedUrls.Length));

        using var fetchLimiter = new SemaphoreSlim(sourceFetchConcurrency);
        var fetchTasks = selectedUrls.Select(async (url, index) =>
        {
            if (!IsAllowedPublicUrl(url, out var reason))
            {
                return new SourceFetchWorkItem(
                    index,
                    new PublicKnowledgeSourceResult(url, false, 0, null, 0, reason),
                    null,
                    $"Skipped {url}: {reason}");
            }

            await fetchLimiter.WaitAsync(cancellationToken);
            try
            {
                var fetched = await FetchSourceAsync(client, url, cancellationToken);
                return new SourceFetchWorkItem(index, fetched.Result, fetched.Body, null);
            }
            finally
            {
                fetchLimiter.Release();
            }
        }).ToArray();

        var fetchedSources = await Task.WhenAll(fetchTasks);
        foreach (var fetched in fetchedSources.OrderBy(item => item.Index))
        {
            sourceResults.Add(fetched.Result);
            if (fetched.Body is not null)
            {
                sourceBodies.Add(fetched.Body);
            }

            if (!string.IsNullOrWhiteSpace(fetched.Warning))
            {
                warnings.Add(fetched.Warning);
            }
        }

        return new PreparedSources(
            manifest,
            sourceResults.ToArray(),
            sourceBodies.ToArray(),
            warnings.ToArray());
    }

    private async Task<PublicKnowledgeRunResult> RunWithPreparedSourcesAsync(
        PublicKnowledgeRunCommand command,
        PreparedSources preparedSources,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>(preparedSources.Warnings);
        var errors = new List<string>();
        var manifest = preparedSources.Manifest;
        var sourceResults = preparedSources.SourceResults;
        var sourceBodies = preparedSources.SourceBodies;
        var providerName = GetConfiguredProviderName(command.ProviderOverride);

        var prompt = BuildPrompt(manifest, command, sourceBodies);
        var promptCharacters = prompt.Length;
        var estimatedInputTokens = EstimateTokens(promptCharacters);

        if (!IsSupportedProvider(providerName))
        {
            errors.Add($"Unsupported public knowledge provider '{providerName}'. Supported providers: OpenAI, Straico.");
        }

        if (command.RunKind.Equals("authority-generation", StringComparison.OrdinalIgnoreCase) &&
            !providerName.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Authority generation requires the dedicated public-source OpenAI lane.");
        }

        if (estimatedInputTokens > _knowledgeOptions.MaxEstimatedInputTokens)
        {
            errors.Add($"Estimated input tokens {estimatedInputTokens} exceed configured limit {_knowledgeOptions.MaxEstimatedInputTokens}.");
        }

        var shouldCallProvider = command.Execute && errors.Count == 0;
        if (shouldCallProvider && !_knowledgeOptions.Enabled)
        {
            errors.Add("PublicKnowledge__Enabled is false. Dry-run is allowed, but OpenAI calls are disabled.");
        }

        if (shouldCallProvider)
        {
            if (UseStraicoProvider(providerName))
            {
                if (string.IsNullOrWhiteSpace(_straicoOptions.ApiKey))
                {
                    errors.Add("Straico__ApiKey is not configured.");
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(GetOpenAiApiKey()))
                {
                    errors.Add("OpenAI__PublicSourceApiKey is not configured for the public-source OpenAI lane.");
                }
            }
        }

        if (!shouldCallProvider || errors.Count > 0)
        {
            var preflightScore = ScoreRegressionResponse(command.RegressionCase, null);
            return new PublicKnowledgeRunResult(
                Ok: errors.Count == 0,
                Execute: command.Execute,
                OpenAiCalled: false,
                Skipped: errors.Count > 0 || !command.Execute,
                Status: errors.Count > 0 ? "preflight-failed" : "dry-run",
                DateTime.UtcNow,
                command.Focus,
                command.RegressionCaseId,
                command.RegressionCase,
                sourceBodies.Count,
                promptCharacters,
                estimatedInputTokens,
                GetConfiguredModel(providerName),
                sourceResults,
                ResponseText: null,
                ProviderStatus: null,
                ProviderUsageJson: null,
                warnings,
                errors,
                preflightScore,
                Provider: providerName,
                RunKind: command.RunKind,
                AuthorityLane: command.AuthorityLane);
        }

        var fetchedSourceUrls = sourceBodies
            .Select(source => source.Url)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var provider = await CallConfiguredProviderAsync(
            prompt,
            providerName,
            command,
            fetchedSourceUrls,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(provider.ResponseText))
        {
            warnings.AddRange(ValidateProviderResponse(provider.ResponseText, fetchedSourceUrls));
        }

        if (!provider.Ok)
        {
            errors.Add(provider.Error ?? "OpenAI call failed.");
        }

        var regressionScore = ScoreRegressionResponse(command.RegressionCase, provider.ResponseText);
        return new PublicKnowledgeRunResult(
            Ok: provider.Ok && errors.Count == 0,
            Execute: command.Execute,
            OpenAiCalled: true,
            Skipped: false,
            Status: provider.Ok ? "completed" : "provider-failed",
            DateTime.UtcNow,
            command.Focus,
            command.RegressionCaseId,
            command.RegressionCase,
            sourceBodies.Count,
            promptCharacters,
            estimatedInputTokens,
            GetConfiguredModel(providerName),
            sourceResults,
            provider.ResponseText,
            provider.Status,
            provider.UsageJson,
            warnings,
            errors,
            regressionScore,
            Provider: providerName,
            ProviderEvidence: provider.Evidence,
            StructuredOutput: provider.StructuredOutput,
            RunKind: command.RunKind,
            AuthorityLane: command.AuthorityLane);
    }

    private static bool CommandsUseSameSourceSet(IReadOnlyList<PublicKnowledgeRunCommand> commands)
    {
        var first = commands[0].RequestedUrls;
        return commands.All(command =>
            command.RequestedUrls.Count == first.Count &&
            command.RequestedUrls.SequenceEqual(first, StringComparer.OrdinalIgnoreCase));
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

    private static PublicKnowledgeRegressionMatrix ParseRegressionMatrix(string json)
    {
        var dto = JsonSerializer.Deserialize<PublicKnowledgeRegressionMatrixDto>(json, JsonOptions)
            ?? throw new JsonException("Regression matrix JSON could not be parsed.");

        var cases = (dto.Cases ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Focus))
            .Select(item => new PublicKnowledgeRegressionCase(
                item.Id!,
                item.Focus!,
                item.Purpose ?? string.Empty,
                item.MustHold ?? [],
                item.FailureSignals ?? [],
                item.SourceUrls ?? []))
            .ToArray();

        return new PublicKnowledgeRegressionMatrix(
            dto.Schema ?? "notary-geek-public-knowledge-regression-matrix-v1",
            dto.Version ?? "0.1-public",
            dto.ReviewedUtc ?? string.Empty,
            dto.Purpose ?? "Public regression matrix.",
            dto.PublicOnlyPolicy ?? "Public source work only.",
            cases);
    }

    private static PublicKnowledgeRegressionMatrix BuildMissingRegressionMatrix(string localPath) =>
        new(
            "notary-geek-public-knowledge-regression-matrix-missing",
            "missing",
            DateTime.UtcNow.ToString("O"),
            $"Regression matrix was not found at {localPath}.",
            "No case data loaded.",
            []);

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
        PublicKnowledgeRunCommand command,
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
        builder.AppendLine("For document output, distinguish paper notarizations from remote/electronic notarizations. A physical paper notarization generally needs the physical original for apostille/authentication; a remote/electronic notarization may use the official printed/electronic output accepted by the competent authority. Do not collapse both into 'paper original' language.");
        builder.AppendLine("Do not use vague 'check with the recipient' language as an escape hatch. Recipient acceptance evidence means known written instructions, published recipient rules, official filing requirements, or documented rejection/acceptance evidence.");
        builder.AppendLine("For a new U.S. private document that has not yet been notarized, the notary public's commissioning state/public-official signature controls the state apostille route; the document subject or named state does not automatically control.");
        builder.AppendLine("Citations must be exact fetched source URLs listed in this run. Do not cite URLs merely discovered inside an index or source document unless that URL was fetched in this run.");
        builder.AppendLine("If an unfetched URL appears useful, put it in lawRefreshCandidates as a fetch candidate, not as a citation.");
        builder.AppendLine("Produce compact JSON that exactly matches the required structured-output schema.");
        builder.AppendLine("Candidate source URLs and citations must be exact fetched URLs. Each candidate must be public-safe, source-scoped, and independently reviewable.");
        builder.AppendLine("Set recheckBeforeUse=true because a destination workflow must re-check current official sources before acting.");
        builder.AppendLine(PublicKnowledgeProviderOutput.BuildReviewTimestampInstruction(DateTime.UtcNow));
        if (command.AuthorityLane.Equals("technical", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("Route candidates to reusable public-safe technical/API/cloud/platform source trails. Include no notary customer, private infrastructure, credential, endpoint, or operational context.");
        }
        else
        {
            builder.AppendLine("Route candidates to notary, apostille, public-law, and source-quality structured public knowledge.");
        }
        builder.AppendLine("Keep every array to 4 or fewer items. Keep each array item as a plain concise string, not a nested JSON object and not JSON serialized as text. Do not quote long passages.");
        builder.AppendLine("Every citation must use one of the exact allowed citation URLs listed below.");
        builder.AppendLine("Allowed citation URLs:");
        foreach (var source in sources)
        {
            builder.AppendLine($"- {source.Url}");
        }

        builder.AppendLine();
        builder.AppendLine($"Focus: {command.Focus}");
        builder.AppendLine($"Run kind: {command.RunKind}");
        builder.AppendLine($"Authority lane: {command.AuthorityLane}");
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
        PublicKnowledgeRunCommand command,
        IReadOnlySet<string> fetchedSourceUrls,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("OpenAI");
        client.Timeout = TimeSpan.FromSeconds(Math.Max(10, _openAiOptions.TimeoutSeconds));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GetOpenAiApiKey());

        var endpoint = BuildOpenAiEndpoint();
        var highCostGuardActive = IsHighCostModel(_openAiOptions.Model) && !_openAiOptions.AllowHighCostMode;
        var authorityRun = command.RunKind.Equals("authority-generation", StringComparison.OrdinalIgnoreCase);
        var configuredReasoningEffort = highCostGuardActive && !string.IsNullOrWhiteSpace(_openAiOptions.HighCostReasoningEffort)
            ? _openAiOptions.HighCostReasoningEffort
            : _openAiOptions.ReasoningEffort;
        var reasoningEffort = authorityRun && !string.IsNullOrWhiteSpace(_openAiOptions.AuthorityReasoningEffort)
            ? _openAiOptions.AuthorityReasoningEffort
            : configuredReasoningEffort;
        var maxAttempts = Math.Clamp(_openAiOptions.MaxProviderAttempts, 1, 2);
        var totalInputTokens = 0;
        var totalOutputTokens = 0;
        var totalReasoningTokens = 0;
        string? lastOutputText = null;
        string? lastUsageJson = null;
        string lastStatus = "not-called";
        string lastResponseStatus = "missing";
        string lastFailureReason = "provider_not_called";
        string lastResolvedModel = _openAiOptions.Model;
        string? lastServiceTier = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var attemptPrompt = attempt == 1
                ? prompt
                : $"{prompt}\n\nThe prior attempt was unusable ({lastFailureReason}). Return one complete schema-valid response; do not add commentary.";
            var maxOutputTokens = PublicKnowledgeExecutionPolicy.SelectOutputTokenBudget(
                _knowledgeOptions.MaxOutputTokens,
                _openAiOptions.AuthorityMaxOutputTokens,
                _openAiOptions.RepairMaxOutputTokens,
                _openAiOptions.HighCostMaxOutputTokens,
                authorityRun,
                repairAttempt: attempt > 1,
                highCostGuardActive);
            var payload = new Dictionary<string, object?>
            {
                ["model"] = _openAiOptions.Model,
                ["input"] = new object[] { new { role = "user", content = attemptPrompt } },
                ["max_output_tokens"] = maxOutputTokens,
                ["store"] = false,
                ["text"] = PublicKnowledgeProviderOutput.BuildOpenAiTextFormat()
            };
            if (!string.IsNullOrWhiteSpace(reasoningEffort))
            {
                payload["reasoning"] = new { effort = reasoningEffort };
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
            };

            try
            {
                using var response = await client.SendAsync(request, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                lastStatus = $"{(int)response.StatusCode} {response.StatusCode}";

                if (!response.IsSuccessStatusCode)
                {
                    lastFailureReason = $"openai_http_{(int)response.StatusCode}";
                    if (attempt < maxAttempts && (int)response.StatusCode >= 500)
                    {
                        continue;
                    }

                    break;
                }

                using var document = JsonDocument.Parse(responseBody);
                lastResponseStatus = TryGetStringProperty(document.RootElement, "status") ?? "missing";
                lastResolvedModel = TryGetStringProperty(document.RootElement, "model") ?? _openAiOptions.Model;
                lastServiceTier = TryGetStringProperty(document.RootElement, "service_tier");
                var incompleteReason = TryGetNestedStringProperty(document.RootElement, "incomplete_details", "reason");
                lastStatus = $"{lastStatus}; response={lastResponseStatus}" +
                             (string.IsNullOrWhiteSpace(incompleteReason) ? string.Empty : $"; reason={incompleteReason}");
                lastOutputText = ExtractOutputText(document.RootElement);
                lastUsageJson = TryGetRawProperty(document.RootElement, "usage");
                var attemptEvidence = PublicKnowledgeProviderOutput.ParseEvidence(
                    "openai",
                    "dedicated_public_source_key",
                    _openAiOptions.Model,
                    lastResponseStatus,
                    attempt,
                    lastUsageJson,
                    null,
                    lastResolvedModel,
                    lastServiceTier);
                totalInputTokens += attemptEvidence.InputTokens;
                totalOutputTokens += attemptEvidence.OutputTokens;
                totalReasoningTokens += attemptEvidence.ReasoningTokens;

                if (PublicKnowledgeProviderOutput.TryValidate(
                        lastResponseStatus,
                        lastOutputText,
                        fetchedSourceUrls,
                        DateTime.UtcNow,
                        _knowledgeOptions.SourceFreshnessDays,
                        out var structuredOutput,
                        out lastFailureReason))
                {
                    var evidence = new PublicKnowledgeProviderEvidence(
                        "openai",
                        "dedicated_public_source_key",
                        lastResolvedModel,
                        lastResponseStatus,
                        attempt,
                        totalInputTokens,
                        totalOutputTokens,
                        totalReasoningTokens,
                        null,
                        _openAiOptions.Model,
                        lastServiceTier);
                    var aggregateUsage = JsonSerializer.Serialize(new
                    {
                        input_tokens = totalInputTokens,
                        output_tokens = totalOutputTokens,
                        output_tokens_details = new { reasoning_tokens = totalReasoningTokens },
                        attempts = attempt
                    }, JsonOptions);
                    return new OpenAiProviderResult(true, lastOutputText, lastStatus, aggregateUsage, null, evidence, structuredOutput);
                }

                if (!string.IsNullOrWhiteSpace(incompleteReason))
                {
                    lastFailureReason = $"{lastFailureReason}:{incompleteReason}";
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogWarning(ex, "OpenAI public knowledge request attempt {Attempt} failed.", attempt);
                lastStatus = "exception";
                lastFailureReason = ex is JsonException ? "openai_response_invalid_json" : "openai_transport_failure";
            }
        }

        var failedEvidence = new PublicKnowledgeProviderEvidence(
            "openai",
            "dedicated_public_source_key",
            lastResolvedModel,
            lastResponseStatus,
            maxAttempts,
            totalInputTokens,
            totalOutputTokens,
            totalReasoningTokens,
            lastFailureReason,
            _openAiOptions.Model,
            lastServiceTier);
        return new OpenAiProviderResult(false, lastOutputText, lastStatus, lastUsageJson, lastFailureReason, failedEvidence, null);
    }

    private Task<OpenAiProviderResult> CallConfiguredProviderAsync(
        string prompt,
        string providerName,
        PublicKnowledgeRunCommand command,
        IReadOnlySet<string> fetchedSourceUrls,
        CancellationToken cancellationToken) =>
        UseStraicoProvider(providerName)
            ? CallStraicoAsync(prompt, cancellationToken)
            : CallOpenAiAsync(prompt, command, fetchedSourceUrls, cancellationToken);

    private async Task<OpenAiProviderResult> CallStraicoAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("Straico");
        client.Timeout = TimeSpan.FromSeconds(Math.Max(10, _straicoOptions.TimeoutSeconds));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _straicoOptions.ApiKey);

        var model = string.IsNullOrWhiteSpace(_straicoOptions.DefaultChatModel)
            ? "openai/gpt-5-mini"
            : _straicoOptions.DefaultChatModel.Trim();
        var endpoint = BuildStraicoEndpoint("/v1/prompt/completion");
        var payload = new
        {
            models = new[] { model },
            message = prompt
        };

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
            var outputText = ExtractStraicoOutputText(document.RootElement, model);
            var usageJson = ExtractStraicoUsageJson(document.RootElement);
            return new OpenAiProviderResult(true, outputText, $"{status}; provider=straico", usageJson, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Straico public knowledge request failed.");
            return new OpenAiProviderResult(false, null, "exception; provider=straico", null, ex.Message);
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

    private Uri BuildStraicoEndpoint(string path)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_straicoOptions.BaseUrl)
            ? "https://api.straico.com"
            : _straicoOptions.BaseUrl.TrimEnd('/');
        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            path = "/" + path;
        }

        return new Uri(baseUrl + path);
    }

    private static bool UseStraicoProvider(string providerName) =>
        providerName.Equals("Straico", StringComparison.OrdinalIgnoreCase);

    private string GetConfiguredProviderName(string? providerOverride = null)
    {
        var provider = string.IsNullOrWhiteSpace(providerOverride)
            ? _knowledgeOptions.Provider
            : providerOverride;

        if (provider.Equals("Straico", StringComparison.OrdinalIgnoreCase))
        {
            return "Straico";
        }

        if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            return "OpenAI";
        }

        return provider.Trim();
    }

    private string GetConfiguredModel(string providerName) =>
        UseStraicoProvider(providerName) ? _straicoOptions.DefaultChatModel : _openAiOptions.Model;

    private string GetOpenAiApiKey()
        => PublicKnowledgeProviderOutput.SelectPublicSourceApiKey(_openAiOptions.PublicSourceApiKey);

    private static bool IsSupportedProvider(string providerName) =>
        providerName.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) ||
        providerName.Equals("Straico", StringComparison.OrdinalIgnoreCase);

    private bool IsHighCostModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        return SplitList(_openAiOptions.HighCostModelMarkers)
            .Any(marker => model.Contains(marker, StringComparison.OrdinalIgnoreCase));
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

    private static string ExtractStraicoOutputText(JsonElement root, string model)
    {
        if (TryGetProperty(root, "data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            if (TryGetProperty(data, "completions", out var completions) && completions.ValueKind == JsonValueKind.Object)
            {
                if (TryGetProperty(completions, model, out var modelCompletion))
                {
                    var text = ExtractStraicoModelCompletionText(modelCompletion);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }

                foreach (var completion in completions.EnumerateObject())
                {
                    var text = ExtractStraicoModelCompletionText(completion.Value);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            if (TryGetProperty(data, "answer", out var answer) && answer.ValueKind == JsonValueKind.String)
            {
                return answer.GetString() ?? string.Empty;
            }
        }

        return root.GetRawText();
    }

    private static string ExtractStraicoModelCompletionText(JsonElement modelCompletion)
    {
        if (TryGetProperty(modelCompletion, "completion", out var completion))
        {
            var text = ExtractChatCompletionText(completion);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return ExtractChatCompletionText(modelCompletion);
    }

    private static string ExtractChatCompletionText(JsonElement completion)
    {
        if (TryGetProperty(completion, "choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (TryGetProperty(choice, "message", out var message) &&
                    TryGetProperty(message, "content", out var content) &&
                    content.ValueKind == JsonValueKind.String)
                {
                    return content.GetString() ?? string.Empty;
                }

                if (TryGetProperty(choice, "text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString() ?? string.Empty;
                }
            }
        }

        if (TryGetProperty(completion, "content", out var directContent) && directContent.ValueKind == JsonValueKind.String)
        {
            return directContent.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string? ExtractStraicoUsageJson(JsonElement root)
    {
        if (!TryGetProperty(root, "data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var usage = new Dictionary<string, object?>();
        if (TryGetProperty(data, "overall_price", out var overallPrice))
        {
            usage["overall_price"] = JsonSerializer.Deserialize<object>(overallPrice.GetRawText(), JsonOptions);
        }

        if (TryGetProperty(data, "overall_words", out var overallWords))
        {
            usage["overall_words"] = JsonSerializer.Deserialize<object>(overallWords.GetRawText(), JsonOptions);
        }

        if (TryGetProperty(data, "coins_used", out var coinsUsed))
        {
            usage["coins_used"] = JsonSerializer.Deserialize<object>(coinsUsed.GetRawText(), JsonOptions);
        }

        return usage.Count == 0 ? null : JsonSerializer.Serialize(usage, JsonOptions);
    }

    private static string? TryGetRawProperty(JsonElement root, string propertyName) =>
        TryGetProperty(root, propertyName, out var property) ? property.GetRawText() : null;

    private static string? TryGetStringProperty(JsonElement root, string propertyName) =>
        TryGetProperty(root, propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? TryGetNestedStringProperty(JsonElement root, string objectName, string propertyName) =>
        TryGetProperty(root, objectName, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? TryGetStringProperty(nested, propertyName)
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

        var normalizedFetchedUrls = fetchedSourceUrls
            .Select(NormalizeCitationUrl)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var citationUrls = ExtractProviderCitationUrls(responseText, out var parseWarning);
        var warnings = citationUrls
            .Where(url =>
            {
                var normalizedUrl = NormalizeCitationUrl(url);
                return string.IsNullOrWhiteSpace(normalizedUrl) || !normalizedFetchedUrls.Contains(normalizedUrl);
            })
            .Select(url => $"Provider cited URL not in fetched source list: {url}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return string.IsNullOrWhiteSpace(parseWarning)
            ? warnings
            : warnings.Prepend(parseWarning).ToArray();
    }

    private static IReadOnlyList<string> ExtractProviderCitationUrls(
        string responseText,
        out string? parseWarning)
    {
        parseWarning = null;
        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (!TryGetProperty(document.RootElement, "citations", out var citations))
            {
                parseWarning = "Provider response did not include a citations array.";
                return [];
            }

            if (citations.ValueKind != JsonValueKind.Array)
            {
                parseWarning = "Provider response citations field was not an array.";
                return [];
            }

            return citations
                .EnumerateArray()
                .SelectMany(item =>
                {
                    var value = item.ValueKind == JsonValueKind.String
                        ? item.GetString()
                        : item.GetRawText();

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        return [];
                    }

                    var urls = ExtractHttpsUrls(value).ToArray();
                    return urls.Length > 0 ? urls : [value];
                })
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (JsonException)
        {
            parseWarning = "Provider response was not valid JSON; URL validation scanned the full response text.";
            return ExtractHttpsUrls(responseText).ToArray();
        }
    }

    private static PublicKnowledgeRegressionScore? ScoreRegressionResponse(
        PublicKnowledgeRegressionCase? regressionCase,
        string? responseText)
    {
        if (regressionCase is null)
        {
            return null;
        }

        var hasResponse = !string.IsNullOrWhiteSpace(responseText);
        var mustHoldChecks = regressionCase.MustHold
            .Select(rule => ScoreMustHoldRule(rule, responseText))
            .ToArray();
        var failureSignalChecks = regressionCase.FailureSignals
            .Select(rule => ScoreFailureSignalRule(rule, responseText))
            .ToArray();
        var mustHoldTotal = mustHoldChecks.Length;
        var mustHoldPassed = mustHoldChecks.Count(item => item.Matched);
        var failureSignalTotal = failureSignalChecks.Length;
        var failureSignalsObserved = failureSignalChecks.Count(item => item.Matched);
        var failureSignalNeedsReview = failureSignalChecks.Any(item =>
            string.Equals(item.Status, "ambiguous-modal-scope", StringComparison.Ordinal));
        var verdict = GetRegressionVerdict(
            hasResponse,
            mustHoldTotal,
            mustHoldPassed,
            failureSignalsObserved,
            failureSignalNeedsReview);

        return new PublicKnowledgeRegressionScore(
            "notary-geek-public-knowledge-regression-score-v1",
            "0.1-public",
            verdict,
            "deterministic-surface-triage-v1",
            "Surface scoring is triage only. Human review still controls promotion, correction, and legal/source-quality approval.",
            mustHoldTotal,
            mustHoldPassed,
            Math.Max(0, mustHoldTotal - mustHoldPassed),
            failureSignalTotal,
            failureSignalsObserved,
            mustHoldChecks,
            failureSignalChecks);
    }

    private static PublicKnowledgeRegressionRuleCheck ScoreMustHoldRule(
        string rule,
        string? responseText)
    {
        var ruleTokens = GetRuleScoringTokens(rule);
        if (string.IsNullOrWhiteSpace(responseText) || ruleTokens.Count == 0)
        {
            return BuildRuleCheck(rule, "not-evaluated", false, [], ruleTokens, 0);
        }

        var normalizedResponse = NormalizeRuleScoringText(responseText);
        var matchedTokens = ruleTokens
            .Where(token => ContainsScoringToken(normalizedResponse, token))
            .ToArray();
        var requiredTokenCount = GetRequiredMustHoldTokenCount(ruleTokens.Count);
        var matched = matchedTokens.Length >= requiredTokenCount;

        return BuildRuleCheck(
            rule,
            matched ? "passed" : "missing",
            matched,
            matchedTokens,
            ruleTokens,
            requiredTokenCount);
    }

    private static PublicKnowledgeRegressionRuleCheck ScoreFailureSignalRule(
        string rule,
        string? responseText)
    {
        var ruleTokens = GetRuleScoringTokens(rule);
        if (string.IsNullOrWhiteSpace(responseText) || ruleTokens.Count == 0)
        {
            return BuildRuleCheck(rule, "not-evaluated", false, [], ruleTokens, 0);
        }

        var requiredTokenCount = GetRequiredFailureSignalTokenCount(ruleTokens.Count);
        IReadOnlyList<string> correctiveMatchedTokens = [];
        IReadOnlyList<string> ambiguousMatchedTokens = [];
        foreach (var segment in SplitScoringSegments(responseText))
        {
            var normalizedSegment = NormalizeRuleScoringText(segment);
            var matchedTokens = ruleTokens
                .Where(token => ContainsScoringToken(normalizedSegment, token))
                .ToArray();

            if (matchedTokens.Length < requiredTokenCount)
            {
                continue;
            }

            if (HasFailureNegationMarker(normalizedSegment))
            {
                correctiveMatchedTokens = matchedTokens;
                continue;
            }

            if (FailureCorrectiveContextMarkers.Any(marker =>
                    normalizedSegment.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                correctiveMatchedTokens = matchedTokens;
                continue;
            }

            if (HasAmbiguousFailureModalScope(
                    segment,
                    matchedTokens,
                    requiredTokenCount))
            {
                ambiguousMatchedTokens = matchedTokens;
                continue;
            }

            var normalizedModalSegment = NormalizeFailureModalScoringText(
                segment,
                matchedTokens,
                requiredTokenCount);
            if (HasFailureCorrectiveContextMarker(
                    normalizedSegment,
                    normalizedModalSegment,
                    matchedTokens,
                    requiredTokenCount))
            {
                correctiveMatchedTokens = matchedTokens;
                continue;
            }

            return BuildRuleCheck(
                rule,
                "observed",
                true,
                matchedTokens,
                ruleTokens,
                requiredTokenCount);
        }

        if (ambiguousMatchedTokens.Count > 0)
        {
            return BuildRuleCheck(
                rule,
                "ambiguous-modal-scope",
                false,
                ambiguousMatchedTokens,
                ruleTokens,
                requiredTokenCount);
        }

        if (correctiveMatchedTokens.Count > 0)
        {
            return BuildRuleCheck(rule, "clear-corrective-mention", false, correctiveMatchedTokens, ruleTokens, requiredTokenCount);
        }

        return BuildRuleCheck(rule, "clear", false, [], ruleTokens, requiredTokenCount);
    }

    private static PublicKnowledgeRegressionRuleCheck BuildRuleCheck(
        string rule,
        string status,
        bool matched,
        IReadOnlyList<string> matchedTokens,
        IReadOnlyList<string> ruleTokens,
        int requiredTokenCount)
    {
        var matchedSet = matchedTokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingTokens = ruleTokens
            .Where(token => !matchedSet.Contains(token))
            .ToArray();

        return new PublicKnowledgeRegressionRuleCheck(
            rule,
            status,
            matched,
            matchedTokens.Count,
            requiredTokenCount,
            matchedTokens,
            missingTokens);
    }

    private static string GetRegressionVerdict(
        bool hasResponse,
        int mustHoldTotal,
        int mustHoldPassed,
        int failureSignalsObserved,
        bool failureSignalNeedsReview)
    {
        if (!hasResponse)
        {
            return "not-scored";
        }

        if (failureSignalsObserved > 0)
        {
            return "fail";
        }

        if (failureSignalNeedsReview)
        {
            return "needs-review";
        }

        if (mustHoldTotal == 0)
        {
            return "not-scored";
        }

        if (mustHoldPassed == mustHoldTotal)
        {
            return "pass";
        }

        return mustHoldPassed == 0 ? "fail" : "needs-review";
    }

    private static IReadOnlyList<string> GetRuleScoringTokens(string rule) =>
        Regex.Matches(rule.ToLowerInvariant(), "[a-z0-9]+", RegexOptions.CultureInvariant)
            .Select(match => match.Value)
            .Where(token => token.Length >= 4)
            .Where(token => !RuleScoringStopWords.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToArray();

    private static int GetRequiredMustHoldTokenCount(int tokenCount) =>
        tokenCount switch
        {
            <= 0 => 0,
            <= 2 => tokenCount,
            <= 5 => Math.Max(2, tokenCount - 1),
            _ => Math.Max(3, (int)Math.Ceiling(tokenCount * 0.45))
        };

    private static int GetRequiredFailureSignalTokenCount(int tokenCount) =>
        tokenCount switch
        {
            <= 0 => 0,
            <= 3 => tokenCount,
            _ => Math.Max(4, (int)Math.Ceiling(tokenCount * 0.65))
        };

    private static bool ContainsScoringToken(string normalizedText, string token) =>
        normalizedText.Contains($" {token} ", StringComparison.OrdinalIgnoreCase);

    private static bool HasAmbiguousFailureModalScope(
        string segment,
        IReadOnlyList<string> matchedTokens,
        int requiredTokenCount)
    {
        var match = Regex.Match(
            segment,
            @"\bmight\b(?:(?!\b(?:but|yet|while|whereas|although|though|because)\b)[^,])*" +
            @"\band\b(?:(?!\b(?:but|yet|while|whereas|although|though|because)\b)[^,])*" +
            @"\b(?:that|which|who|whom|whose)\b" +
            @"(?:(?!\b(?:but|yet|while|whereas|although|though|because)\b)[^,])*" +
            @"\b(?:requires?|proves?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success &&
               !HasUnhedgedFailureThresholdOutsideAmbiguousMatch(
                   segment,
                   match,
                   matchedTokens,
                   requiredTokenCount);
    }

    private static bool HasUnhedgedFailureThresholdOutsideAmbiguousMatch(
        string segment,
        Match match,
        IReadOnlyList<string> matchedTokens,
        int requiredTokenCount)
    {
        var outsideMatch = segment.ToCharArray();
        Array.Fill(outsideMatch, ' ', match.Index, match.Length);
        return Regex.Split(
                new string(outsideMatch),
                @",|\b(?:but|yet|while|whereas|although|though|because)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(NormalizeRuleScoringText)
            .Any(clause =>
                (!clause.Contains(" might ", StringComparison.OrdinalIgnoreCase) &&
                 matchedTokens.Count(token => ContainsScoringToken(clause, token)) >= requiredTokenCount) ||
                HasUnhedgedFailureThresholdBeforeModal(
                    clause,
                    clause,
                    matchedTokens,
                    requiredTokenCount));
    }

    private static string NormalizeRuleScoringText(string text)
    {
        var lowered = text.ToLowerInvariant();
        var normalized = Regex.Replace(lowered, "[^a-z0-9]+", " ", RegexOptions.CultureInvariant);
        return $" {Regex.Replace(normalized, "\\s+", " ", RegexOptions.CultureInvariant).Trim()} ";
    }

    private static string NormalizeFailureModalScoringText(
        string segment,
        IReadOnlyList<string> matchedTokens,
        int requiredTokenCount)
    {
        string[] candidatePatterns =
        [
            @",\s*(?:that|which|who|whom|whose)\b[^,]*\bmight\b[^,]*,",
            @"^\s*(?:although|though|while|whereas|because)\b[^,]*\bmight\b[^,]*,",
            @"^[^,]*\bmight\b[^,]*,\s*(?=(?:and|but|or|yet|while|whereas|although|though|because)\b)",
            @",\s*(?:and|but|or|yet|while|whereas|although|though|because)\b[^,]*\bmight\b[^,]*$",
            @"\b(?:that|which|who|whom|whose)\s+(?:[a-z0-9]+\s+)+?might\s+(?:[a-z0-9]+\s+)+?" +
            @"(?=requires?|proves?\b)"
        ];
        var candidateMatches = candidatePatterns
            .SelectMany(pattern => Regex.Matches(
                segment,
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Cast<Match>())
            .ToArray();
        if (!HasUnhedgedFailureThresholdOutsideMatches(
                segment,
                candidateMatches,
                matchedTokens,
                requiredTokenCount))
        {
            return NormalizeRuleScoringText(segment);
        }

        var maskedModal = segment;
        foreach (var pattern in candidatePatterns)
        {
            maskedModal = Regex.Replace(
                maskedModal,
                pattern,
                MaskFailureModal,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return NormalizeRuleScoringText(maskedModal);
    }

    private static bool HasUnhedgedFailureThresholdOutsideMatches(
        string segment,
        IReadOnlyList<Match> matches,
        IReadOnlyList<string> matchedTokens,
        int requiredTokenCount)
    {
        if (matches.Count == 0)
        {
            return false;
        }

        var outsideMatches = segment.ToCharArray();
        foreach (var match in matches)
        {
            Array.Fill(outsideMatches, ' ', match.Index, match.Length);
        }

        var normalizedOutsideMatch = NormalizeRuleScoringText(new string(outsideMatches));
        return !normalizedOutsideMatch.Contains(" might ", StringComparison.OrdinalIgnoreCase) &&
               matchedTokens.Count(token => ContainsScoringToken(normalizedOutsideMatch, token)) >=
               requiredTokenCount;
    }

    private static string MaskFailureModal(Match match) =>
        Regex.Replace(
            match.Value,
            @"\bmight\b",
            "maybe",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IReadOnlyList<string> SplitScoringSegments(string responseText) =>
        Regex.Split(responseText, @"[\r\n.!?;]+", RegexOptions.CultureInvariant)
            .Select(segment => segment.Trim())
            .Where(segment => segment.Length > 0)
            .ToArray();

    private static bool HasFailureNegationMarker(string normalizedSegment) =>
        FailureNegationMarkers.Any(marker =>
            normalizedSegment.Contains(NormalizeRuleScoringText(marker), StringComparison.OrdinalIgnoreCase));

    private static bool HasFailureCorrectiveContextMarker(
        string normalizedSegment,
        string normalizedModalSegment,
        IReadOnlyList<string> matchedTokens,
        int requiredTokenCount)
    {
        if (HasUnhedgedFailureThresholdBeforeModal(
                normalizedSegment,
                normalizedModalSegment,
                matchedTokens,
                requiredTokenCount))
        {
            return false;
        }

        var whetherModalScope = GetWhetherModalScope(
            normalizedSegment,
            matchedTokens,
            requiredTokenCount);
        if (whetherModalScope.HasValue)
        {
            return whetherModalScope.Value;
        }

        if (FailureCorrectiveContextMarkers.Any(marker =>
            normalizedSegment.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var clauseStarts = Regex.Matches(
                normalizedSegment,
                FailureModalIndependentClauseBoundaryPattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => match.Index)
            .Prepend(0)
            .Distinct()
            .Order()
            .ToArray();
        var hasCorrectiveClause = false;

        for (var index = 0; index < clauseStarts.Length; index++)
        {
            var clauseStart = clauseStarts[index];
            var clauseEnd = index + 1 < clauseStarts.Length
                ? clauseStarts[index + 1]
                : normalizedSegment.Length;
            var clause = normalizedSegment[clauseStart..clauseEnd];
            var modalClause = normalizedModalSegment[clauseStart..clauseEnd];
            var clauseMatchedTokens = matchedTokens
                .Where(token => ContainsScoringToken(clause, token))
                .ToArray();
            if (clauseMatchedTokens.Length == 0)
            {
                continue;
            }

            if (modalClause.Contains(" might ", StringComparison.OrdinalIgnoreCase))
            {
                hasCorrectiveClause = true;
                continue;
            }

            if (clauseMatchedTokens.Length >= requiredTokenCount)
            {
                return false;
            }
        }

        return hasCorrectiveClause;
    }

    private static bool? GetWhetherModalScope(
        string normalizedSegment,
        IReadOnlyList<string> matchedTokens,
        int requiredTokenCount)
    {
        var whetherIndex = normalizedSegment.IndexOf(" whether ", StringComparison.OrdinalIgnoreCase);
        var mightIndex = whetherIndex >= 0
            ? normalizedSegment.IndexOf(
                " might ",
                whetherIndex + " whether ".Length,
                StringComparison.OrdinalIgnoreCase)
            : -1;
        if (mightIndex < 0)
        {
            return null;
        }

        var beforeWhether = $"{normalizedSegment[..whetherIndex]} ";
        var afterMightStart = mightIndex + " might ".Length;
        var afterMight = normalizedSegment[afterMightStart..];
        var outsideBoundary = Regex.Match(
            afterMight,
            @"\band\s+(?:requires?|proves?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var afterWhetherProposition = outsideBoundary.Success
            ? $" {afterMight[outsideBoundary.Index..]}"
            : string.Empty;
        var outsideWhetherProposition = $"{beforeWhether}{afterWhetherProposition}";
        return matchedTokens.Count(token =>
            ContainsScoringToken(outsideWhetherProposition, token)) < requiredTokenCount;
    }

    private static bool HasUnhedgedFailureThresholdBeforeModal(
        string normalizedSegment,
        string normalizedModalSegment,
        IReadOnlyList<string> matchedTokens,
        int requiredTokenCount)
    {
        var mightIndex = normalizedModalSegment.IndexOf(" might ", StringComparison.OrdinalIgnoreCase);
        var precedingAndIndex = mightIndex > 0
            ? normalizedSegment.LastIndexOf(
                " and ",
                mightIndex,
                StringComparison.OrdinalIgnoreCase)
            : -1;
        if (precedingAndIndex < 0)
        {
            return false;
        }

        var whetherIndex = normalizedSegment.IndexOf(" whether ", StringComparison.OrdinalIgnoreCase);
        if (whetherIndex >= 0 && whetherIndex < precedingAndIndex)
        {
            return false;
        }

        var precedingClause = $"{normalizedSegment[..precedingAndIndex]} ";
        return matchedTokens.Count(token => ContainsScoringToken(precedingClause, token)) >= requiredTokenCount;
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

    private static string? NormalizeCitationUrl(string url)
    {
        var trimmed = url.Trim().TrimEnd('.', ',', ';', ':', ')', ']', '}');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.IdnHost.ToLowerInvariant(),
            Fragment = string.Empty
        };

        if ((builder.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) && builder.Port == 443) ||
            (builder.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && builder.Port == 80))
        {
            builder.Port = -1;
        }

        if (builder.Path.Length > 1)
        {
            builder.Path = builder.Path.TrimEnd('/');
        }

        return builder.Uri.AbsoluteUri;
    }

    private static IReadOnlyList<string> SplitList(string value) =>
        value
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string NormalizeStatusProvider(string provider) =>
        string.IsNullOrWhiteSpace(provider) || provider.Equals("default", StringComparison.OrdinalIgnoreCase)
            ? "Default"
            : provider.Trim();

    private static IReadOnlyList<string> GetRegressionCaseIdsForBatch(string batch) =>
        batch.Trim().ToLowerInvariant() switch
        {
            "core" =>
            [
                "spain-hague-finality",
                "georgia-affidavit-florida-notary-spain",
                "platform-hype-foreign-signer-no-ssn-spain",
                "outside-apostille-path-not-apostille-plus"
            ],
            "dailysourceingestion" =>
            [
                "daily-public-source-ingestion-safety-gates"
            ],
            "technicalsourceingestion" =>
            [
                "daily-technical-source-ingestion"
            ],
            "platform" =>
            [
                "foreign-signer-no-ssn-platform-route-first",
                "virginia-foreign-signer-network-myth",
                "commercial-incentive-routing-not-route-authority",
                "platform-hype-foreign-signer-no-ssn-spain",
                "notarycam-proof-history-scrutiny",
                "real-estate-court-defensible-platform-trap",
                "nna-data-exchange-api-private-credential-rail",
                "nna-legitimacy-not-legal-authority",
                "ethical-acceptance-diploma-mill-boundary",
                "coaching-scam-no-criminal-intent-boundary"
            ],
            "apostille" =>
            [
                "spain-hague-finality",
                "georgia-affidavit-florida-notary-spain",
                "saudi-arabia-hague-not-non-hague",
                "outside-apostille-path-not-apostille-plus"
            ],
            "recipient" =>
            [
                "recipient-phone-comment-not-rejection",
                "real-estate-court-defensible-platform-trap"
            ],
            "nna" =>
            [
                "nna-data-exchange-api-private-credential-rail",
                "nna-legitimacy-not-legal-authority",
                "coaching-scam-no-criminal-intent-boundary",
                "commercial-incentive-routing-not-route-authority",
                "ethical-acceptance-diploma-mill-boundary"
            ],
            _ => []
        };

    private sealed record SourceBody(string Url, string Content);

    private sealed record PreparedSources(
        PublicKnowledgeManifest Manifest,
        IReadOnlyList<PublicKnowledgeSourceResult> SourceResults,
        IReadOnlyList<SourceBody> SourceBodies,
        IReadOnlyList<string> Warnings);

    private sealed record SourceFetchWorkItem(
        int Index,
        PublicKnowledgeSourceResult Result,
        SourceBody? Body,
        string? Warning);

    private sealed record OpenAiProviderResult(
        bool Ok,
        string? ResponseText,
        string? Status,
        string? UsageJson,
        string? Error,
        PublicKnowledgeProviderEvidence? Evidence = null,
        PublicKnowledgeStructuredOutput? StructuredOutput = null);
}

public sealed record PublicKnowledgeStatus(
    DateTime CheckedAtUtc,
    bool Enabled,
    bool TimerEnabled,
    bool PumpTimerEnabled,
    string TimerBatch,
    IReadOnlyList<string> TimerBatches,
    string TimerProvider,
    IReadOnlyList<string> PumpTimerBatches,
    string PumpTimerProvider,
    string PublicBaseUrl,
    string QueueName,
    string ManifestSource,
    string LawSourceIndexPath,
    string Provider,
    string OpenAiBaseUrl,
    string OpenAiEndpointPath,
    string Model,
    bool HasOpenAiApiKey,
    bool HasPublicSourceOpenAiApiKey,
    bool PublicSourceOpenAiKeyRequired,
    string StraicoBaseUrl,
    string StraicoModel,
    bool HasStraicoApiKey,
    int MaxOutputTokens,
    bool HighCostGuardActive,
    int HighCostMaxOutputTokens,
    string HighCostReasoningEffort,
    string AuthorityReasoningEffort,
    int AuthorityMaxOutputTokens,
    int RepairMaxOutputTokens,
    int MaxProviderAttempts,
    int MaxSourcesPerRun,
    int SourceFetchConcurrency,
    int MaxEstimatedInputTokens,
    IReadOnlyList<string> AllowedSourceHosts);
