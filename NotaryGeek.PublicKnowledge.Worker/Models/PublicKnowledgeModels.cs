using System.Text.Json.Serialization;

namespace NotaryGeek.PublicKnowledge.Worker.Models;

public sealed record PublicKnowledgeRunCommand(
    bool Execute,
    bool FromTimer,
    string Focus,
    IReadOnlyList<string> RequestedUrls,
    string? RegressionCaseId,
    PublicKnowledgeRegressionCase? RegressionCase,
    string? ProviderOverride = null,
    string RunKind = "regression",
    string AuthorityLane = "notary");

public sealed record PublicKnowledgeRunResult(
    bool Ok,
    bool Execute,
    bool OpenAiCalled,
    bool Skipped,
    string Status,
    DateTime CheckedAtUtc,
    string Focus,
    string? RegressionCaseId,
    PublicKnowledgeRegressionCase? RegressionCase,
    int SourceCount,
    int PromptCharacters,
    int EstimatedInputTokens,
    string Model,
    IReadOnlyList<PublicKnowledgeSourceResult> Sources,
    string? ResponseText,
    string? ProviderStatus,
    string? ProviderUsageJson,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    PublicKnowledgeRegressionScore? RegressionScore = null,
    string? Provider = null,
    PublicKnowledgeProviderEvidence? ProviderEvidence = null,
    PublicKnowledgeStructuredOutput? StructuredOutput = null,
    string RunKind = "regression",
    string AuthorityLane = "notary")
{
    public bool ProviderCalled => OpenAiCalled;
}

public sealed record PublicKnowledgeRegressionScore(
    string Schema,
    string Version,
    string Verdict,
    string Strategy,
    string Note,
    int MustHoldTotal,
    int MustHoldPassed,
    int MustHoldMissing,
    int FailureSignalTotal,
    int FailureSignalsObserved,
    IReadOnlyList<PublicKnowledgeRegressionRuleCheck> MustHoldChecks,
    IReadOnlyList<PublicKnowledgeRegressionRuleCheck> FailureSignalChecks);

public sealed record PublicKnowledgeRegressionRuleCheck(
    string Rule,
    string Status,
    bool Matched,
    int MatchedTokenCount,
    int RequiredTokenCount,
    IReadOnlyList<string> MatchedTokens,
    IReadOnlyList<string> MissingTokens);

public sealed record PublicKnowledgeQueuedRunMessage(
    string JobId,
    string Batch,
    string Trigger,
    bool Execute,
    IReadOnlyList<string> CaseIds,
    DateTime SubmittedAtUtc,
    string? CaseId = null,
    string? ProviderOverride = null,
    string RunKind = "regression",
    string AuthorityLane = "notary");

public sealed record PublicKnowledgeProviderEvidence(
    string Provider,
    string AuthMode,
    string Model,
    string ResponseStatus,
    int Attempts,
    int InputTokens,
    int OutputTokens,
    int ReasoningTokens,
    string? FailureReason);

public sealed record PublicKnowledgeStructuredOutput(
    string Summary,
    IReadOnlyList<string> RouteFindings,
    IReadOnlyList<string> SourceQualityFindings,
    IReadOnlyList<string> SuggestedPublicReplies,
    IReadOnlyList<string> WebsiteBriefs,
    IReadOnlyList<string> LawRefreshCandidates,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Citations,
    IReadOnlyList<PublicAuthorityCandidateDraft> Candidates);

public sealed record PublicAuthorityCandidateDraft(
    string TopicId,
    string Title,
    string Summary,
    DateTime ReviewedAtUtc,
    bool RecheckBeforeUse,
    IReadOnlyList<PublicAuthoritySourceDraft> Sources,
    IReadOnlyList<string> Supports,
    IReadOnlyList<string> DoesNotProve);

public sealed record PublicAuthoritySourceDraft(
    string Url,
    string Title,
    string Publisher,
    string Kind,
    DateTime ReviewedAtUtc,
    string Supports);

public sealed record PublicAuthorityCandidateFeed(
    string Schema,
    DateTime GeneratedAtUtc,
    IReadOnlyList<PublicAuthorityCandidate> Candidates);

public sealed record PublicAuthorityCandidate(
    string CandidateId,
    string Destination,
    string TopicId,
    string Title,
    string Summary,
    DateTime ReviewedAtUtc,
    bool RecheckBeforeUse,
    IReadOnlyList<PublicAuthoritySourceDraft> Sources,
    IReadOnlyList<string> Supports,
    IReadOnlyList<string> DoesNotProve,
    PublicAuthorityGeneratorEvidence GeneratorEvidence);

