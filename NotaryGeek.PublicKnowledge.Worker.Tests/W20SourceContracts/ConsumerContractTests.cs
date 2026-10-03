using System.Net;
using NotaryGeek.PublicKnowledge.Worker.Services;

namespace NotaryGeek.PublicKnowledge.Worker.Tests.W20SourceContracts;

public sealed class ConsumerContractTests
{
    [Fact]
    public async Task ProductionRegistrationAndEveryConsumerPreserveProvenance()
    {
        using var f = new SyntheticSources();
        // Construction-only controls: no provider/default send can pass the synthetic transport.
        foreach (var name in new[] { "", "OpenAI", "Straico" }) using (f.Factory.CreateClient(name)) { }
        f.Options.PublicCorpusManifestUrl = "https://source.example/manifest";
        f.Respond = (r, _) => Task.FromResult(r.RequestUri!.AbsolutePath switch
        {
            "/manifest" => f.Response(302, ["//second.example/manifest-v2"]),
            "/manifest-v2" => f.Response(200, [], "{\"sourceSets\":[{\"urls\":[\"https://source.example/document\"]}]}"),
            "/document" => f.Response(307, ["https://second.example/Document?q=A"]),
            "/law" => f.Response(308, ["https://second.example/law-final"]),
            "/law-source-cache/source-cache-manifest.json" => f.Response(303, ["https://second.example/cache"]),
            "/cache" => f.Response(200, [], "{\"schema\":\"synthetic\",\"sources\":[],\"rules\":{\"refreshAfterDays\":14}}"),
            _ => f.Response(200, [], "synthetic text")
        });
        var result = await f.Research().RunAsync(SyntheticSources.Command(), CancellationToken.None);
        Check.That(!result.OpenAiCalled && result.SourceCount == 1, "Provider execution or missing source");
        Check.That(result.RemoteManifestUrl == "https://source.example/manifest" && result.FinalRemoteManifestUrl == "https://second.example/manifest-v2", "Manifest provenance");
        var source = result.Sources.Single();
        Check.That(source.Url == "https://source.example/document" && source.FinalUrl == "https://second.example/Document?q=A", "Research provenance");
        Check.That(CitationAdmission.Structured(source.FinalUrl!, source.FinalUrl!), "Fetched final citation rejected");
        Check.That(!CitationAdmission.Structured(source.FinalUrl!, source.Url), "Original redirect URL admitted as fetched content");
        var law = (await f.Index().CheckLawSourceHealthAsync("ZZ", 1, CancellationToken.None)).Checks.Single();
        Check.That(law.Ok && law.Url == "https://source.example/law" && law.FinalUrl == "https://second.example/law-final", "Law provenance");
        var cache = await f.Index().BuildPublishedLawSourceCacheStatusAsync(null, 1, CancellationToken.None);
        Check.That(cache.Reachable && cache.ManifestUrl == "https://source.example/law-source-cache/source-cache-manifest.json" && cache.FinalManifestUrl == "https://second.example/cache", "Cache provenance");
        Check.That(f.Names.Count(n => n == nameof(PublicKnowledgeResearchService)) == 4 &&
            f.Names.Count(n => n == nameof(PublicKnowledgeSourceIndexService)) == 4, "Named-client selection mismatch");
        Check.That(f.Constructed.Count == 5 && f.Constructed.Count(x => !x.AutoRedirect) == 2, "Production registration controls missing");
        Check.That(f.Bodies.All(b => b.Disposed), "Consumer response leaked");
    }

