using System.Collections;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotaryGeek.PublicKnowledge.Worker.Configuration;
using NotaryGeek.PublicKnowledge.Worker.Models;
using NotaryGeek.PublicKnowledge.Worker.Services;

// KBA adaptation of the reviewed #38 production-method harness. The linked
// production methods select, fetch, normalize and build; no parallel evaluator.
var root = Path.GetFullPath(args.Length == 0 ? "." : args[0]);
var corpus = Path.Combine(root, "NotaryGeek.PublicKnowledge.Worker/public-knowledge");
var directory = Path.Combine(corpus, "evidence/kba-worker-2026-10-30");
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var assertions = 0;
void Check(bool ok, string message) { assertions++; if (!ok) throw new InvalidOperationException(message); }
JsonNode Read(string path) => JsonNode.Parse(File.ReadAllText(path))!;
string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
const string pin = "e4c31b4a0b3c2f223978dcb06c6af64f6bb4385d";
const string prefix = "https://raw.githubusercontent.com/glirette/NotaryGeekPublicKnowledgeWorker/";
const string relative = "NotaryGeek.PublicKnowledge.Worker/public-knowledge/";
const string caseBase = prefix + "main/" + relative + "evidence/kba-worker-2026-10-30/";
var bindings = new JsonObject();
var originals = new Dictionary<string, JsonNode>();
foreach (var (key, name, size, sha) in new[] {
    ("matrix", "kba-claim-source-matrix-2026-10-30.json", 27486, "e77ca70072fad0bb1b6e2c3bae0648d73e5fff44e36831e29e62fec15dfa0a0e"),
    ("proposal", "kba-routing-proposal-2026-10-30.json", 13634, "48241103f81e8a28a5281f6c7068e2539c0f92d17b21b997932826d90441a10d") })
{
    var bytes = File.ReadAllBytes(Path.Combine(corpus, "research", name));
    Check(bytes.Length == size && Hash(bytes) == sha, "Pinned research bytes changed: " + name);
    originals[key] = JsonNode.Parse(bytes)!;
    bindings[key] = new JsonObject { ["url"] = prefix + pin + "/" + relative + "research/" + name, ["sha256"] = sha, ["utf8Bytes"] = size };
}
var matrix = originals["matrix"];
var proposal = originals["proposal"];
var package = Read(Path.Combine(directory, "package.json"));
void Binding(JsonNode node) => Check(JsonNode.DeepEquals(node["researchBindings"], bindings), "Research binding differs");
Binding(package);
var expectedNames = new[] { "records-1.json", "records-2.json", "records-3.json", "regression-inputs.json", "discovery-proposal.json" };
Check(package["artifacts"]!.AsArray().Select(n => n!["path"]!.GetValue<string>()).SequenceEqual(expectedNames), "Package artifact inventory differs");
foreach (var artifact in package["artifacts"]!.AsArray())
{
    var name = artifact!["path"]!.GetValue<string>();
    var bytes = File.ReadAllBytes(Path.Combine(directory, name));
    Check(Hash(bytes) == artifact["sha256"]!.GetValue<string>() && bytes.Length == artifact["utf8Bytes"]!.GetValue<int>() && Encoding.UTF8.GetString(bytes).Length == artifact["utf16Characters"]!.GetValue<int>(), "Artifact byte binding differs: " + name);
}
var productionHashes = new Dictionary<string, string>();
foreach (var (path, sha) in new[] {
    ("Services/PublicKnowledgeResearchService.cs", "46815cfd1c8ad9ed3ee904e95806e7798482820d9cdf95a323ec3877bffe0494"),
    ("Services/PublicKnowledgeProviderOutput.cs", "5b33a7c9831708d609685098c012cfd87f536c19eb5474dcdb82bf25bba792b8"),
    ("Services/PublicKnowledgeExecutionPolicy.cs", "9fd48012592067d937f961132dc95c8d71d421af1451e52597a99f10eb7971e3"),
    ("Models/PublicKnowledgeModels.cs", "836dc4a6295c7ad182b8bfa989d5d85bfc36d59dff2cad83f38cfff04a829cba"),
    ("Configuration/PublicKnowledgeOptions.cs", "449822b8e083cc934e8c1c29f3f6c54dc6b90ff2e19dcbdf1365da322ef45b61"),
    ("Configuration/OpenAiOptions.cs", "fb5d5ff703da962c9e5339152e228ff62c27e76f0fdfb7c53d4d9c7528d96553"),
    ("Configuration/StraicoOptions.cs", "291c9ca040beb0a9f1bbed528949b74f5a50d25980567a83e0db37ad3a68b706") })
{
    productionHashes[path] = Hash(File.ReadAllBytes(Path.Combine(root, "NotaryGeek.PublicKnowledge.Worker", path)));
    Check(productionHashes[path] == sha, "Linked production source changed: " + path);
}
var manifest = JsonSerializer.Deserialize<PublicKnowledgeManifest>(File.ReadAllText(Path.Combine(corpus, "public-knowledge-manifest.json")), options)!;
var parseMatrix = typeof(PublicKnowledgeResearchService).GetMethod("ParseRegressionMatrix", BindingFlags.NonPublic | BindingFlags.Static)!;
var inputs = (PublicKnowledgeRegressionMatrix)parseMatrix.Invoke(null, [File.ReadAllText(Path.Combine(directory, "regression-inputs.json"))])!;
var limits = new PublicKnowledgeOptions();
Check(limits.MaxSourcesPerRun == 24 && limits.MaxCharactersPerSource == 20000 && limits.MaxInputCharacters == 60000 && limits.MaxBytesPerSource == 750000 && limits.MaxEstimatedInputTokens == 18000, "Reassess changed production defaults");
var service = new PublicKnowledgeResearchService(new ForbiddenFactory(), Options.Create(limits), Options.Create(new OpenAiOptions()), Options.Create(new StraicoOptions()), NullLogger<PublicKnowledgeResearchService>.Instance);
var type = typeof(PublicKnowledgeResearchService);
var select = type.GetMethod("SelectSourceUrls", BindingFlags.NonPublic | BindingFlags.Instance)!;
var fetch = type.GetMethod("FetchSourceAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
var build = type.GetMethod("BuildPrompt", BindingFlags.NonPublic | BindingFlags.Instance)!;
var estimate = type.GetMethod("EstimateTokens", BindingFlags.NonPublic | BindingFlags.Static)!;
var bodyType = type.GetNestedType("SourceBody", BindingFlags.NonPublic)!;
string Content(object body) => (string)bodyType.GetProperty("Content")!.GetValue(body)!;
var sourceUrls = expectedNames.Take(3).Select(n => caseBase + n).ToArray();
var map = sourceUrls.ToDictionary(u => u, u => File.ReadAllBytes(Path.Combine(directory, new Uri(u).Segments[^1])), StringComparer.Ordinal);
var originalMatrixUrl = bindings["matrix"]!["url"]!.GetValue<string>();
map[originalMatrixUrl] = File.ReadAllBytes(Path.Combine(corpus, "research/kba-claim-source-matrix-2026-10-30.json"));
using var handler = new InMemorySources(map);
using var client = new HttpClient(handler);
var baselineTask = (Task)fetch.Invoke(service, [client, originalMatrixUrl, CancellationToken.None])!;
await baselineTask;
var baselineTuple = baselineTask.GetType().GetProperty("Result")!.GetValue(baselineTask)!;
var baselineResult = (PublicKnowledgeSourceResult)baselineTuple.GetType().GetField("Item1")!.GetValue(baselineTuple)!;
var baselineBody = baselineTuple.GetType().GetField("Item2")!.GetValue(baselineTuple)!;
Check(baselineResult.Ok && baselineResult.Note.StartsWith("ok-truncated:", StringComparison.Ordinal) && baselineResult.CharacterCount == 20000, "Unsplit research truncation not reproduced");
Check(!Content(baselineBody).Contains("\"sourceRefreshLimits\"", StringComparison.Ordinal), "Unsplit omission of refresh qualifications not reproduced");
var defaults = (IReadOnlyList<string>)select.Invoke(service, [manifest, Array.Empty<string>(), new List<string>()])!;
var defaultExpected = manifest.SourceSets.SelectMany(s => s.Urls).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase).Take(24);
Check(defaults.SequenceEqual(defaultExpected) && defaults.Count == 24 && !defaults.Intersect(sourceUrls).Any(), "Default source selection changed");
var discovery = Read(Path.Combine(directory, "discovery-proposal.json"));
Binding(discovery);
Check(discovery["notApplied"]!.GetValue<bool>() && discovery["status"]!.GetValue<string>() == "proposal-only-not-registered", "Discovery must remain unapplied");
var sharedMatrix = Read(Path.Combine(corpus, "public-knowledge-regression-matrix.json"));
Check(!sharedMatrix["cases"]!.AsArray().Any(c => c!["id"]!.GetValue<string>().StartsWith("kba-", StringComparison.Ordinal)), "KBA cases unexpectedly registered");
var rules = proposal["rules"]!.AsArray().Select(n => n!).ToArray();
var examples = proposal["regressions"]!.AsArray().Select(n => n!).ToArray();
var ids = rules.Select(n => "kba-rule-" + n["id"]!.GetValue<string>().ToLowerInvariant()).Concat(examples.Select(n => "kba-example-" + n["id"]!.GetValue<string>().ToLowerInvariant())).ToArray();
Check(inputs.Cases.Select(c => c.Id).SequenceEqual(ids) && ids.Length == 29, "Case inventory differs");
Check(discovery["proposedCaseIds"]!.AsArray().Select(n => n!.GetValue<string>()).SequenceEqual(ids), "Discovery case inventory differs");
Check(discovery["proposedSourceUrls"]!.AsArray().Select(n => n!.GetValue<string>()).SequenceEqual(sourceUrls), "Discovery source inventory differs");
string[] Strings(JsonNode n, string key) => n[key]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
var reports = new List<object>();
foreach (var (c, index) in inputs.Cases.Select((c, i) => (c, i)))
{
    string expectedFocus;
    string[] must, failures;
    if (index < rules.Length)
    {
        var rule = rules[index];
        expectedFocus = "Review dated KBA routing proposal " + rule["id"]!.GetValue<string>() + ": " + rule["trigger"]!.GetValue<string>() + ". Use all three complete research parts. Preserve qualifications and competing interpretations; do not decide transaction validity.";
        must = Strings(rule, "requiredActions"); failures = Strings(rule, "mustNotConclude");
    }
    else
    {
        var example = examples[index - rules.Length];
        var selectedRules = rules.Where(r => Strings(example, "ruleIds").Contains(r["id"]!.GetValue<string>())).ToArray();
        expectedFocus = "Review synthetic KBA example " + example["id"]!.GetValue<string>() + ": " + example["input"]!.GetValue<string>() + " Use all three complete research parts and its named rules. Preserve the expected disposition and meaning as proposed, not an adjudicated result.";
        must = new[] { example["expectedDisposition"]!.GetValue<string>(), example["expectedMeaning"]!.GetValue<string>() }.Concat(selectedRules.SelectMany(r => Strings(r, "requiredActions"))).ToArray();
        failures = selectedRules.SelectMany(r => Strings(r, "mustNotConclude")).ToArray();
    }
    Check(c.Focus == expectedFocus && c.MustHold.SequenceEqual(must) && c.FailureSignals.SequenceEqual(failures), "Worker expected meaning differs: " + c.Id);
    Check(c.SourceUrls.SequenceEqual(sourceUrls), "Case source selection differs: " + c.Id);
    var selected = (IReadOnlyList<string>)select.Invoke(service, [manifest, c.SourceUrls, new List<string>()])!;
    Check(selected.SequenceEqual(sourceUrls), "Production explicit selection differs");
    var bodies = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(bodyType))!;
    var nodes = new List<JsonNode>();
    foreach (var url in selected)
    {
        var task = (Task)fetch.Invoke(service, [client, url, CancellationToken.None])!;
        await task;
        var tuple = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var result = (PublicKnowledgeSourceResult)tuple.GetType().GetField("Item1")!.GetValue(tuple)!;
        var body = tuple.GetType().GetField("Item2")!.GetValue(tuple)!;
        Check(result.Ok && result.Note == "ok" && result.CharacterCount <= 17000, "Source truncated or rejected");
        // The originals are compact JSON plus final newline. NormalizeSourceText
        // trims that newline; all JSON characters must remain exactly intact.
        Check(Content(body) == Encoding.UTF8.GetString(map[url]).Trim(), "Source normalization lost material");
        var node = JsonNode.Parse(Content(body))!;
        Binding(node); nodes.Add(node); bodies.Add(body);
    }
    Check(nodes.Select(n => n["part"]!.GetValue<int>()).SequenceEqual(new[] { 1, 2, 3 }) && nodes.All(n => n["partCount"]!.GetValue<int>() == 3), "Part inventory differs");
    var command = new PublicKnowledgeRunCommand(false, false, c.Focus, c.SourceUrls, c.Id, c);
    var prompt = (string)build.Invoke(service, [manifest, command, bodies])!;
    var estimatedTokens = (int)estimate.Invoke(null, [prompt.Length])!;
    Check(prompt.Length <= limits.MaxInputCharacters && estimatedTokens <= limits.MaxEstimatedInputTokens && !prompt.Contains("prompt-snippet-truncated", StringComparison.Ordinal), "Prompt budget exceeded");
    Check(prompt.Contains("Focus: " + c.Focus, StringComparison.Ordinal), "Focus absent from actual prompt");
    foreach (var body in bodies.Cast<object>())
        Check(prompt.Contains(Content(body), StringComparison.Ordinal), "Complete body absent from actual prompt");
    // Validate only text that passed through actual Fetch/Normalize/BuildPrompt.
    foreach (var field in new[] { "claims", "sources", "contraryInterpretations", "rules", "regressions" })
    {
        var canonical = (matrix[field] ?? proposal[field])!.AsArray().Select(n => n!).OrderBy(n => n["id"]!.GetValue<string>()).ToArray();
        var actual = nodes.SelectMany(n => n[field]!.AsArray()).Select(n => n!).OrderBy(n => n["id"]!.GetValue<string>()).ToArray();
        Check(actual.Length == canonical.Length, "Record inventory differs: " + field);
        for (var i = 0; i < canonical.Length; i++)
            Check(JsonNode.DeepEquals(actual[i], canonical[i]), "Complete record differs: " + field + "/" + canonical[i]["id"]);
    }
    foreach (var (key, field) in new[] { ("matrix", "matrixContext"), ("proposal", "proposalContext") })
    {
        var contexts = nodes.SelectMany(n => n[field]!.AsArray()).ToArray();
        var expected = originals[key].DeepClone().AsObject();
        foreach (var f in new[] { "claims", "sources", "contraryInterpretations", "rules", "regressions" }) expected.Remove(f);
        Check(contexts.Length == 1 && JsonNode.DeepEquals(contexts[0], expected), "Complete context differs: " + key);
    }
    var sources = nodes.SelectMany(n => n["sources"]!.AsArray()).Select(n => n!["id"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
    foreach (var record in nodes.SelectMany(n => n["claims"]!.AsArray().Concat(n["contraryInterpretations"]!.AsArray())))
        Check(Strings(record!, "sourceIds").All(sources.Contains), "Dangling claim/source binding");
    reports.Add(new { c.Id, promptCharacters = prompt.Length, estimatedTokens, maximumSourceCharacters = bodies.Cast<object>().Max(b => Content(b).Length), completeResearch = true });
}
Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", assertions, cases = inputs.Cases.Count, claimsPerCase = 24, sourcesPerCase = 22, contraryInterpretationsPerCase = 4, rulesPerCase = 7, examplesPerCase = 22, researchBindings = bindings, productionHashes, baseline = new { note = baselineResult.Note, normalizedCharacters = baselineResult.CharacterCount, refreshQualificationsOmitted = true }, productionMethods = new[] { "ParseRegressionMatrix", "SelectSourceUrls", "FetchSourceAsync", "NormalizeSourceText (through FetchSourceAsync)", "BuildPrompt", "EstimateTokens" }, boundary = "Offline direct production-method input qualification only; not meaning approval, host qualification, live model-answer evaluation or publication availability.", results = reports }, new JsonSerializerOptions { WriteIndented = true }));

sealed class ForbiddenFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => throw new InvalidOperationException("External/provider client forbidden");
}
sealed class InMemorySources(IReadOnlyDictionary<string, byte[]> sources) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get || request.RequestUri is null || !sources.TryGetValue(request.RequestUri.AbsoluteUri, out var bytes))
            throw new InvalidOperationException("Unexpected request; no socket fallback");
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes), RequestMessage = request };
        response.Content.Headers.ContentType = new("application/json");
        return Task.FromResult(response);
    }
}