public sealed record PublicAuthorityGeneratorEvidence(
    string Provider,
    string AuthMode,
    string Model,
    string RunId,
    DateTime GeneratedAtUtc,
    PublicAuthorityUsage Usage);

public sealed record PublicAuthorityUsage(
    int InputTokens,
    int OutputTokens,
    int ReasoningTokens);

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record PublicAuthorityPromotionReceipt(
    string CandidateId,
    string Destination,
    DateTime PromotedAtUtc,
    string PullRequestUrl);

[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record PublicAuthorityPublicationReceipt(
    string CandidateId,
    string Destination,
    DateTime PublishedAtUtc,
    string PullRequestUrl);

public sealed record PublicAuthorityPromotionStatus(
    int CandidateCount,
    int PromotedCandidateCount,
    int PublishedCandidateCount,
    DateTime? LastCandidateUtc,
    DateTime? LastSuccessfulPromotionUtc,
    DateTime? LastSuccessfulPublicationUtc,
    int RunHistoryRetentionDays,
    int JobEnvelopeRetentionDays,
    string RetentionMode);

public sealed record PublicKnowledgeSourceResult(
    string Url,
    bool Ok,
    int StatusCode,
    string? ContentType,
    int CharacterCount,
    string Note);

public sealed record PublicKnowledgeManifest(
    string Schema,
    string Version,
    string Purpose,
    string CanonicalRoutingModel,
    PublicKnowledgePolicy PublicOnlyPolicy,
    IReadOnlyList<PublicKnowledgeSourceSet> SourceSets,
    IReadOnlyList<string> StrictExclusions);

public sealed record PublicKnowledgePolicy(
    bool PublicOnly,
    string Summary);

public sealed record PublicKnowledgeSourceSet(
    string Name,
    string Use,
    IReadOnlyList<string> Urls);

public sealed record PublicKnowledgeRegressionMatrix(
    string Schema,
    string Version,
    string ReviewedUtc,
    string Purpose,
    string PublicOnlyPolicy,
    IReadOnlyList<PublicKnowledgeRegressionCase> Cases);

public sealed record PublicKnowledgeRegressionCase(
    string Id,
    string Focus,
    string Purpose,
    IReadOnlyList<string> MustHold,
    IReadOnlyList<string> FailureSignals,
    IReadOnlyList<string> SourceUrls);

internal sealed record PublicKnowledgeManifestDto(
    [property: JsonPropertyName("schema")] string? Schema,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("purpose")] string? Purpose,
    [property: JsonPropertyName("canonicalRoutingModel")] string? CanonicalRoutingModel,
    [property: JsonPropertyName("publicOnlyPolicy")] PublicKnowledgePolicyDto? PublicOnlyPolicy,
    [property: JsonPropertyName("sourceSets")] IReadOnlyList<PublicKnowledgeSourceSetDto>? SourceSets,
    [property: JsonPropertyName("strictExclusions")] IReadOnlyList<string>? StrictExclusions);

internal sealed record PublicKnowledgePolicyDto(
    [property: JsonPropertyName("publicOnly")] bool PublicOnly,
    [property: JsonPropertyName("summary")] string? Summary);

internal sealed record PublicKnowledgeSourceSetDto(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("use")] string? Use,
    [property: JsonPropertyName("urls")] IReadOnlyList<string>? Urls);

internal sealed record PublicKnowledgeRegressionMatrixDto(
    [property: JsonPropertyName("schema")] string? Schema,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("reviewedUtc")] string? ReviewedUtc,
    [property: JsonPropertyName("purpose")] string? Purpose,
    [property: JsonPropertyName("publicOnlyPolicy")] string? PublicOnlyPolicy,
    [property: JsonPropertyName("cases")] IReadOnlyList<PublicKnowledgeRegressionCaseDto>? Cases);

internal sealed record PublicKnowledgeRegressionCaseDto(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("focus")] string? Focus,
    [property: JsonPropertyName("purpose")] string? Purpose,
    [property: JsonPropertyName("mustHold")] IReadOnlyList<string>? MustHold,
    [property: JsonPropertyName("failureSignals")] IReadOnlyList<string>? FailureSignals,
    [property: JsonPropertyName("sourceUrls")] IReadOnlyList<string>? SourceUrls);
