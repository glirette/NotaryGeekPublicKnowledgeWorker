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

var root = Path.GetFullPath(args.Length == 0 ? "." : args[0]);
var corpus = Path.Combine(root, "NotaryGeek.PublicKnowledge.Worker/public-knowledge");
var packageDir = Path.Combine(corpus, "evidence/notarycam-worker-2026-10-02");
var auditBytes = File.ReadAllBytes(Path.Combine(corpus, "evidence/notarycam-virginia-claims-2026-10-02.json"));
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var auditText = Encoding.UTF8.GetString(auditBytes);
var audit = JsonNode.Parse(auditText)!;
var manifest = JsonSerializer.Deserialize<PublicKnowledgeManifest>(File.ReadAllText(Path.Combine(corpus, "public-knowledge-manifest.json")), jsonOptions)!;
var matrix = JsonSerializer.Deserialize<PublicKnowledgeRegressionMatrix>(File.ReadAllText(Path.Combine(corpus, "public-knowledge-regression-matrix.json")), jsonOptions)!;
var cases = matrix.Cases.Where(c => c.Id.StartsWith("notarycam-audit-part-", StringComparison.Ordinal)).ToArray();
var package = JsonNode.Parse(File.ReadAllText(Path.Combine(packageDir, "package.json")))!;
var knowledgeOptions = new PublicKnowledgeOptions();
var service = new PublicKnowledgeResearchService(new ForbiddenFactory(), Options.Create(knowledgeOptions), Options.Create(new OpenAiOptions()), Options.Create(new StraicoOptions()), NullLogger<PublicKnowledgeResearchService>.Instance);
var type = typeof(PublicKnowledgeResearchService);
var select = type.GetMethod("SelectSourceUrls", BindingFlags.NonPublic | BindingFlags.Instance)!;
var fetch = type.GetMethod("FetchSourceAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
var promptMethod = type.GetMethod("BuildPrompt", BindingFlags.NonPublic | BindingFlags.Instance)!;
var sourceBodyType = type.GetNestedType("SourceBody", BindingFlags.NonPublic)!;
var assertions = 0;
void Check(bool ok, string message) { assertions++; if (!ok) throw new InvalidOperationException(message); }
Check(auditBytes.Length == 167039 && Convert.ToHexStringLower(SHA256.HashData(auditBytes)) == "84021763580edfcd5c0bcb8560dfbc3e0e4b421c4b9bebc6d6c02c00b5ff14ba", "Canonical bytes changed");
Check(knowledgeOptions.MaxSourcesPerRun == 24 && knowledgeOptions.MaxCharactersPerSource == 20000 && knowledgeOptions.MaxInputCharacters == 60000, "Reassess package against changed production defaults");
var allUrls = manifest.SourceSets.SelectMany(s => s.Urls).Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
var auditPositions = allUrls.Select((url, index) => new { url, position = index + 1 }).Where(x => x.url.Contains("notarycam-virginia", StringComparison.Ordinal)).ToArray();
var defaultSelected = (IReadOnlyList<string>)select.Invoke(service, [manifest, Array.Empty<string>(), new List<string>()])!;
Check(defaultSelected.Count == 24 && auditPositions.All(x => !defaultSelected.Contains(x.url)), "Baseline default audit exclusion changed");
var urls = new Dictionary<string, byte[]>(StringComparer.Ordinal);
var pin = package["canonicalSnapshot"]!.GetValue<string>();
urls.Add(pin, auditBytes);
foreach (var c in cases)
    foreach (var url in c.SourceUrls)
        urls[url] = File.ReadAllBytes(Path.Combine(packageDir, new Uri(url).Segments[^1]));
using var handler = new InMemorySources(urls);
using var client = new HttpClient(handler);
async Task<(PublicKnowledgeSourceResult Result, object Body)> Fetch(string url)
{
    var task = (Task)fetch.Invoke(service, [client, url, CancellationToken.None])!;
    await task;
    var tuple = task.GetType().GetProperty("Result")!.GetValue(task)!;
    return ((PublicKnowledgeSourceResult)tuple.GetType().GetField("Item1")!.GetValue(tuple)!, tuple.GetType().GetField("Item2")!.GetValue(tuple)!);
}
string BodyText(object body) => (string)sourceBodyType.GetProperty("Content")!.GetValue(body)!;
var baseline = await Fetch(pin);
Check(baseline.Result.Ok && baseline.Result.Note.StartsWith("ok-truncated:", StringComparison.Ordinal), "Original source truncation not reproduced");
Check(!BodyText(baseline.Body).Contains("\"claims\":", StringComparison.Ordinal) && !BodyText(baseline.Body).Contains("\"interpretationRules\":", StringComparison.Ordinal), "Original omitted sections not reproduced");
var seenClaims = new HashSet<string>(StringComparer.Ordinal);
var seenCounterarguments = new HashSet<string>(StringComparer.Ordinal);
var reports = new List<object>();
var originalClaims = audit["claims"]!.AsArray().ToDictionary(c => c!["id"]!.GetValue<string>(), c => c!);
var originalSources = audit["sources"]!.AsArray().ToDictionary(c => c!["id"]!.GetValue<string>(), c => c!);
var originalArguments = audit["counterarguments"]!.AsArray().ToDictionary(c => c!["id"]!.GetValue<string>(), c => c!);
foreach (var c in cases)
{
    var selected = (IReadOnlyList<string>)select.Invoke(service, [manifest, c.SourceUrls, new List<string>()])!;
    Check(selected.SequenceEqual(c.SourceUrls) && selected.Count == 3, $"Explicit selection failed: {c.Id}");
    var bodies = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(sourceBodyType))!;
    var nodes = new List<JsonNode>();
    foreach (var url in selected)
    {
        var result = await Fetch(url);
        Check(result.Result.Ok && result.Result.Note == "ok", $"Source truncated: {url}");
        bodies.Add(result.Body);
        nodes.Add(JsonNode.Parse(BodyText(result.Body))!);
    }
    var command = new PublicKnowledgeRunCommand(false, false, c.Focus, c.SourceUrls, c.Id, c);
    var prompt = (string)promptMethod.Invoke(service, [manifest, command, bodies])!;
    Check(prompt.Length <= knowledgeOptions.MaxInputCharacters && !prompt.Contains("prompt-snippet-truncated", StringComparison.Ordinal), $"Prompt budget exceeded: {c.Id}");
    foreach (var body in bodies)
        Check(prompt.Contains(BodyText(body!), StringComparison.Ordinal), $"Body missing from actual prompt: {c.Id}");
    var guards = nodes.Single(n => n["kind"]!.GetValue<string>() == "shared-safeguards");
    Check(JsonNode.DeepEquals(guards["interpretationRules"], audit["interpretationRules"]), $"Rules changed: {c.Id}");
    Check(JsonNode.DeepEquals(guards["limitations"], audit["limitations"]), $"Limitations changed: {c.Id}");
    var recordParts = nodes.Where(n => n != guards).ToArray();
    var partClaims = recordParts.SelectMany(n => n["claims"]!.AsArray()).Select(n => n!).ToArray();
    var partArguments = recordParts.SelectMany(n => n["counterarguments"]!.AsArray()).Select(n => n!).ToArray();
    var partSources = recordParts.SelectMany(n => n["sources"]!.AsArray()).Select(n => n!).ToDictionary(n => n["id"]!.GetValue<string>());
    var expectedIds = package["cases"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == c.Id)!["claimIds"]!.AsArray().Select(n => n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
    Check(expectedIds.SetEquals(partClaims.Select(n => n["id"]!.GetValue<string>())), $"Case claim set differs: {c.Id}");
    foreach (var claim in partClaims)
    {
        var id = claim["id"]!.GetValue<string>();
        Check(seenClaims.Add(id), $"Duplicate claim: {id}");
        Check(JsonNode.DeepEquals(claim, originalClaims[id]), $"Claim was rewritten: {id}");
    }
    foreach (var argument in partArguments)
    {
        var id = argument["id"]!.GetValue<string>();
        Check(JsonNode.DeepEquals(argument, originalArguments[id]), $"Counterargument was rewritten: {id}");
        seenCounterarguments.Add(id);
    }
    foreach (var source in partSources)
        Check(JsonNode.DeepEquals(source.Value, originalSources[source.Key]), $"Source changed: {source.Key}");
    foreach (var record in partClaims.Concat(partArguments).Concat(guards["interpretationRules"]!["rules"]!.AsArray().Select(n => n!)))
        foreach (var id in record["sourceIds"]!.AsArray().Select(n => n!.GetValue<string>()))
            Check(partSources.ContainsKey(id), $"Dangling source {id} in actual prompt: {c.Id}");
    reports.Add(new { c.Id, claimIds = expectedIds.Order().ToArray(), sources = selected.Count, promptCharacters = prompt.Length, maximumSourceCharacters = bodies.Cast<object>().Max(b => BodyText(b).Length), allBodiesComplete = true });
}
Check(seenClaims.SetEquals(originalClaims.Keys), "Missing audit claims");
Check(seenCounterarguments.SetEquals(originalArguments.Keys), "Missing counterargument");
var report = new { status = "passed", assertions, cases = cases.Length, claims = seenClaims.Count, counterarguments = seenCounterarguments.Count, interpretationRulesPerCase = 15, canonicalSha256 = Convert.ToHexStringLower(SHA256.HashData(auditBytes)), baseline = new { selectedSources = defaultSelected.Count, auditPositions, snapshotCharacters = auditText.Length, claimsStart = auditText.IndexOf("\"claims\"", StringComparison.Ordinal), interpretationRulesStart = auditText.IndexOf("\"interpretationRules\"", StringComparison.Ordinal), normalizedCharacters = baseline.Result.CharacterCount, note = baseline.Result.Note }, productionMethodCoverage = new[] { "SelectSourceUrls", "FetchSourceAsync", "NormalizeSourceText (through FetchSourceAsync)", "BuildPrompt" }, transport = "Synthetic in-memory HttpMessageHandler only; no DNS, sockets, source network or provider execution", runtimeScope = "Direct production-method harness, not Azure Functions host or full solution build", results = reports };
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

sealed class ForbiddenFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => throw new InvalidOperationException("No provider or external client may be created by this harness.");
}
sealed class InMemorySources(IReadOnlyDictionary<string, byte[]> sources) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get || request.RequestUri is null || !sources.TryGetValue(request.RequestUri.AbsoluteUri, out var body))
            throw new InvalidOperationException("Unexpected source request; no network fallback exists.");
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body), RequestMessage = request };
        response.Content.Headers.ContentType = new("application/json");
        return Task.FromResult(response);
    }
}
