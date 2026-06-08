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
    private readonly PublicKnowledgeRunStorageService _storage;
    private readonly PublicKnowledgeOptions _options;
    private readonly ILogger<PublicKnowledgeResearchFunction> _logger;

    public PublicKnowledgeResearchFunction(
        PublicKnowledgeResearchService service,
        PublicKnowledgeRunStorageService storage,
        IOptions<PublicKnowledgeOptions> options,
        ILogger<PublicKnowledgeResearchFunction> logger)
    {
        _service = service;
        _storage = storage;
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
            status = _service.GetStatus(),
            storage = _storage.GetStatus()
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

    [Function("PublicKnowledgeLatestRuns")]
    public async Task<HttpResponseData> LatestRuns(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "public-knowledge/runs/latest")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        try
        {
            var caseId = TryGetStringQuery(req, "case");
            var response = req.CreateResponse(HttpStatusCode.OK);
            if (!string.IsNullOrWhiteSpace(caseId))
            {
                var latest = await _storage.ReadLatestAsync(caseId, cancellationToken);
                if (latest is null)
                {
                    var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFound.WriteAsJsonAsync(new
                    {
                        ok = false,
                        error = "latest_run_not_found",
                        requestedCase = caseId
                    }, cancellationToken);
                    return notFound;
                }

                await response.WriteAsJsonAsync(new
                {
                    ok = true,
                    latest
                }, cancellationToken);
                return response;
            }

            var latestRuns = await _storage.ListLatestAsync(cancellationToken);
            await response.WriteAsJsonAsync(new
            {
                ok = true,
                latestRuns
            }, cancellationToken);
            return response;
        }
        catch (InvalidOperationException ex)
        {
            var failed = req.CreateResponse(HttpStatusCode.BadRequest);
            await failed.WriteAsJsonAsync(new
            {
                ok = false,
                error = "storage_not_configured",
                message = ex.Message
            }, cancellationToken);
            return failed;
        }
    }

    [Function("PublicKnowledgeExportLatestIndex")]
    public async Task<HttpResponseData> ExportLatestIndex(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "public-knowledge/runs/export-index")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        try
        {
            var save = TryGetBoolQuery(req, "save") ?? true;
            var index = save
                ? await _storage.SaveLatestIndexAsync(cancellationToken)
                : await _storage.BuildLatestIndexAsync(cancellationToken);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                ok = true,
                saved = save,
                index
            }, cancellationToken);
            return response;
        }
        catch (InvalidOperationException ex)
        {
            var failed = req.CreateResponse(HttpStatusCode.BadRequest);
            await failed.WriteAsJsonAsync(new
            {
                ok = false,
                error = "storage_not_configured",
                message = ex.Message
            }, cancellationToken);
            return failed;
        }
    }

    [Function("PublicKnowledgeRunBatchNow")]
    public async Task<HttpResponseData> RunBatchNow(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "public-knowledge/runs/run-batch")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var batch = TryGetStringQuery(req, "batch") ?? _options.TimerBatch;
        var execute = TryGetBoolQuery(req, "execute") ?? true;
        var caseId = TryGetStringQuery(req, "case");

        IReadOnlyList<PublicKnowledgeRegressionCase> cases;
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

            batch = $"single:{regressionCase.Id}";
            cases = [regressionCase];
        }
        else
        {
            cases = _service.GetRegressionCasesForBatch(batch);
        }

        if (cases.Count == 0)
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new
            {
                ok = false,
                error = "empty_batch",
                requestedBatch = batch,
                availableBatches = _service.GetRegressionBatchNames(),
                availableCases = _service.GetRegressionMatrix().Cases.Select(item => item.Id)
            }, cancellationToken);
            return badRequest;
        }

        try
        {
            var receipts = await RunStoredBatchAsync(cases, batch, execute, "manual-batch", cancellationToken);
            var index = await _storage.SaveLatestIndexAsync(cancellationToken);
            var response = req.CreateResponse(receipts.All(item => item.Ok) ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
            await response.WriteAsJsonAsync(new
            {
                ok = receipts.All(item => item.Ok),
                execute,
                batch,
                caseCount = receipts.Count,
                receipts,
                latestIndex = new
                {
                    index.RunCount,
                    index.LatestIndexBlobName,
                    index.GeneratedAtUtc
                }
            }, cancellationToken);
            return response;
        }
        catch (InvalidOperationException ex)
        {
            var failed = req.CreateResponse(HttpStatusCode.BadRequest);
            await failed.WriteAsJsonAsync(new
            {
                ok = false,
                error = "storage_not_configured",
                message = ex.Message
            }, cancellationToken);
            return failed;
        }
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

        await RunConfiguredTimerBatchesAsync(_options.TimerBatches, _options.TimerBatch, "timer", cancellationToken);
    }

    [Function("PublicKnowledgePumpTimer")]
    public async Task RunPumpTimer(
        [TimerTrigger("0 7 */2 * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        if (!_options.PumpTimerEnabled)
        {
            _logger.LogInformation("Public knowledge pump timer skipped because PublicKnowledge__PumpTimerEnabled is false.");
            return;
        }

        await RunConfiguredTimerBatchesAsync(_options.PumpTimerBatches, "Core;Platform", "pump-timer", cancellationToken);
    }

    private async Task<IReadOnlyList<PublicKnowledgeStoredRunReceipt>> RunStoredBatchAsync(
        IReadOnlyList<PublicKnowledgeRegressionCase> cases,
        string batch,
        bool execute,
        string trigger,
        CancellationToken cancellationToken)
    {
        var runStartedUtc = DateTime.UtcNow;
        var commands = cases
            .Select(regressionCase => new PublicKnowledgeRunCommand(
                Execute: execute,
                FromTimer: trigger.Contains("timer", StringComparison.OrdinalIgnoreCase),
                Focus: regressionCase.Focus,
                RequestedUrls: [],
                RegressionCaseId: regressionCase.Id,
                RegressionCase: regressionCase))
            .ToArray();

        var results = await _service.RunBatchAsync(commands, cancellationToken);
        var receipts = new List<PublicKnowledgeStoredRunReceipt>();
        for (var index = 0; index < cases.Count; index++)
        {
            var regressionCase = cases[index];
            var result = results[index];
            var receipt = await _storage.SaveAsync(result, trigger, batch, runStartedUtc, cancellationToken);
            receipts.Add(receipt);
            _logger.LogInformation(
                "Public knowledge {Trigger} stored case {CaseId}: ok={Ok}; status={Status}; warnings={WarningCount}; errors={ErrorCount}; blob={BlobName}",
                trigger,
                receipt.CaseId,
                receipt.Ok,
                receipt.Status,
                receipt.WarningCount,
                receipt.ErrorCount,
                receipt.BlobName);

            if (!result.Ok)
            {
                _logger.LogWarning(
                    "Public knowledge research {Trigger} case {CaseId} failed with {ErrorCount} error(s): {Errors}",
                    trigger,
                    regressionCase.Id,
                    result.Errors.Count,
                    string.Join(" | ", result.Errors));
            }
        }

        return receipts;
    }

    private async Task RunConfiguredTimerBatchesAsync(
        string configuredBatches,
        string fallbackBatch,
        string trigger,
        CancellationToken cancellationToken)
    {
        var batches = SplitList(configuredBatches);
        if (batches.Count == 0)
        {
            batches = SplitList(fallbackBatch);
        }

        if (batches.Count == 0)
        {
            batches = ["Core"];
        }

        var totalReceipts = 0;
        foreach (var batch in batches)
        {
            var cases = _service.GetRegressionCasesForBatch(batch);
            if (cases.Count == 0)
            {
                _logger.LogWarning(
                    "Public knowledge {Trigger} found no cases for batch {Batch}. Available batches: {Batches}",
                    trigger,
                    batch,
                    string.Join(", ", _service.GetRegressionBatchNames()));
                continue;
            }

            var receipts = await RunStoredBatchAsync(cases, batch, execute: true, trigger, cancellationToken);
            totalReceipts += receipts.Count;
        }

        if (totalReceipts == 0)
        {
            _logger.LogWarning("Public knowledge {Trigger} completed with no stored receipts.", trigger);
            return;
        }

        var index = await _storage.SaveLatestIndexAsync(cancellationToken);
        _logger.LogInformation(
            "Public knowledge {Trigger} stored {ReceiptCount} receipt(s) and refreshed latest index with {RunCount} run(s): {BlobName}",
            trigger,
            totalReceipts,
            index.RunCount,
            index.LatestIndexBlobName);
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

    private static IReadOnlyList<string> SplitList(string value) =>
        value
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
