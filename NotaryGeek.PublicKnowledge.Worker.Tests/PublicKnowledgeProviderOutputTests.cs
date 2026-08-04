using System.Text.Json;
using NotaryGeek.PublicKnowledge.Worker.Models;
using NotaryGeek.PublicKnowledge.Worker.Services;

namespace NotaryGeek.PublicKnowledge.Worker.Tests;

public sealed class PublicKnowledgeProviderOutputTests
{
    private const string SourceUrl = "https://developers.openai.com/api/docs/guides/structured-outputs";

    [Fact]
    public void IncompleteResponseIsNotUsableEvenWhenHttpSucceeded()
    {
        var ok = PublicKnowledgeProviderOutput.TryValidate(
            "incomplete",
            string.Empty,
            FetchedUrls(),
            DateTime.UtcNow,
            14,
            out var output,
            out var reason);

        Assert.False(ok);
        Assert.Null(output);
        Assert.Equal("provider_response_incomplete", reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    public void EmptyOrInvalidJsonIsNotUsable(string responseText)
    {
        var ok = PublicKnowledgeProviderOutput.TryValidate(
            "completed",
            responseText,
            FetchedUrls(),
            DateTime.UtcNow,
            14,
            out _,
            out var reason);

        Assert.False(ok);
        Assert.StartsWith("provider_output_", reason);
    }

    [Fact]
    public void MissingRequiredArraysFailsWithoutThrowing()
    {
        var ok = PublicKnowledgeProviderOutput.TryValidate(
            "completed",
            "{\"summary\":\"syntactically valid but incomplete\"}",
            FetchedUrls(),
            DateTime.UtcNow,
            14,
            out _,
            out var reason);

        Assert.False(ok);
        Assert.Equal("provider_output_citations_invalid", reason);
    }

    [Fact]
    public void ValidCandidateIsAcceptedAndPreservesBooleanRecheckFlag()
    {
        var now = DateTime.UtcNow;
        var ok = PublicKnowledgeProviderOutput.TryValidate(
            "completed",
            CreateValidResponse(now),
            FetchedUrls(),
            now,
            14,
            out var output,
            out var reason);

        Assert.True(ok, reason);
        Assert.NotNull(output);
        Assert.True(output.Candidates.Single().RecheckBeforeUse);
    }

    [Fact]
    public void UnfetchedCandidateSourceIsRejected()
    {
        var now = DateTime.UtcNow;
        var json = CreateValidResponse(now).Replace(SourceUrl, "https://example.com/unfetched", StringComparison.Ordinal);
        var ok = PublicKnowledgeProviderOutput.TryValidate(
            "completed",
            json,
            FetchedUrls(),
            now,
            14,
            out _,
            out var reason);

        Assert.False(ok);
        Assert.Equal("provider_output_citations_invalid", reason);
    }

    [Fact]
    public void StaleCandidateIsRejected()
    {
        var now = DateTime.UtcNow;
        var ok = PublicKnowledgeProviderOutput.TryValidate(
            "completed",
            CreateValidResponse(now.AddDays(-30)),
            FetchedUrls(),
            now,
            14,
            out _,
            out var reason);

        Assert.False(ok);
        Assert.Equal("provider_candidate_freshness_invalid", reason);
    }

    [Fact]
    public void StructuredOutputRequestUsesStrictResponsesTextFormat()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(PublicKnowledgeProviderOutput.BuildOpenAiTextFormat()));
        var format = document.RootElement.GetProperty("format");

        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        var candidate = format.GetProperty("schema").GetProperty("properties").GetProperty("candidates").GetProperty("items");
        Assert.Equal("boolean", candidate.GetProperty("properties").GetProperty("recheckBeforeUse").GetProperty("type").GetString());
    }

    [Fact]
    public void UsageEvidenceSeparatesReasoningTokens()
    {
        var evidence = PublicKnowledgeProviderOutput.ParseEvidence(
            "openai",
            "dedicated_public_source_key",
            "gpt-5-mini",
            "completed",
            1,
            "{\"input_tokens\":100,\"output_tokens\":60,\"output_tokens_details\":{\"reasoning_tokens\":40}}",
            null);

        Assert.Equal(100, evidence.InputTokens);
        Assert.Equal(60, evidence.OutputTokens);
        Assert.Equal(40, evidence.ReasoningTokens);
    }

    [Fact]
    public void DedicatedPublicSourceKeyFailsClosedInsteadOfUsingGenericKey()
    {
        Assert.Equal(string.Empty, PublicKnowledgeProviderOutput.SelectPublicSourceApiKey(null));
        Assert.Equal("dedicated", PublicKnowledgeProviderOutput.SelectPublicSourceApiKey("dedicated"));
    }

