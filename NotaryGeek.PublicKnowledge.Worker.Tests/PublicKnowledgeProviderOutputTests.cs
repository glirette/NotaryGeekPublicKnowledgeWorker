using System.Reflection;
using System.Text.Json;
using NotaryGeek.PublicKnowledge.Worker.Models;
using NotaryGeek.PublicKnowledge.Worker.Services;

namespace NotaryGeek.PublicKnowledge.Worker.Tests;

public sealed class PublicKnowledgeProviderOutputTests
{
    private const string SourceUrl = "https://developers.openai.com/api/docs/guides/structured-outputs";

    [Theory]
    [InlineData(
        "llms-txt-discovery-not-provider-ingestion-proof",
        "llms.txt is automatically read",
        "llms.txt isn't automatically read, indexed, absorbed, or used by Google AI or another provider.",
        "clear-corrective-mention")]
    [InlineData(
        "llms-txt-discovery-not-provider-ingestion-proof",
        "publication of llms.txt",
        "Publishing llms.txt doesn't prove crawler access or model ingestion.",
        "clear-corrective-mention")]
    [InlineData(
        "llms-txt-discovery-not-provider-ingestion-proof",
        "improves or dilutes provider answers",
        "Adding resources might improve or dilute answers, but provider documentation, logs, or reproducible evidence is needed.",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "A state might require an NNA background check or NNA certification.",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "That might mean a state requires NNA background certification.",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "Who might say a state requires NNA background certification?",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "Which might mean a state requires NNA background certification?",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "Experts say that might mean a state requires NNA background certification.",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "Experts say that a state might conclude its law requires NNA background certification.",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "A report, which might say a state requires NNA background certification, was published.",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "A state that publishes guidance might require an NNA background check or NNA certification.",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "A state might require an NNA background check or an NNA certification that is current.",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "A state might require an NNA background check or an NNA certification required by the lender.",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "A state might require an NNA background check or a state certification from the NNA.",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "A state might require an NNA background check or an NNA certification for claims processing.",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "A state might require an NNA background check or an NNA certification the lender requires for closings.",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "A state might require routine paperwork and an NNA certification that is current for a state background check.",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "A state might require routine paperwork or an NNA certification for a state background check that the lender requires for closings.",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "A state might require routine paperwork or an NNA certification for a state background check that lenders require for closings.",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "completely legitimate",
        "The NNA might be the gold standard, and be completely legitimate and highly trusted.",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "completely legitimate",
        "The NNA might, depending on timing and context, be completely legitimate, highly trusted, the gold standard, and justify a compliance conclusion.",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "completely legitimate",
        "The NNA might be reputable and completely legitimate, highly trusted, the gold standard, and a sound basis for a compliance conclusion.",
        "clear-corrective-mention")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "completely legitimate",
        "The NNA might be completely legitimate and highly trusted, but it might also be the gold standard and justify that compliance conclusion.",
        "clear-corrective-mention")]
    [InlineData(
        "llms-txt-discovery-not-provider-ingestion-proof",
        "Publishing discovery metadata",
        "Publisher-controlled discovery metadata and provider-controlled retrieval or training behavior are distinct.",
        "clear")]
    [InlineData(
        "source-quality-term-context-not-invented-taxonomy",
        "relevant source-quality categories",
        "Geographic, performance, school, and cyber are not source-quality categories established by the supplied source.",
        "clear-corrective-mention")]
    public void CorrectiveResponsesDoNotTriggerFailureSignals(
        string regressionCaseId,
        string failureSignalFragment,
        string responseText,
        string expectedStatus)
    {
        var score = ScoreRegressionResponse(regressionCaseId, responseText);
        var failureSignalCheck = score.FailureSignalChecks.Single(item =>
            item.Rule.Contains(failureSignalFragment, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(0, score.FailureSignalsObserved);
        Assert.Equal(expectedStatus, failureSignalCheck.Status);
    }

    [Theory]
    [InlineData(
        "llms-txt-discovery-not-provider-ingestion-proof",
        "improves or dilutes provider answers",
        "Adding more resources to llms.txt automatically improves or dilutes provider answers.")]
    [InlineData(
        "llms-txt-discovery-not-provider-ingestion-proof",
        "Publishing discovery metadata",
        "Publishing discovery metadata proves that providers automatically retrieve it for training.")]
    [InlineData(
        "source-quality-term-context-not-invented-taxonomy",
        "relevant source-quality categories",
        "The relevant source-quality categories are geographic, performance, school, and cyber.")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "A state requires NNA background certification, and notaries might face penalties.")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "Notaries might face penalties, and a state requires NNA background certification.")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "Notaries might face penalties and a state requires NNA background certification.")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "Notaries might face penalties, and California state law requires NNA background certification.")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "Notaries might face penalties, or California state law that applies requires NNA background certification.")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "Notaries might face penalties, or California state law that the legislature enacted requires NNA background certification.")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "A state requires NNA background certification, and notaries might face penalties for missing that certification.")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "A state requires NNA background certification because notaries might face penalties without it.")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "A state requires NNA background certification, and notaries who omit it might face penalties.")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "A state requires NNA background certification, and notaries that omit it might face penalties.")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "A state requires NNA background certification, and notaries which omit it might face penalties.")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "California state law, which might change, requires NNA background certification.")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "California state law that notaries might discuss requires NNA background certification.")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "California state law that notaries working for large national mortgage lenders might discuss requires NNA background certification.")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "Although notaries might face penalties, a state requires NNA background certification.")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "Although notaries might face penalties, California state law, which might change, requires NNA background certification.")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "A state requires NNA background certification, and notaries working for large national mortgage lenders might face penalties.")]
    [InlineData(
        "nna-legitimacy-not-legal-authority",
        "state requires an NNA background check or NNA certification",
        "California state law requires NNA background certification and that might worry notaries.")]
    public void DirectOverclaimsTriggerFailureSignals(
        string regressionCaseId,
        string failureSignalFragment,
        string responseText)
    {
        var score = ScoreRegressionResponse(regressionCaseId, responseText);
        var failureSignalCheck = score.FailureSignalChecks.Single(item =>
            item.Rule.Contains(failureSignalFragment, StringComparison.OrdinalIgnoreCase));

        Assert.Equal("observed", failureSignalCheck.Status);
        Assert.True(failureSignalCheck.Matched);
    }

    [Theory]
    [InlineData("A state might require routine paperwork and an NNA certification for a state background check that lenders require for closings.")]
    [InlineData("Depending on the jurisdiction, a state might require routine paperwork and an NNA certification for a state background check that lenders require for closings.")]
    [InlineData("A state might require routine paperwork and an NNA certification that lenders claim proves completion of a state background check.")]
    [InlineData("Notaries might face penalties and California state law that applies requires NNA background certification.")]
    [InlineData("Notaries might face penalties and California state law that the legislature wrote requires NNA background certification.")]
    public void AmbiguousModalScopesRequireHumanReview(string responseText)
    {
        var score = ScoreRegressionResponse("nna-legitimacy-not-legal-authority", responseText);
        var failureSignalCheck = score.FailureSignalChecks.Single(item =>
            item.Rule.Contains(
                "state requires an NNA background check or NNA certification",
                StringComparison.OrdinalIgnoreCase));

        Assert.Equal(0, score.FailureSignalsObserved);
        Assert.Equal("needs-review", score.Verdict);
        Assert.False(failureSignalCheck.Matched);
        Assert.Equal("ambiguous-modal-scope", failureSignalCheck.Status);
    }

    private static PublicKnowledgeRegressionScore ScoreRegressionResponse(
        string regressionCaseId,
        string responseText)
    {
        var matrixPath = Path.Combine(
            AppContext.BaseDirectory,
            "public-knowledge",
            "public-knowledge-regression-matrix.json");
        var matrix = JsonSerializer.Deserialize<PublicKnowledgeRegressionMatrix>(
            File.ReadAllText(matrixPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var regressionCase = Assert.Single(
            Assert.IsType<PublicKnowledgeRegressionMatrix>(matrix).Cases,
            item => string.Equals(item.Id, regressionCaseId, StringComparison.Ordinal));
        var scoreMethod = typeof(PublicKnowledgeResearchService).GetMethod(
            "ScoreRegressionResponse",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(scoreMethod);
        return Assert.IsType<PublicKnowledgeRegressionScore>(
            scoreMethod.Invoke(null, [regressionCase, responseText]));
    }

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
        Assert.Equal("gpt-5-mini", evidence.RequestedModel);
        Assert.Equal("gpt-5-mini", evidence.Model);
        Assert.Null(evidence.CostUsd);
        Assert.Equal("not_returned_by_provider_response", evidence.CostEvidence);
        Assert.Equal("not_reported_by_provider_response", evidence.IncentiveEvidence);
    }

    [Fact]
    public void ProviderEvidenceSeparatesRequestedAndResolvedModelAndServiceTier()
    {
        var evidence = PublicKnowledgeProviderOutput.ParseEvidence(
            "openai",
            "dedicated_public_source_key",
            "gpt-5-mini",
            "completed",
            1,
            "{\"input_tokens\":10,\"output_tokens\":5}",
            null,
            "gpt-5-mini-2025-08-07",
            "default");

        Assert.Equal("gpt-5-mini", evidence.RequestedModel);
        Assert.Equal("gpt-5-mini-2025-08-07", evidence.Model);
        Assert.Equal("default", evidence.ServiceTier);
        Assert.Null(evidence.CostUsd);
    }

    [Fact]
    public void LegacyProviderEvidenceDefaultsToUnknownCostAndIncentiveTreatment()
    {
        var evidence = JsonSerializer.Deserialize<PublicKnowledgeProviderEvidence>("""
            {
              "provider": "openai",
              "authMode": "dedicated_public_source_key",
              "model": "gpt-5-mini",
              "responseStatus": "completed",
              "attempts": 1,
              "inputTokens": 10,
              "outputTokens": 5,
              "reasoningTokens": 0,
              "failureReason": null
            }
            """, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(evidence);
        Assert.Null(evidence.RequestedModel);
        Assert.Null(evidence.CostUsd);
        Assert.Equal("not_returned_by_provider_response", evidence.CostEvidence);
        Assert.Equal("not_reported_by_provider_response", evidence.IncentiveEvidence);
    }

    [Fact]
    public void DedicatedPublicSourceKeyFailsClosedInsteadOfUsingGenericKey()
    {
        Assert.Equal(string.Empty, PublicKnowledgeProviderOutput.SelectPublicSourceApiKey(null));
        Assert.Equal("dedicated", PublicKnowledgeProviderOutput.SelectPublicSourceApiKey("dedicated"));
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
    public void NeedsGregReportExplainsAmbiguousModalScope()
    {
        var storedAtUtc = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        var envelope = CreateStoredRun("regression", "notary", "test", storedAtUtc);
        var ruleCheck = new PublicKnowledgeRegressionRuleCheck(
            "state requires an NNA background check or NNA certification",
            "ambiguous-modal-scope",
            false,
            3,
            3,
            ["state", "background", "certification"],
            []);
        var score = new PublicKnowledgeRegressionScore(
            "notary-geek-public-knowledge-regression-score-v1",
            "0.1-public",
            "needs-review",
            "deterministic-surface-triage-v1",
            "Human review required.",
            1,
            1,
            0,
            1,
            0,
            [new PublicKnowledgeRegressionRuleCheck(
                "must hold",
                "passed",
                true,
                1,
                1,
                ["hold"],
                [])],
            [ruleCheck]);
        envelope = envelope with
        {
            Result = envelope.Result with { RegressionScore = score }
        };
        var method = typeof(PublicKnowledgeRunStorageService).GetMethod(
            "BuildNeedsGregItem",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var item = Assert.IsType<PublicKnowledgeNeedsGregItem>(method.Invoke(null, [envelope]));
        Assert.Contains("ambiguous modal scope", item.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ambiguous failure-signal", item.SuggestedNextAction, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("missing must-hold", item.SuggestedNextAction, StringComparison.OrdinalIgnoreCase);
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
