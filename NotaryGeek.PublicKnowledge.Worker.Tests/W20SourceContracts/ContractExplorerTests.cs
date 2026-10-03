using System.Text.Json;
using NotaryGeek.PublicKnowledge.Worker.Services;

namespace NotaryGeek.PublicKnowledge.Worker.Tests.W20SourceContracts;

public sealed class ContractExplorerTests
{
    [Fact]
    public async Task EnumeratedAndSeededRedirectContracts()
    {
        int count = 0;
        var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var recipe in Grammar.Enumerate().Concat(Grammar.Seeded(0x20C17E, 512)))
        {
            var expected = ContractOracle.Predict(recipe);
            var actual = await Observe(recipe);
            kinds[expected.Kind] = kinds.GetValueOrDefault(expected.Kind) + 1;
            if (!Equal(expected, actual))
            {
                var reduced = await Reducer.Steps(recipe, async r => !Equal(ContractOracle.Predict(r), await Observe(r)));
                throw new InvalidOperationException("redirect mismatch " + JsonSerializer.Serialize(new { recipe, expected, actual, reduced }));
            }
            count++;
        }
        Console.WriteLine($"W20 redirect recipes={count}; seed=0x20C17E; generated length=0..8; outcomes={JsonSerializer.Serialize(kinds)}");
    }

    internal static async Task<Observation> Observe(Recipe recipe)
    {
        using var fixture = new SyntheticSources();
        int step = 0;
        fixture.Respond = (_, _) =>
        {
            var current = step < recipe.Steps.Length ? recipe.Steps[step] : new Step(200, []); step++;
            return Task.FromResult(fixture.Response(current.Status, current.Locations));
        };
        using var client = fixture.Factory.CreateClient(nameof(PublicKnowledgeResearchService));
        string kind = "terminal"; string? final = null;
        try
        {
            var fetched = await AllowedSourceRedirects.SendAsync(client, new Uri(recipe.Start), ContractOracle.Hosts, CancellationToken.None);
            using var response = fetched.Response;
            final = fetched.FinalUri.AbsoluteUri;
            // No body read is implied by redirect resolution.
        }
        catch (SourceRedirectException e)
        {
            kind = e.Message.Contains("loop") ? "loop" : e.Message.Contains("limit") ? "limit" :
                e.Message.Contains("location") ? "location" : "policy";
        }
        Check.That(fixture.Bodies.All(b => b.Disposed && b.Reads == 0), "Redirect/terminal response leak or body consumed by redirect helper");
        return new(kind, fixture.Requests.ToArray(), final);
    }
    private static bool Equal(Observation a, Observation b) => a.Kind == b.Kind && a.Final == b.Final && a.Sent.SequenceEqual(b.Sent);

    [Fact]
    public void CitationCartesianProductHasIndependentLiteralOracle()
    {
        int count = 0;
        foreach (var a in Grammar.Resources)
        foreach (var b in Grammar.Resources)
        {
            bool expected = a.Identity == b.Identity;
            Check.That(ContractOracle.SameResource(a.Url, b.Url) == expected, $"Oracle/literal disagreement {a.Url} {b.Url}");
            Check.That(CitationAdmission.Structured(a.Url, b.Url) == expected, $"Structured identity mismatch {a.Url} {b.Url}");
            Check.That(CitationAdmission.Research(a.Url, b.Url) == expected, $"Research citation mismatch {a.Url} {b.Url}");
            count++;
        }
        Console.WriteLine($"W20 citation pairs={count}; two production admissions per pair; literal oracle labels={Grammar.Resources.Length}");
    }

    [Fact]
    public void ValidationGrammarAndConfiguredIdnPolicy()
    {
        var cases = new (string Text, bool Allowed)[]
        {
            ("https://SOURCE.example:443/doc",true), ("https://source.example:8443/doc",true),
            ("https://bücher.example/doc",true), ("https://xn--bcher-kva.example/doc",false),
            ("https://source.example/a/../doc",true), ("https://source.example/a%2Fb?q=%26",true),
            ("https://source.example/doc#part",false), ("https://u:p@source.example/doc",false),
            ("http://source.example/doc",false), ("https://other.example/doc",false),
            ("//source.example/doc",false), ("https://[",false), ("https://source.example/a b",false)
        };
        foreach (var c in cases)
        {
            Check.That(ContractOracle.Allows(c.Text) == c.Allowed, "Validation oracle/literal disagreement " + c.Text);
            Check.That(AllowedSourceRedirects.TryValidate(c.Text, ContractOracle.Hosts, out _, out _) == c.Allowed, "Validation mismatch " + c.Text);
        }
        Check.That(AllowedSourceRedirects.TryValidate("https://xn--bcher-kva.example/doc", "xn--bcher-kva.example", out _, out _), "Explicit punycode configuration lost");
        Console.WriteLine($"W20 validation cases={cases.Length + 1}; IDN allowlist spelling characterized, no new policy");
    }

    [Fact]
    public async Task DistinctSourcesAreNeitherDroppedNorReusedAcrossBatches()
    {
        var pairs = new[] {
            new[] { "https://source.example/L", "https://source.example/l" },
            new[] { "https://source.example/?E", "https://source.example/?e" },
            new[] { "https://source.example/LongDocument", "https://source.example/longDocument" },
            new[] { "https://source.example/doc?key=VALUE", "https://source.example/doc?key=value" }
        };
        var failures = new List<string>();
        foreach (var pair in pairs)
        foreach (bool batch in new[] { false, true })
        {
            var signature = await PairFailure(pair, batch);
            if (signature is not null)
            {
                var reduced = await Reducer.Pair(pair, async p => await PairFailure(p, batch) == signature);
                failures.Add(JsonSerializer.Serialize(new { mechanism = batch ? "batch-reuse" : "source-selection", signature, original = pair, reduced }));
            }
        }
        Check.That(failures.Count == 0, "Source identity counterexamples: " + string.Join("\n", failures));
    }
    internal static async Task<string?> PairFailure(string[] pair, bool batch)
    {
        using var fixture = new SyntheticSources();
        if (batch)
        {
            var results = await fixture.Research().RunBatchAsync(pair.Select(p => SyntheticSources.Command(p)).ToArray(), CancellationToken.None);
            if (results.Count == 2 && results[0].SourceCount == 1 && results[1].SourceCount == 1 &&
                results[1].Sources.Single().Url == pair[0] && results[1].Sources.Single().FinalUrl == new Uri(pair[0]).AbsoluteUri && fixture.Requests.Count == 1)
                return "second-command-reuses-first-source";
            Check.That(results.Count == 2 && !results.Where((r, i) => r.SourceCount != 1 || r.Sources[0].Url != pair[i] || r.Sources[0].FinalUrl != new Uri(pair[i]).AbsoluteUri).Any()
                && fixture.Requests.Count == 2, "Unexpected batch failure (not original reduction mechanism)");
            return null;
        }
        var result = await fixture.Research().RunAsync(SyntheticSources.Command(pair), CancellationToken.None);
        if (result.SourceCount == 1 && result.Sources.Count == 1 && result.Sources[0].Url == pair[0] && fixture.Requests.Count == 1)
            return "second-case-distinct-source-dropped";
        Check.That(result.SourceCount == 2 && result.Sources.Select(s => s.Url).SequenceEqual(pair) && fixture.Requests.Count == 2,
            "Unexpected selection failure (not original reduction mechanism)");
        return null;
    }

    [Fact]
    public async Task AuthorityAliasesDoNotDisplaceDistinctResourcesAtTheSourceCap()
    {
        using var f = new SyntheticSources();
        f.Options.MaxSourcesPerRun = 2;
        var result = await f.Research().RunAsync(SyntheticSources.Command(
            "https://SOURCE.example/doc", "https://source.example/doc", "https://source.example/other"), CancellationToken.None);
        Check.That(result.SourceCount == 2 && result.Sources.Select(s => s.FinalUrl).SequenceEqual(new[] {
            "https://source.example/doc", "https://source.example/other" }), "Authority aliases displaced a distinct source at the cap");
    }

    [Fact]
    public async Task DisallowedIdnSpellingCannotShadowAnAllowedSource()
    {
        using var f = new SyntheticSources();
        var r = await f.Research().RunAsync(SyntheticSources.Command(
            "https://xn--bcher-kva.example/doc", "https://bücher.example/doc"), CancellationToken.None);
        Check.That(r.Sources.Count == 2 && !r.Sources[0].Ok && r.Sources[1].Ok && r.SourceCount == 1,
            "Disallowed IDN spelling shadowed configured Unicode source");
        Check.That(f.Requests.Count == 1, "Disallowed IDN spelling sent");
    }

    [Fact]
    public async Task ReducerReplaysAndRetainsFailureMechanism()
    {
        var input = new Recipe(ContractOracle.Start, [new(302,["/a"]),new(307,["/b"]),new(308,["https://blocked.example/no"]),new(301,["/unused"])]);
        // Coverage control: reduce an actual policy refusal, without calling it a production bug.
        var reduced = await Reducer.Steps(input, async r => (await Observe(r)).Kind == "policy");
        Check.That(reduced.Steps.Length == 1 && reduced.Steps[0].Locations[0] == "https://blocked.example/no", "Reducer did not remove irrelevant steps");
        Check.That((await Observe(reduced with { Steps = [] })).Kind == "terminal", "Reducer minimality check failed");
        Console.WriteLine("W20 reducer control: 4 steps -> 1 policy-refusal step; deletion minimality replayed");
    }
}
