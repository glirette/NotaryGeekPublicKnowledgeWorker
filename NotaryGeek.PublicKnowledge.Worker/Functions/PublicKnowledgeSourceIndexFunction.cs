using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using NotaryGeek.PublicKnowledge.Worker.Services;

namespace NotaryGeek.PublicKnowledge.Worker.Functions;

public sealed class PublicKnowledgeSourceIndexFunction
{
    private readonly PublicKnowledgeSourceIndexService _sourceIndexService;

    public PublicKnowledgeSourceIndexFunction(PublicKnowledgeSourceIndexService sourceIndexService)
    {
        _sourceIndexService = sourceIndexService;
    }

    [Function("PublicKnowledgeLawSourceIndex")]
    public async Task<HttpResponseData> LawSourceIndex(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "public-knowledge/source-index/law")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var state = TryGetStringQuery(req, "state");
        var summary = await _sourceIndexService.BuildLawSourceIndexSummaryAsync(state, cancellationToken);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            ok = true,
            summary
        }, cancellationToken);
        return response;
    }

    [Function("PublicKnowledgeLawSourceHealth")]
    public async Task<HttpResponseData> LawSourceHealth(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "public-knowledge/source-index/law/health")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var state = TryGetStringQuery(req, "state");
        var take = Math.Clamp(TryGetIntQuery(req, "take") ?? 25, 1, 100);
        var report = await _sourceIndexService.CheckLawSourceHealthAsync(state, take, cancellationToken);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            ok = report.FailedCount == 0,
            report
        }, cancellationToken);
        return response;
    }

    [Function("PublicKnowledgePublishedLawSourceCacheStatus")]
    public async Task<HttpResponseData> PublishedLawSourceCacheStatus(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "public-knowledge/source-index/law/cache-status")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var jurisdiction = TryGetStringQuery(req, "jurisdiction") ?? TryGetStringQuery(req, "state");
        var take = Math.Clamp(TryGetIntQuery(req, "take") ?? 50, 1, 250);
        var report = await _sourceIndexService.BuildPublishedLawSourceCacheStatusAsync(jurisdiction, take, cancellationToken);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            ok = report.Reachable && report.NeedsReviewCount == 0,
            report
        }, cancellationToken);
        return response;
    }

    private static string? TryGetStringQuery(HttpRequestData req, string name)
    {
        var prefix = $"{Uri.EscapeDataString(name)}=";
        foreach (var part in req.Url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Uri.UnescapeDataString(part[prefix.Length..]);
        }

        return null;
    }

    private static int? TryGetIntQuery(HttpRequestData req, string name)
    {
        var value = TryGetStringQuery(req, name);
        return int.TryParse(value, out var parsed) ? parsed : null;
    }
}
