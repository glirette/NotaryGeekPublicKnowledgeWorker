using System.Text.Json;
using NotaryGeek.PublicKnowledge.Worker.Models;

namespace NotaryGeek.PublicKnowledge.Worker.Services;

public static class PublicKnowledgeProviderOutput
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static object BuildOpenAiTextFormat() => new
    {
        format = new
        {
            type = "json_schema",
            name = "public_authority_candidates",
            strict = true,
            schema = BuildSchema()
        }
    };

    public static string SelectPublicSourceApiKey(string? publicSourceApiKey, string? genericApiKey, bool requirePublicSourceKey)
    {
        if (!string.IsNullOrWhiteSpace(publicSourceApiKey))
        {
            return publicSourceApiKey;
        }

        return requirePublicSourceKey ? string.Empty : genericApiKey ?? string.Empty;
    }

    public static bool TryValidate(
        string? responseStatus,
        string? responseText,
        IReadOnlySet<string> fetchedSourceUrls,
        DateTime nowUtc,
        int sourceFreshnessDays,
        out PublicKnowledgeStructuredOutput? output,
        out string failureReason)
    {
        output = null;
        if (!string.Equals(responseStatus, "completed", StringComparison.OrdinalIgnoreCase))
        {
            failureReason = string.IsNullOrWhiteSpace(responseStatus)
                ? "provider_response_status_missing"
                : $"provider_response_{responseStatus.Trim().ToLowerInvariant()}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(responseText))
        {
            failureReason = "provider_output_empty";
            return false;
        }

        try
        {
            output = JsonSerializer.Deserialize<PublicKnowledgeStructuredOutput>(responseText, JsonOptions);
        }
        catch (JsonException)
        {
            failureReason = "provider_output_invalid_json";
            return false;
        }

        if (output is null || string.IsNullOrWhiteSpace(output.Summary))
        {
            failureReason = "provider_output_schema_invalid";
            return false;
        }

        var normalizedFetchedUrls = fetchedSourceUrls
            .Select(NormalizeUrl)
            .Where(item => item is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (output.Citations is null || output.Citations.Count == 0 ||
            output.Citations.Any(url => !IsAllowedCitation(url, normalizedFetchedUrls)))
        {
            failureReason = "provider_output_citations_invalid";
            output = null;
            return false;
        }

        if (output.Candidates is null || output.Candidates.Count == 0 || output.Candidates.Count > 4)
        {
            failureReason = "provider_output_candidates_invalid";
            output = null;
            return false;
        }

        foreach (var candidate in output.Candidates)
        {
            if (!TryValidateCandidate(candidate, normalizedFetchedUrls, nowUtc, sourceFreshnessDays, out failureReason))
            {
                output = null;
                return false;
            }
        }

        failureReason = string.Empty;
        return true;
    }

    public static PublicKnowledgeProviderEvidence ParseEvidence(
        string provider,
        string authMode,
        string model,
        string responseStatus,
        int attempts,
        string? usageJson,
        string? failureReason)
    {
        var inputTokens = 0;
        var outputTokens = 0;
        var reasoningTokens = 0;
        if (!string.IsNullOrWhiteSpace(usageJson))
        {
            try
            {
                using var usage = JsonDocument.Parse(usageJson);
                inputTokens = ReadInt(usage.RootElement, "input_tokens");
                outputTokens = ReadInt(usage.RootElement, "output_tokens");
                if (TryGetProperty(usage.RootElement, "output_tokens_details", out var details))
                {
                    reasoningTokens = ReadInt(details, "reasoning_tokens");
                }
            }
            catch (JsonException)
            {
                failureReason ??= "provider_usage_invalid_json";
            }
        }

        return new PublicKnowledgeProviderEvidence(
            provider,
            authMode,
            model,
            responseStatus,
            attempts,
            inputTokens,
            outputTokens,
            reasoningTokens,
            failureReason);
    }

    private static object BuildSchema()
    {
        var stringArray = new Dictionary<string, object?>
        {
            ["type"] = "array",
            ["items"] = new { type = "string" },
            ["maxItems"] = 4
        };
        var sourceSchema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new[] { "url", "title", "publisher", "kind", "reviewedAtUtc", "supports" },
            ["properties"] = new Dictionary<string, object?>
            {
                ["url"] = new { type = "string" },
                ["title"] = new { type = "string" },
                ["publisher"] = new { type = "string" },
                ["kind"] = new { type = "string" },
                ["reviewedAtUtc"] = new { type = "string" },
                ["supports"] = new { type = "string" }
            }
        };
        var candidateSchema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new[] { "topicId", "title", "summary", "reviewedAtUtc", "recheckBeforeUse", "sources", "supports", "doesNotProve" },
            ["properties"] = new Dictionary<string, object?>
            {
                ["topicId"] = new { type = "string" },
                ["title"] = new { type = "string" },
                ["summary"] = new { type = "string" },
                ["reviewedAtUtc"] = new { type = "string" },
                ["recheckBeforeUse"] = new { type = "boolean" },
                ["sources"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = sourceSchema, ["minItems"] = 1, ["maxItems"] = 12 },
                ["supports"] = stringArray,
                ["doesNotProve"] = stringArray
            }
        };

        return new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new[] { "summary", "routeFindings", "sourceQualityFindings", "suggestedPublicReplies", "websiteBriefs", "lawRefreshCandidates", "risks", "citations", "candidates" },
            ["properties"] = new Dictionary<string, object?>
            {
                ["summary"] = new { type = "string" },
                ["routeFindings"] = stringArray,
                ["sourceQualityFindings"] = stringArray,
                ["suggestedPublicReplies"] = stringArray,
                ["websiteBriefs"] = stringArray,
                ["lawRefreshCandidates"] = stringArray,
                ["risks"] = stringArray,
                ["citations"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = new { type = "string" }, ["minItems"] = 1, ["maxItems"] = 12 },
                ["candidates"] = new Dictionary<string, object?> { ["type"] = "array", ["items"] = candidateSchema, ["minItems"] = 1, ["maxItems"] = 4 }
            }
        };
    }

    private static bool TryValidateCandidate(
        PublicAuthorityCandidateDraft candidate,
        IReadOnlySet<string> fetchedSourceUrls,
        DateTime nowUtc,
        int sourceFreshnessDays,
        out string failureReason)
    {
        if (string.IsNullOrWhiteSpace(candidate.TopicId) ||
            string.IsNullOrWhiteSpace(candidate.Title) ||
            string.IsNullOrWhiteSpace(candidate.Summary) ||
            candidate.Supports is null || candidate.Supports.Count == 0 ||
            candidate.DoesNotProve is null ||
            candidate.Sources is null || candidate.Sources.Count is < 1 or > 12)
        {
            failureReason = "provider_candidate_schema_invalid";
            return false;
        }

        var freshnessCutoff = nowUtc.AddDays(-Math.Max(1, sourceFreshnessDays));
        if (candidate.ReviewedAtUtc == default ||
            candidate.ReviewedAtUtc < freshnessCutoff ||
            candidate.ReviewedAtUtc > nowUtc.AddMinutes(5) ||
            !candidate.RecheckBeforeUse)
        {
            failureReason = "provider_candidate_freshness_invalid";
            return false;
        }

        foreach (var source in candidate.Sources)
        {
            if (string.IsNullOrWhiteSpace(source.Title) ||
                string.IsNullOrWhiteSpace(source.Publisher) ||
                string.IsNullOrWhiteSpace(source.Kind) ||
                string.IsNullOrWhiteSpace(source.Supports) ||
                source.ReviewedAtUtc == default ||
                source.ReviewedAtUtc < freshnessCutoff ||
                source.ReviewedAtUtc > nowUtc.AddMinutes(5) ||
                !IsAllowedCitation(source.Url, fetchedSourceUrls))
            {
                failureReason = "provider_candidate_source_invalid";
                return false;
            }
        }

        failureReason = string.Empty;
        return true;
    }

    private static bool IsAllowedCitation(string url, IReadOnlySet<string> fetchedSourceUrls)
    {
        var normalized = NormalizeUrl(url);
        return normalized is not null && fetchedSourceUrls.Contains(normalized);
    }

    private static string? NormalizeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var builder = new UriBuilder(uri) { Fragment = string.Empty, Host = uri.IdnHost.ToLowerInvariant() };
        if (builder.Port == 443)
        {
            builder.Port = -1;
        }

        if (builder.Path.Length > 1)
        {
            builder.Path = builder.Path.TrimEnd('/');
        }

        return builder.Uri.AbsoluteUri;
    }

    private static int ReadInt(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