    [Fact]
    public void PromotionReceiptRequiresExactCandidateDestinationAndPullRequestUrl()
    {
        var candidateId = new string('a', 64);
        var destination = "glirette/NotaryGeekPublicKnowledgeWorker";

        Assert.Equal(
            destination,
            PublicKnowledgePromotionService.ValidateReceiptIdentity(
                candidateId,
                destination,
                DateTime.UtcNow,
                "https://github.com/glirette/NotaryGeekPublicKnowledgeWorker/pull/8",
                "Promotion"));
        Assert.Throws<ArgumentException>(() => PublicKnowledgePromotionService.ValidateReceiptIdentity(
            candidateId,
            "glirette/thisstuffiswaytootech",
            DateTime.UtcNow,
            "https://github.com/glirette/NotaryGeekPublicKnowledgeWorker/pull/8",
            "Promotion"));
        Assert.Throws<ArgumentException>(() => PublicKnowledgePromotionService.ValidateReceiptIdentity(
            "not-a-sha256",
            destination,
            DateTime.UtcNow,
            "https://github.com/glirette/NotaryGeekPublicKnowledgeWorker/pull/8",
            "Promotion"));
        Assert.Throws<ArgumentException>(() => PublicKnowledgePromotionService.ValidateReceiptIdentity(
            candidateId,
            destination,
            DateTime.UtcNow,
            "https://github.com/glirette/thisstuffiswaytootech/pull/13",
            "Promotion"));
    }

    [Fact]
    public void ReceiptContractsRejectUnknownFieldsAndKeepPromotionSeparateFromPublication()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var promotion = new PublicAuthorityPromotionReceipt(
            new string('a', 64),
            "glirette/NotaryGeekPublicKnowledgeWorker",
            DateTime.UtcNow,
            "https://github.com/glirette/NotaryGeekPublicKnowledgeWorker/pull/8");
        using var promotionJson = JsonDocument.Parse(JsonSerializer.Serialize(promotion, options));

