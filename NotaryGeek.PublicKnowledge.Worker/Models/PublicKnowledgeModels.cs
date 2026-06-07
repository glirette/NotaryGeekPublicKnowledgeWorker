using System.Text.Json.Serialization;

namespace NotaryGeek.PublicKnowledge.Worker.Models;

public sealed record PublicKnowledgeRunCommand(
    bool Execute,
    bool FromTimer,
    string Focus,
    IReadOnlyList<string> RequestedUrls,
    string? RegressionCaseId,
    PublicKnowledgeRegressionCase? RegressionCase);

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
    IReadOnlyList<string> Errors);

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
    IReadOnlyList<string> FailureSignals);

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
    [property: JsonPropertyName("failureSignals")] IReadOnlyList<string>? FailureSignals);
