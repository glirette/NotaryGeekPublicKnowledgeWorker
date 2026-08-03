using System.Net;
using System.Text.Json;
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
    private readonly PublicKnowledgeQueueService _queue;
    private readonly PublicKnowledgePromotionService _promotion;
    private readonly PublicKnowledgeOptions _options;
    private readonly ILogger<PublicKnowledgeResearchFunction> _logger;

    public PublicKnowledgeResearchFunction(
        PublicKnowledgeResearchService service,
        PublicKnowledgeRunStorageService storage,
        PublicKnowledgeQueueService queue,
        PublicKnowledgePromotionService promotion,
        IOptions<PublicKnowledgeOptions> options,
        ILogger<PublicKnowledgeResearchFunction> logger)
    {
        _service = service;
        _storage = storage;
        _queue = queue;
        _promotion = promotion;
        _options = options.Value;
        _logger = logger;
    }

    [Function("PublicKnowledgeStatus")]
    public async Task<HttpResponseData> Status(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "public-knowledge/status")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var promotion = await _promotion.GetStatusAsync(cancellationToken);
        var queue = await _queue.GetStatusAsync(cancellationToken);
        var backlog = await _storage.GetBacklogStatusAsync(cancellationToken);
        var providerHealth = await _storage.GetProviderHealthAsync(cancellationToken);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            ok = true,
            status = _service.GetStatus(),
            storage = _storage.GetStatus(),
            promotion,
            queue,
            backlog,
            providerHealth
        }, cancellationToken);
        return response;
    }

    [Function("PublicKnowledgePromotionFeed")]
    public async Task<HttpResponseData> PromotionFeed(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "public-knowledge/promotion-feed")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var destination = TryGetStringQuery(req, "destination")
            ?? PublicKnowledgePromotionService.GetDestination("notary");
        try
        {
            var feed = await _promotion.ReadFeedAsync(destination, cancellationToken);
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Cache-Control", "public, max-age=300");
            await response.WriteAsJsonAsync(feed, cancellationToken);
            return response;
        }
        catch (ArgumentException ex)
        {
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteAsJsonAsync(new { ok = false, error = "unknown_destination", message = ex.Message }, cancellationToken);
            return response;
        }
    }

    [Function("PublicKnowledgePromotionAck")]
    public async Task<HttpResponseData> PromotionAck(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "public-knowledge/promotion-ack")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        PublicAuthorityPromotionReceipt? receipt;
        try
        {
            receipt = await req.ReadFromJsonAsync<PublicAuthorityPromotionReceipt>(cancellationToken);
        }
        catch (JsonException)
        {
            receipt = null;
        }
        if (receipt is null)
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { ok = false, error = "invalid_promotion_receipt" }, cancellationToken);
            return badRequest;
        }

        try
        {
            await _promotion.RecordPromotionAsync(receipt, cancellationToken);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { ok = true, receipt.CandidateId, receipt.Destination, receipt.PromotedAtUtc }, cancellationToken);
            return response;
        }
        catch (ArgumentException ex)
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { ok = false, error = "invalid_promotion_receipt", message = ex.Message }, cancellationToken);
            return badRequest;
        }
    }

    [Function("PublicKnowledgePublicationAck")]
    public async Task<HttpResponseData> PublicationAck(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "public-knowledge/publication-ack")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        PublicAuthorityPublicationReceipt? receipt;
        try
        {
            receipt = await req.ReadFromJsonAsync<PublicAuthorityPublicationReceipt>(cancellationToken);
        }
        catch (JsonException)
        {
            receipt = null;
        }
        if (receipt is null)
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { ok = false, error = "invalid_publication_receipt" }, cancellationToken);
            return badRequest;
        }

        try
        {
            await _promotion.RecordPublicationAsync(receipt, cancellationToken);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new { ok = true, receipt.CandidateId, receipt.Destination, receipt.PublishedAtUtc }, cancellationToken);
            return response;
        }
        catch (ArgumentException ex)
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            await badRequest.WriteAsJsonAsync(new { ok = false, error = "invalid_publication_receipt", message = ex.Message }, cancellationToken);
            return badRequest;
        }
    }

    [Function("PublicKnowledgeResearch")]
    public async Task<HttpResponseData> RunManual(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "public-knowledge/research")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var caseId = TryGetStringQuery(req, "case") ?? TryGetStringQuery(req, "profile");
        var focus = TryGetStringQuery(req, "focus") ?? "public notary, apostille, identity, platform, and source-quality research";
        var providerOverride = NormalizeProviderOverride(TryGetStringQuery(req, "provider"));
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
            RequestedUrls: MergeRequestedUrls(GetRepeatedQuery(req, "url"), selectedRegressionCase?.SourceUrls),
            RegressionCaseId: selectedRegressionCase?.Id,
            RegressionCase: selectedRegressionCase,
            ProviderOverride: providerOverride);

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

    [Function("PublicKnowledgeNeedsGreg")]
    public async Task<HttpResponseData> NeedsGreg(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "public-knowledge/runs/needs-greg")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        try
        {
            var save = TryGetBoolQuery(req, "save") ?? false;
            var report = save
                ? await _storage.SaveNeedsGregReportAsync(cancellationToken)
                : await _storage.BuildNeedsGregReportAsync(cancellationToken);
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                ok = true,
                saved = save,
                report
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

    [Function("PublicKnowledgeLatestDigest")]
    public async Task<HttpResponseData> LatestDigest(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "public-knowledge/runs/latest-digest")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        try
        {
            var refresh = TryGetBoolQuery(req, "refresh") ?? false;
            PublicKnowledgeNeedsGregReport? report = null;
            if (!refresh)
            {
                report = await _storage.ReadSavedNeedsGregReportAsync(cancellationToken);
            }

            var refreshed = refresh || report is null;
            report ??= await _storage.SaveNeedsGregReportAsync(cancellationToken);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                ok = true,
                refreshed,
                digest = BuildDigestSummary(report),
                report
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

    [Function("PublicKnowledgeOperatorSnapshot")]
    public async Task<HttpResponseData> OperatorSnapshot(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "public-knowledge/operator-snapshot")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        try
        {
            var refresh = TryGetBoolQuery(req, "refresh") ?? false;
            var take = Math.Clamp(TryGetIntQuery(req, "take") ?? 10, 1, 50);
            PublicKnowledgeNeedsGregReport? report = null;
            if (!refresh)
            {
                report = await _storage.ReadSavedNeedsGregReportAsync(cancellationToken);
            }

            var refreshed = refresh || report is null;
            report ??= await _storage.SaveNeedsGregReportAsync(cancellationToken);
            var jobs = await _storage.ListQueuedRunsAsync(take, status: null, cancellationToken);
            var promotion = await _promotion.GetStatusAsync(cancellationToken);
            var queue = await _queue.GetStatusAsync(cancellationToken);
            var backlog = await _storage.GetBacklogStatusAsync(cancellationToken);
            var providerHealth = await _storage.GetProviderHealthAsync(cancellationToken);
            var staleJobs = jobs.Where(item => item.IsStale).ToArray();
            var runningJobs = jobs.Where(item => !item.IsTerminal).ToArray();
            var nextActions = BuildOperatorSnapshotNextActions(report, staleJobs);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                ok = true,
                generatedAtUtc = DateTime.UtcNow,
                refreshed,
                healthy = report.Healthy && staleJobs.Length == 0,
                attentionCount = report.Items.Count + staleJobs.Length,
                status = _service.GetStatus(),
                storage = _storage.GetStatus(),
                promotion,
                queue,
                backlog,
                providerHealth,
                digest = BuildDigestSummary(report),
                runningJobCount = runningJobs.Length,
                staleJobCount = staleJobs.Length,
                recentJobs = jobs,
                topReviewItems = report.Items.Take(10).ToArray(),
                operatorNextActions = nextActions
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
        var providerOverride = NormalizeProviderOverride(TryGetStringQuery(req, "provider"));

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

        var sync = TryGetBoolQuery(req, "sync") ?? cases.Count == 1;
        if (!sync)
        {
            var message = await SubmitQueuedCasesAsync(cases, batch, execute, "manual-batch", providerOverride, cancellationToken);
            var accepted = req.CreateResponse(HttpStatusCode.Accepted);
            await accepted.WriteAsJsonAsync(new
            {
                ok = true,
                status = "queued",
                execute,
                batch,
                provider = providerOverride,
                caseCount = cases.Count,
                cases = cases.Select(item => item.Id),
                jobId = message.JobId,
                statusPath = $"/api/public-knowledge/runs/jobs/{Uri.EscapeDataString(message.JobId)}",
                note = "Multi-case batches run through the async queue by default. Use sync=true only for small one-off diagnostics."
            }, cancellationToken);
            return accepted;
        }

        try
        {
            var receipts = await RunStoredBatchAsync(cases, batch, execute, "manual-batch", providerOverride, cancellationToken);
            var index = await _storage.SaveLatestIndexAsync(cancellationToken);
            var digest = await _storage.SaveNeedsGregReportAsync(cancellationToken);
            var response = req.CreateResponse(receipts.All(item => item.Ok) ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
            await response.WriteAsJsonAsync(new
            {
                ok = receipts.All(item => item.Ok),
                execute,
                batch,
                provider = providerOverride,
                caseCount = receipts.Count,
                receipts,
                latestIndex = new
                {
                    index.RunCount,
                    index.LatestIndexBlobName,
                    index.GeneratedAtUtc
                },
                latestDigest = BuildDigestSummary(digest)
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

    [Function("PublicKnowledgeSubmitBatch")]
    public async Task<HttpResponseData> SubmitBatch(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "public-knowledge/runs/submit-batch")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var batch = TryGetStringQuery(req, "batch") ?? _options.TimerBatch;
        var execute = TryGetBoolQuery(req, "execute") ?? true;
        var caseId = TryGetStringQuery(req, "case");
        var providerOverride = NormalizeProviderOverride(TryGetStringQuery(req, "provider"));

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

        var message = await SubmitQueuedCasesAsync(cases, batch, execute, "queued-batch", providerOverride, cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(new
        {
            ok = true,
            status = "queued",
            jobId = message.JobId,
            batch,
            provider = providerOverride,
            execute,
            caseCount = cases.Count,
            cases = cases.Select(item => item.Id),
            statusPath = $"/api/public-knowledge/runs/jobs/{Uri.EscapeDataString(message.JobId)}"
        }, cancellationToken);
        return response;
    }

    [Function("PublicKnowledgeQueuedJobs")]
    public async Task<HttpResponseData> QueuedJobs(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "public-knowledge/runs/jobs")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        try
        {
            var take = Math.Clamp(TryGetIntQuery(req, "take") ?? 20, 1, 100);
            var status = TryGetStringQuery(req, "status");
            var jobs = await _storage.ListQueuedRunsAsync(take, status, cancellationToken);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                ok = true,
                take,
                status,
                jobCount = jobs.Count,
                staleJobCount = jobs.Count(item => item.IsStale),
                jobs
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

    [Function("PublicKnowledgeQueuedJobStatus")]
    public async Task<HttpResponseData> QueuedJobStatus(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "public-knowledge/runs/jobs/{jobId}")] HttpRequestData req,
        string jobId,
        CancellationToken cancellationToken)
    {
        var job = await _storage.ReadQueuedRunAsync(jobId, cancellationToken);
        if (job is null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new
            {
                ok = false,
                error = "queued_job_not_found",
                jobId
            }, cancellationToken);
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            ok = true,
            job
        }, cancellationToken);
        return response;
    }

    [Function("PublicKnowledgeQueuedBatchWorker")]
    public async Task ProcessQueuedBatch(
        [QueueTrigger("public-knowledge-run-jobs", Connection = "AzureWebJobsStorage")] PublicKnowledgeQueuedRunMessage message,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Public knowledge queued job {JobId} started for batch {Batch}; case={CaseId}; listed cases={CaseCount}.",
            message.JobId,
            message.Batch,
            message.CaseId,
            message.CaseIds.Count);

        try
        {
            await _storage.MarkQueuedRunRunningAsync(message, cancellationToken);

            if (string.IsNullOrWhiteSpace(message.CaseId) && message.CaseIds.Count > 1)
            {
                foreach (var childCaseId in message.CaseIds.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    await _queue.EnqueueAsync(message with { CaseId = childCaseId }, cancellationToken);
                }

                _logger.LogInformation(
                    "Public knowledge queued job {JobId} was a legacy batch message; fanned out {CaseCount} per-case message(s).",
                    message.JobId,
                    message.CaseIds.Count);
                return;
            }

            var caseId = string.IsNullOrWhiteSpace(message.CaseId)
                ? message.CaseIds.FirstOrDefault()
                : message.CaseId;

            if (string.IsNullOrWhiteSpace(caseId) ||
                !_service.TryGetRegressionCase(caseId, out var regressionCase) ||
                regressionCase is null)
            {
                await _storage.FailQueuedRunCaseAsync(message, $"No valid regression case was found for queued case '{caseId}'.", cancellationToken);
                return;
            }

            var receipts = await RunStoredBatchAsync([regressionCase], message.Batch, message.Execute, message.Trigger, message.ProviderOverride, cancellationToken);
            await _storage.CompleteQueuedRunAsync(message, receipts, cancellationToken);
            var index = await _storage.SaveLatestIndexAsync(cancellationToken);
            var digest = await _storage.SaveNeedsGregReportAsync(cancellationToken);
            _logger.LogInformation(
                "Public knowledge queued job {JobId} stored case {CaseId}; latest index has {RunCount} run(s); digest healthy={Healthy}; reviewCount={ReviewCount}.",
                message.JobId,
                caseId,
                index.RunCount,
                digest.Healthy,
                digest.Items.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Public knowledge queued job {JobId} failed.", message.JobId);
            await _storage.FailQueuedRunCaseAsync(message, ex.Message, cancellationToken);
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

        await RunConfiguredTimerBatchesAsync(
            _options.TimerBatches,
            _options.TimerBatch,
            "timer",
            _options.TimerProvider,
            cancellationToken);
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

        var index = await _storage.SaveLatestIndexAsync(cancellationToken);
        var digest = await _storage.SaveNeedsGregReportAsync(cancellationToken);
        var promotion = await _promotion.GetStatusAsync(cancellationToken);
        _logger.LogInformation(
            "Public knowledge pump refreshed indexes without provider calls: latest={RunCount}; attention={AttentionCount}; candidates={CandidateCount}.",
            index.RunCount,
            digest.Items.Count,
            promotion.CandidateCount);
    }

    private async Task<IReadOnlyList<PublicKnowledgeStoredRunReceipt>> RunStoredBatchAsync(
        IReadOnlyList<PublicKnowledgeRegressionCase> cases,
        string batch,
        bool execute,
        string trigger,
        string? providerOverride,
        CancellationToken cancellationToken)
    {
        var runStartedUtc = DateTime.UtcNow;
        var runKind = GetRunKind(batch);
        var effectiveExecute = PublicKnowledgeExecutionPolicy.ShouldCallProvider(trigger, runKind, execute);
        var commands = cases
            .Select(regressionCase => new PublicKnowledgeRunCommand(
                Execute: effectiveExecute,
                FromTimer: trigger.Contains("timer", StringComparison.OrdinalIgnoreCase),
                Focus: regressionCase.Focus,
                RequestedUrls: regressionCase.SourceUrls,
                RegressionCaseId: regressionCase.Id,
                RegressionCase: regressionCase,
                ProviderOverride: providerOverride,
                RunKind: runKind,
                AuthorityLane: GetAuthorityLane(batch)))
            .ToArray();

        var results = await _service.RunBatchAsync(commands, cancellationToken);
        var receipts = new List<PublicKnowledgeStoredRunReceipt>();
        for (var index = 0; index < cases.Count; index++)
        {
            var regressionCase = cases[index];
            var result = results[index];
            var receipt = await _storage.SaveAsync(result, trigger, batch, runStartedUtc, cancellationToken);
            receipts.Add(receipt);
            if (result.Ok && result.RunKind.Equals("authority-generation", StringComparison.OrdinalIgnoreCase))
            {
                var savedCandidates = await _promotion.SaveValidatedCandidatesAsync(
                    result,
                    $"{runStartedUtc:yyyyMMddTHHmmssZ}-{regressionCase.Id}",
                    cancellationToken);
                _logger.LogInformation(
                    "Public knowledge authority run {CaseId} saved {CandidateCount} new sanitized promotion candidate(s).",
                    regressionCase.Id,
                    savedCandidates);
            }
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
        string provider,
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

        var submittedJobs = 0;
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

            if (IsAuthorityBatch(batch))
            {
                var freshCases = new List<PublicKnowledgeRegressionCase>();
                foreach (var regressionCase in cases)
                {
                    var hasFreshRun = await _storage.HasFreshSuccessfulRunAsync(
                        regressionCase.Id,
                        TimeSpan.FromHours(Math.Max(1, _options.AuthorityFreshnessHours)),
                        cancellationToken);
                    if (!hasFreshRun)
                    {
                        freshCases.Add(regressionCase);
                    }
                }

                cases = freshCases;
                if (cases.Count == 0)
                {
                    _logger.LogInformation(
                        "Public knowledge {Trigger} skipped authority batch {Batch}; all selection targets have fresh successful outputs.",
                        trigger,
                        batch);
                    continue;
                }

                if (trigger.Equals("timer", StringComparison.OrdinalIgnoreCase) &&
                    !await _storage.TryAcquireDailySelectionAsync(batch, cancellationToken))
                {
                    _logger.LogInformation(
                        "Public knowledge {Trigger} skipped authority batch {Batch}; today's idempotent selection was already acquired.",
                        trigger,
                        batch);
                    continue;
                }
            }

            var providerOverride = NormalizeProviderOverride(provider);
            var message = await SubmitQueuedCasesAsync(cases, batch, execute: true, trigger, providerOverride, cancellationToken);
            submittedJobs++;
            _logger.LogInformation(
                "Public knowledge {Trigger} queued batch {Batch} as job {JobId} with {CaseCount} case(s); provider={Provider}.",
                trigger,
                batch,
                message.JobId,
                cases.Count,
                providerOverride ?? "Default");
        }

        if (submittedJobs == 0)
        {
            _logger.LogWarning("Public knowledge {Trigger} completed with no queued jobs.", trigger);
            return;
        }

        _logger.LogInformation(
            "Public knowledge {Trigger} queued {JobCount} job(s). Workers will refresh the latest index as cases complete.",
            trigger,
            submittedJobs);
    }

    private async Task<PublicKnowledgeQueuedRunMessage> SubmitQueuedCasesAsync(
        IReadOnlyList<PublicKnowledgeRegressionCase> cases,
        string batch,
        bool execute,
        string trigger,
        string? providerOverride,
        CancellationToken cancellationToken)
    {
        var submittedAtUtc = DateTime.UtcNow;
        var jobId = $"{submittedAtUtc:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}";
        var parent = new PublicKnowledgeQueuedRunMessage(
            jobId,
            batch,
            trigger,
            execute,
            cases.Select(item => item.Id).ToArray(),
            submittedAtUtc,
            ProviderOverride: providerOverride,
            RunKind: GetRunKind(batch),
            AuthorityLane: GetAuthorityLane(batch));

        await _storage.CreateQueuedRunAsync(parent, cancellationToken);
        foreach (var regressionCase in cases)
        {
            await _queue.EnqueueAsync(parent with { CaseId = regressionCase.Id }, cancellationToken);
        }

        return parent;
    }

    private static bool IsAuthorityBatch(string batch) =>
        batch.Equals("DailySourceIngestion", StringComparison.OrdinalIgnoreCase) ||
        batch.Equals("TechnicalSourceIngestion", StringComparison.OrdinalIgnoreCase);

    private static string GetRunKind(string batch) => IsAuthorityBatch(batch) ? "authority-generation" : "regression";

    private static string GetAuthorityLane(string batch) =>
        batch.Equals("TechnicalSourceIngestion", StringComparison.OrdinalIgnoreCase) ? "technical" : "notary";

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

    private static string? NormalizeProviderOverride(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider) ||
            provider.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return provider.Trim();
    }

    private static bool? TryGetBoolQuery(HttpRequestData req, string name)
    {
        var value = TryGetStringQuery(req, name);
        return bool.TryParse(value, out var parsed) ? parsed : null;
    }

    private static int? TryGetIntQuery(HttpRequestData req, string name)
    {
        var value = TryGetStringQuery(req, name);
        return int.TryParse(value, out var parsed) ? parsed : null;
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

    private static IReadOnlyList<string> MergeRequestedUrls(
        IReadOnlyList<string> requestUrls,
        IReadOnlyList<string>? caseUrls)
    {
        if (caseUrls is null || caseUrls.Count == 0)
        {
            return requestUrls;
        }

        if (requestUrls.Count == 0)
        {
            return caseUrls;
        }

        return requestUrls
            .Concat(caseUrls)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> SplitList(string value) =>
        value
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static object BuildDigestSummary(PublicKnowledgeNeedsGregReport report) =>
        new
        {
            report.GeneratedAtUtc,
            report.Healthy,
            report.Summary,
            report.RunCount,
            report.PassingCount,
            report.NeedsReviewCount,
            report.FailCount,
            report.NotScoredCount,
            report.WarningRunCount,
            report.ErrorRunCount,
            report.HighestPriority,
            report.LatestReportBlobName,
            OperatorNextActions = report.OperatorNextActions ?? Array.Empty<string>()
        };

    private static IReadOnlyList<string> BuildOperatorSnapshotNextActions(
        PublicKnowledgeNeedsGregReport report,
        IReadOnlyList<PublicKnowledgeQueuedRunSummary> staleJobs)
    {
        var actions = new List<string>();
        foreach (var staleJob in staleJobs.Take(5))
        {
            actions.Add($"Check stale queued job {staleJob.JobId}: status={staleJob.Status}, completed={staleJob.CompletedCount}/{staleJob.TotalCount}, ageMinutes={staleJob.ActiveAgeMinutes}.");
        }

        actions.AddRange(report.OperatorNextActions ?? Array.Empty<string>());
        return actions.Count == 0
            ? ["No operator action needed from the latest public knowledge snapshot."]
            : actions.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
    }
}