        Assert.Equal(
            new[] { "candidateId", "destination", "promotedAtUtc", "pullRequestUrl" },
            promotionJson.RootElement.EnumerateObject().Select(property => property.Name));
        var publication = new PublicAuthorityPublicationReceipt(
            promotion.CandidateId,
            promotion.Destination,
            DateTime.UtcNow,
            promotion.PullRequestUrl);
        using var publicationJson = JsonDocument.Parse(JsonSerializer.Serialize(publication, options));
        Assert.Equal(
            new[] { "candidateId", "destination", "publishedAtUtc", "pullRequestUrl" },
            publicationJson.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PublicAuthorityPromotionReceipt>(
            "{\"candidateId\":\"" + new string('a', 64) + "\",\"destination\":\"glirette/NotaryGeekPublicKnowledgeWorker\",\"promotedAtUtc\":\"2026-08-03T00:00:00Z\",\"pullRequestUrl\":\"https://github.com/glirette/NotaryGeekPublicKnowledgeWorker/pull/8\",\"publishedAtUtc\":\"2026-08-03T00:00:00Z\"}",
            options));
    }

    [Fact]
    public void PumpRefusesProviderExecutionIncludingLegacyAuthorityEnvelopes()
    {
        Assert.False(PublicKnowledgeExecutionPolicy.ShouldCallProvider("pump-timer", "authority-generation", true));
        Assert.False(PublicKnowledgeExecutionPolicy.ShouldCallProvider("pump-timer", "regression", true));
        Assert.True(PublicKnowledgeExecutionPolicy.ShouldCallProvider("timer", "authority-generation", true));
    }

    [Fact]
    public void FreshnessRequiresMatchingAuthorityRunLaneAndBatch()
    {
        var freshAfterUtc = DateTime.UtcNow.AddHours(-24);
        var matching = CreateStoredRun("authority-generation", "notary", "DailySourceIngestion", DateTime.UtcNow);

        Assert.True(PublicKnowledgeExecutionPolicy.IsFreshAuthorityRun(
            matching, "DailySourceIngestion", "notary", freshAfterUtc));
        Assert.False(PublicKnowledgeExecutionPolicy.IsFreshAuthorityRun(
            matching with { Batch = "Core" }, "DailySourceIngestion", "notary", freshAfterUtc));
        Assert.False(PublicKnowledgeExecutionPolicy.IsFreshAuthorityRun(
            matching with { Result = matching.Result with { RunKind = "regression" } },
            "DailySourceIngestion", "notary", freshAfterUtc));
        Assert.False(PublicKnowledgeExecutionPolicy.IsFreshAuthorityRun(
            matching with { Result = matching.Result with { AuthorityLane = "technical" } },
            "DailySourceIngestion", "notary", freshAfterUtc));
        Assert.False(PublicKnowledgeExecutionPolicy.IsFreshAuthorityRun(
            matching with { StoredAtUtc = freshAfterUtc.AddSeconds(-1) },
            "DailySourceIngestion", "notary", freshAfterUtc));
    }

    [Fact]
    public void HighCostCapIsAppliedAfterAuthorityAndRepairBudgetSelection()
    {
        Assert.Equal(6000, PublicKnowledgeExecutionPolicy.SelectOutputTokenBudget(
            1600, 6000, 8000, 1600, authorityRun: true, repairAttempt: false, highCostGuardActive: false));
        Assert.Equal(8000, PublicKnowledgeExecutionPolicy.SelectOutputTokenBudget(
            1600, 6000, 8000, 1600, authorityRun: true, repairAttempt: true, highCostGuardActive: false));
        Assert.Equal(1600, PublicKnowledgeExecutionPolicy.SelectOutputTokenBudget(
            1600, 6000, 8000, 1600, authorityRun: true, repairAttempt: false, highCostGuardActive: true));
        Assert.Equal(1600, PublicKnowledgeExecutionPolicy.SelectOutputTokenBudget(
            1600, 6000, 8000, 1600, authorityRun: true, repairAttempt: true, highCostGuardActive: true));
    }

    [Fact]
    public void DailyAuthorityJobIdentityIsStableForUtcDayAndBatch()
    {
        var morning = PublicKnowledgeExecutionPolicy.CreateDailyAuthorityJobId(
            "DailySourceIngestion", new DateTime(2026, 8, 3, 9, 17, 0, DateTimeKind.Utc));
        var retry = PublicKnowledgeExecutionPolicy.CreateDailyAuthorityJobId(
            "DailySourceIngestion", new DateTime(2026, 8, 3, 10, 2, 0, DateTimeKind.Utc));

        Assert.Equal("authority-20260803-dailysourceingestion", morning);
        Assert.Equal(morning, retry);
    }

    [Fact]
    public void AuthorityPromptDefinesReviewTimestampAsCurrentRunTime()
    {
        var reviewedAtUtc = new DateTime(2026, 8, 4, 3, 45, 0, DateTimeKind.Utc);
        var instruction = PublicKnowledgeProviderOutput.BuildReviewTimestampInstruction(reviewedAtUtc);

        Assert.Contains("2026-08-04T03:45:00.0000000Z", instruction, StringComparison.Ordinal);
        Assert.Contains("this run's fetch/review time", instruction, StringComparison.Ordinal);
        Assert.Contains("do not copy dates embedded in source content", instruction, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("preparing", "active")]
    [InlineData("queued", "active")]
    [InlineData("running", "active")]
    [InlineData("completed", "completed")]
    [InlineData("completed-with-errors", "failed")]
    [InlineData("completed-empty", "failed")]
    [InlineData("failed", "failed")]
    public void BacklogBucketsDoNotReportTerminalFailuresAsActive(string status, string expected)
    {
        Assert.Equal(expected, PublicKnowledgeExecutionPolicy.GetBacklogBucket(status));
    }

    private static HashSet<string> FetchedUrls() => new(StringComparer.OrdinalIgnoreCase) { SourceUrl };

    private static PublicKnowledgeStoredRunEnvelope CreateStoredRun(
        string runKind,
        string authorityLane,
        string batch,
        DateTime storedAtUtc)
    {
        var output = new PublicKnowledgeStructuredOutput(
            "Usable structured output.", [], [], [], [], [], [], [SourceUrl], []);
        var result = new PublicKnowledgeRunResult(
            true,
            true,
            true,
            false,
            "completed",
            storedAtUtc,
            "test",
            "case-1",
            null,
            1,
            100,
            25,
            "gpt-5-mini",
            [],
            "{}",
            "completed",
            "{}",
            [],
            [],
            StructuredOutput: output,
            RunKind: runKind,
            AuthorityLane: authorityLane);
        return new PublicKnowledgeStoredRunEnvelope(
            "notary-geek-public-knowledge-run/v1",
            "0.1-public",
            storedAtUtc,
            "timer",
            batch,
            "case-1",
            "runs/test.json",
            "runs/latest/case-1.json",
            result);
    }

    private static string CreateValidResponse(DateTime reviewedAtUtc) => JsonSerializer.Serialize(new
    {
        summary = "Structured Outputs constrain generated JSON to the supplied schema.",
        routeFindings = Array.Empty<string>(),
        sourceQualityFindings = new[] { "The cited first-party guide documents the API shape." },
        suggestedPublicReplies = Array.Empty<string>(),
        websiteBriefs = Array.Empty<string>(),
        lawRefreshCandidates = Array.Empty<string>(),
        risks = new[] { "Model output still requires application-side validation." },
        citations = new[] { SourceUrl },
        candidates = new[]
        {
            new
            {
                topicId = "openai-structured-outputs",
                title = "OpenAI Responses structured outputs",
                summary = "The Responses API accepts strict JSON Schema through text.format.",
                reviewedAtUtc,
                recheckBeforeUse = true,
                sources = new[]
                {
                    new
                    {
                        url = SourceUrl,
                        title = "Structured model outputs",
                        publisher = "OpenAI",
                        kind = "first-party-documentation",
                        reviewedAtUtc,
                        supports = "Responses API text.format JSON Schema behavior."
                    }
                },
                supports = new[] { "Use text.format with strict JSON Schema." },
                doesNotProve = new[] { "Application-specific output quality or correctness." }
            }
        }
    });
}