    [Fact]
    public async Task CancellationBeforeSendBetweenHopsAndDuringBodyThenRestart()
    {
        using var f = new SyntheticSources();
        using (var stop = new CancellationTokenSource())
        {
            stop.Cancel();
            await Check.Cancelled(() => f.Research().RunAsync(SyntheticSources.Command(ContractOracle.Start), stop.Token));
            Check.That(f.Requests.IsEmpty, "Pre-cancelled request sent");
        }
        using (var stop = new CancellationTokenSource())
        {
            f.Respond = (_, _) => { stop.Cancel(); return Task.FromResult(f.Response(302, ["/next"])); };
            await Check.Cancelled(() => f.Research().RunAsync(SyntheticSources.Command(ContractOracle.Start), stop.Token));
            Check.That(f.Requests.Count == 1 && f.Bodies.All(b => b.Disposed), "Between-hop cancellation leaked/sent extra request");
        }
        using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            f.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = f.Body("unused", "cancel", stop.Cancel) });
            await Check.Cancelled(() => f.Research().RunAsync(SyntheticSources.Command(ContractOracle.Start), stop.Token));
            Check.That(f.Bodies.All(b => b.Disposed), "Body cancellation leaked response");
        }
        int before = f.Requests.Count;
        f.Respond = (_, _) => Task.FromResult(f.Response(200, [], "restarted synthetic source"));
        var result = await f.Research().RunAsync(SyntheticSources.Command(ContractOracle.Start), CancellationToken.None);
        Check.That(result.SourceCount == 1 && result.Sources.Single().Ok && f.Requests.Count == before + 1,
            "Restart invented success or reused interrupted body");
    }

    [Fact]
    public async Task BodyFaultsAndTruncationCannotBecomeFetchedSources()
    {
        foreach (var fault in new[] { "throw", "truncate" })
        {
            using var f = new SyntheticSources();
            f.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = f.Body("partial synthetic", fault) });
            var result = await f.Research().RunAsync(SyntheticSources.Command(ContractOracle.Start), CancellationToken.None);
            Check.That(result.SourceCount == 0 && !result.Sources.Single().Ok, "Faulting body admitted as fetched");
            Check.That(f.Bodies.All(b => b.Disposed), "Faulting response leaked");
        }
    }

    [Fact]
    public async Task ConcurrentIndependentChainsDoNotShareVisitedState()
    {
        using var f = new SyntheticSources();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int initial = 0;
        f.Respond = async (r, token) =>
        {
            if (r.RequestUri!.AbsolutePath == "/start")
            {
                if (Interlocked.Increment(ref initial) == 16) gate.SetResult();
                await gate.Task.WaitAsync(token);
                return f.Response(302, ["/common"]);
            }
            return f.Response(200, [], "independent body");
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var tasks = Enumerable.Range(0, 16).Select(_ => f.Research().RunAsync(SyntheticSources.Command(ContractOracle.Start), timeout.Token));
        var results = await Task.WhenAll(tasks);
        Check.That(results.All(r => r.SourceCount == 1 && r.Sources.Single().FinalUrl == "https://source.example/common"), "Chain contamination");
        Check.That(f.Requests.Count == 32 && f.Bodies.All(b => b.Disposed), "Concurrent send/disposal count");
    }

    [Fact]
    public async Task ManifestAndIndexFaultsCancellationAndHeaderOnlyHealth()
    {
        foreach (var fault in new[] { "throw", "truncate" })
        {
            using var f = new SyntheticSources();
            f.Options.PublicCorpusManifestUrl = "https://source.example/manifest";
            f.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = f.Body("{", fault) });
            var research = await f.Research().RunAsync(SyntheticSources.Command(ContractOracle.Start), CancellationToken.None);
            Check.That(research.FinalRemoteManifestUrl is null && research.SourceCount == 0 && research.Warnings.Any(w => w.Contains("Remote manifest failed")), "Faulting manifest invented success");
            var cache = await f.Index().BuildPublishedLawSourceCacheStatusAsync(null, 1, CancellationToken.None);
            Check.That(!cache.Reachable && cache.FinalManifestUrl is null, "Faulting cache invented success");
            var law = (await f.Index().CheckLawSourceHealthAsync("ZZ", 1, CancellationToken.None)).Checks.Single();
            // Law health is a header-only reachability check, NOT body/citation admission.
            Check.That(law.Ok, "Header-only health unexpectedly consumed faulting body");
            Check.That(f.Bodies.All(b => b.Disposed) && f.Bodies.Count(b => b.Reads == 0) == 1, "Manifest/index fault disposal or header-only semantics");
        }
        foreach (var consumer in new[] { "research", "manifest", "law", "cache" })
        foreach (var phase in new[] { "send-fault", "send-cancel", "body-cancel" })
        {
            if (consumer == "law" && phase == "body-cancel") continue; // headers only
            using var f = new SyntheticSources();
            if (consumer == "manifest") f.Options.PublicCorpusManifestUrl = "https://source.example/manifest";
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            f.Respond = (_, token) =>
            {
                if (phase == "send-fault") throw new HttpRequestException("synthetic transport failure");
                if (phase == "send-cancel") { stop.Cancel(); token.ThrowIfCancellationRequested(); }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = f.Body("", "cancel", stop.Cancel) });
            };
            async Task Run()
            {
                if (consumer == "law")
                    Check.That(!(await f.Index().CheckLawSourceHealthAsync("ZZ", 1, stop.Token)).Checks.Single().Ok, "Failed law send became success");
                else if (consumer == "cache")
                    Check.That(!(await f.Index().BuildPublishedLawSourceCacheStatusAsync(null, 1, stop.Token)).Reachable, "Failed cache send became success");
                else
                {
                    var r = await f.Research().RunAsync(SyntheticSources.Command(ContractOracle.Start), stop.Token);
                    Check.That(r.SourceCount == 0 && r.FinalRemoteManifestUrl is null, "Failed research/manifest send became success");
                }
            }
            if (phase == "send-fault") await Run(); else await Check.Cancelled(Run);
            Check.That(f.Bodies.All(b => b.Disposed), "Cancellation leaked " + consumer + " " + phase);
        }
    }

    [Fact]
    public async Task PendingTransportCancellationDoesNotPublishAResult()
    {
        foreach (var consumer in new[] { "research", "manifest", "law", "cache" })
        {
            using var f = new SyntheticSources();
            if (consumer == "manifest") f.Options.PublicCorpusManifestUrl = "https://source.example/manifest";
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Respond = async (_, token) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                throw new InvalidOperationException("Pending send completed without cancellation");
            };
            Task pending = consumer switch
            {
                "law" => f.Index().CheckLawSourceHealthAsync("ZZ", 1, stop.Token),
                "cache" => f.Index().BuildPublishedLawSourceCacheStatusAsync(null, 1, stop.Token),
                _ => f.Research().RunAsync(SyntheticSources.Command(ContractOracle.Start), stop.Token)
            };
            await entered.Task.WaitAsync(stop.Token);
            Check.That(!pending.IsCompleted, "Send was not pending at cancellation boundary");
            stop.Cancel();
            await Check.Cancelled(() => pending);
            Check.That(f.Requests.Count == 1 && f.Bodies.IsEmpty, "Cancelled pending send invented a response");
        }
    }

    [Fact]
    public async Task WhitespaceLocationRejectedBeforeAnySecondSend()
    {
        using var f = new SyntheticSources();
        f.Respond = (_, _) => Task.FromResult(f.Response(301, ["   "]));
        var result = await f.Research().RunAsync(SyntheticSources.Command(ContractOracle.Start), CancellationToken.None);
        Check.That(result.SourceCount == 0 && !result.Sources.Single().Ok && f.Requests.Count == 1, "Whitespace Location escaped into second request");
        Check.That(f.Bodies.All(b => b.Disposed && b.Reads == 0), "Rejected redirect body consumed/leaked");
    }
}
