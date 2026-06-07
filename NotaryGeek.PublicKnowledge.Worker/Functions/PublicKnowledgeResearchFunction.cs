using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotaryGeek.PublicKnowledge.Worker.Configuration;
using NotaryGeek.PublicKnowledge.Worker.Models;
using NotaryGeek.PublicKnowledge.Worker.Services;

namespace NotaryGeek.PublicKnowledge.Worker.Functions;

public sealed class PublicKnowledgeResearchFunction
{
    private readonly PublicKnowledgeResearchService _service;
    private readonly PublicKnowledgeOptions _options;
    private readonly ILogger<PublicKnowledgeResearchFunction> _logger;

    public PublicKnowledgeResearchFunction(
        PublicKnowledgeResearchService service,
        IOptions<PublicKnowledgeOptions> options,
        ILogger<PublicKnowledgeResearchFunction> logger)
    {
        _service = service;
        _options = options.Value;
        _logger = logger;
    }

    [Function("PublicKnowledgeStatus")]
    public async Task<HttpResponseData> Status(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "public-knowledge/status")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            ok = true,
            status = _service.GetStatus()
        }, cancellationToken);
        return response;
    }

    [Function("PublicKnowledgeResearch")]
    public async Task<HttpResponseData> RunManual(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "public-knowledge/research")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var caseId = TryGetStringQuery(req, "case") ?? TryGetStringQuery(req, "profile");
        var focus = TryGetStringQuery(req, "focus") ?? "public notary, apostille, identity, platform, and source-quality research";
        PublicKnowledgeRegressionCase? selectedRegressionCase = null;

        if (!string.IsNullOrWhiteSpace(caseId))
        {
            if (!_service.TryGetRegressionCase(caseId, out var regressionCase) || regressionCase is null)
            {
                var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequest.WriteAsJsonAsync(new
                {
                    ok = false,
                    error = "unknown_regression_case",
                    requestedCase = caseId,
                    availableCases = _service.GetRegressionMatrix().Cases.Select(item => item.Id)
                }, cancellationToken);
                return badRequest;
            }

            selectedRegressionCase = regressionCase;
            focus = regressionCase.Focus;
        }

        var command = new PublicKnowledgeRunCommand(
            Execute: TryGetBoolQuery(req, "execute") ?? false,
            FromTimer: false,
            Focus: focus,
            RequestedUrls: GetRepeatedQuery(req, "url"),
            RegressionCaseId: selectedRegressionCase?.Id,
            RegressionCase: selectedRegressionCase);

        var result = await _service.RunAsync(command, cancellationToken);
        var response = req.CreateResponse(result.Ok ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(result, cancellationToken);
        return response;
    }

    [Function("PublicKnowledgeRegressionMatrix")]
    public async Task<HttpResponseData> RegressionMatrix(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "public-knowledge/regression-matrix")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            ok = true,
            matrix = _service.GetRegressionMatrix()
        }, cancellationToken);
        return response;
    }

    [Function("PublicKnowledgeResearchTimer")]
    public async Task RunTimer(
        [TimerTrigger("0 17 9 * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        if (!_options.TimerEnabled)
        {
            _logger.LogInformation("Public knowledge research timer skipped because PublicKnowledge__TimerEnabled is false.");
            return;
        }

        var command = new PublicKnowledgeRunCommand(
            Execute: true,
            FromTimer: true,
            Focus: "daily public law, answer-engine, source-quality, and route-first research triage",
            RequestedUrls: [],
            RegressionCaseId: null,
            RegressionCase: null);

        var result = await _service.RunAsync(command, cancellationToken);
        if (!result.Ok)
        {
            _logger.LogWarning(
                "Public knowledge research timer failed with {ErrorCount} error(s): {Errors}",
                result.Errors.Count,
                string.Join(" | ", result.Errors));
        }
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

    private static bool? TryGetBoolQuery(HttpRequestData req, string name)
    {
        var value = TryGetStringQuery(req, name);
        return bool.TryParse(value, out var parsed) ? parsed : null;
    }

    private static IReadOnlyList<string> GetRepeatedQuery(HttpRequestData req, string name)
    {
        var values = new List<string>();
        var prefix = $"{Uri.EscapeDataString(name)}=";
        foreach (var part in req.Url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            values.Add(Uri.UnescapeDataString(part[prefix.Length..]));
        }

        return values;
    }
}
