# Public Knowledge Run Queue

Reviewed: 2026-06-11

This repo uses Azure Storage Queue `public-knowledge-run-jobs` for public knowledge research/regression batches.

This is not the private Notary Geek workflow queue. It does not use the `notary-workflow-events` envelope and should not carry private customer/provider workflow payloads.

## Queue Contract

Queue messages serialize `PublicKnowledgeQueuedRunMessage` from `NotaryGeek.PublicKnowledge.Worker/Models/PublicKnowledgeModels.cs`.

Current fields:

```json
{
  "jobId": "20260611T000000Z-guid",
  "batch": "Core",
  "trigger": "manual-batch",
  "execute": true,
  "caseIds": ["case-id"],
  "submittedAtUtc": "2026-06-11T00:00:00Z",
  "caseId": "case-id",
  "providerOverride": null
}
```

The queue is for public-safe research runs, regression cases, source checks, and answer-quality checks. It should not include secrets, private prompts, customer records, private queue/blob payloads, protected endpoint details, billing/provider tactics, or private operations.

## Daily Public Source Ingestion

Public context:

```text
https://notary.cx/notary-geek-openai-public-data-sharing-case-study-press-release.html
```

The first review-gated daily source-ingestion slice is documented in:

```text
NotaryGeek.PublicKnowledge.Worker/public-knowledge/topics/daily-public-source-ingestion-contract.json
```

Use the Azure Functions timer as the daily scheduler. Configure:

```text
PublicKnowledge__Enabled=true
PublicKnowledge__TimerEnabled=true
PublicKnowledge__TimerBatches=DailySourceIngestion
PublicKnowledge__TimerProvider=OpenAI
PublicKnowledge__RequirePublicSourceOpenAiKey=true
OpenAI__PublicSourceApiKey=<the dedicated daily-reset public data-sharing key, stored in Azure app settings or Key Vault reference>
```

The existing `PublicKnowledgeResearchTimer` schedule is:

```text
0 17 9 * * *
```

Treat this as 09:17 UTC unless the Azure Functions host is explicitly configured with another timer time zone.

The GitHub Actions workflow `run-public-knowledge-batch.yml` remains available for manual/on-demand protected submits, defaulting to `DailySourceIngestion` with provider `OpenAI`. Do not add a second daily submitter unless duplicate OpenAI public-source runs are intentional.

Do not set this lane up with a private or generic provider key. When `PublicKnowledge__RequirePublicSourceOpenAiKey=true`, the worker fails closed if `OpenAI__PublicSourceApiKey` is missing instead of falling back to `OpenAI__ApiKey`.

The daily ingestion lane is intentionally PR-gated:

- Grok batch collection is for broad public-source discovery when latency up to 24 hours is acceptable.
- OpenAI normalization is for public-source records only, not final legal advice.
- Daily source-target selection, collection evidence, normalized source records, usage ledger, and promotion plan artifacts must exist before publication.
- No auto-merge is allowed until a later reviewed contract changes that rule.

## 2026-06-21 Live Verification

Deployment and app-setting verification completed against `ng-public-knowledge-func-2026` in resource group `NG-PUBLIC-KNOWLEDGE`.

Protected status endpoint confirmed:

```text
status.Enabled=true
status.TimerEnabled=true
status.TimerBatch=DailySourceIngestion
status.TimerBatches includes DailySourceIngestion
status.TimerProvider=OpenAI
status.Provider=OpenAI
status.HasPublicSourceOpenAiApiKey=true
status.RequirePublicSourceOpenAiKey=true
status.PumpTimerEnabled=true
```

Manual protected submit verified the daily batch before waiting for the next timer window:

```text
jobId=20260621T221301Z-2b706e89d50547bfbaa8e4d1fe627e36
batch=DailySourceIngestion
providerOverride=OpenAI
status=completed
completed=1/1
case=daily-public-source-ingestion-safety-gates
openAiCalled=true
provider=OpenAI
sourceCount=2
warnings=5
errors=0
scoreVerdict=needs-review
latestBlobName=runs/latest/daily-public-source-ingestion-safety-gates.json
```

## Runtime Settings

`host.json` explicitly sets:

```json
{
  "extensions": {
    "queues": {
      "batchSize": 4,
      "newBatchThreshold": 2,
      "maxPollingInterval": "00:00:05",
      "maxDequeueCount": 5
    }
  }
}
```

`maxDequeueCount` is set to `5` to make the retry/poison posture visible in source. That matches the normal Azure Functions queue-trigger default.

Expected poison queue name if infrastructure-level queue processing keeps failing:

```text
public-knowledge-run-jobs-poison
```

## Failure Model

`PublicKnowledgeQueuedBatchWorker` is intentionally different from the private workflow event processor:

- normal research/provider/regression failures are caught;
- the run state is updated through `PublicKnowledgeRunStorageService`;
- failed cases are visible through queued job status, latest run indexes, needs-Greg reports, and operator snapshot endpoints;
- the function does not rethrow normal case failures just to force queue poison handling.

That means a failed public knowledge case is not automatically a poison message. It is usually a completed queued job with failed or review-needed run state.

Infrastructure failures can still surface through Azure Functions retry/poison behavior if the worker cannot deserialize the queue message, cannot reach storage, or fails before it can record job state.

## Operator Checks

Start here:

```text
GET /api/public-knowledge/operator-snapshot?code=FUNCTION_KEY
```

Then check:

```text
GET /api/public-knowledge/runs/jobs?code=FUNCTION_KEY&take=20
GET /api/public-knowledge/runs/latest-digest?code=FUNCTION_KEY
GET /api/public-knowledge/runs/needs-greg?code=FUNCTION_KEY
```

If the operator snapshot reports stale jobs, inspect the queued job status:

```text
GET /api/public-knowledge/runs/jobs/{jobId}?code=FUNCTION_KEY
```

## Tomorrow Daily Run Verification

The next daily Azure timer run should submit `DailySourceIngestion` at the first `09:17 UTC` timer occurrence after deployment and app-setting updates.

Before the timer window, call the protected status endpoint and verify the booleans/settings only:

```text
GET /api/public-knowledge/status?code=FUNCTION_KEY
```

Expected non-secret fields:

```text
status.Enabled=true
status.TimerEnabled=true
status.TimerBatches includes DailySourceIngestion
status.TimerProvider=OpenAI
status.Provider=OpenAI
status.HasPublicSourceOpenAiApiKey=true
status.RequirePublicSourceOpenAiKey=true
storage.HasConnectionString=true
```

After `09:17 UTC`, check:

```text
GET /api/public-knowledge/operator-snapshot?code=FUNCTION_KEY&refresh=true
GET /api/public-knowledge/runs/jobs?code=FUNCTION_KEY&take=20
```

The recent job list should include a `timer` job with `batch=DailySourceIngestion`, `providerOverride=OpenAI`, and the case `daily-public-source-ingestion-safety-gates`. If the job is still running, wait for queue workers to finish and then inspect:

```text
GET /api/public-knowledge/runs/jobs/{jobId}?code=FUNCTION_KEY
GET /api/public-knowledge/runs/latest-digest?code=FUNCTION_KEY
GET /api/public-knowledge/runs/needs-greg?code=FUNCTION_KEY
```

Expected outcome is a completed public-source run or a review-needed failure with public-safe errors. Any failure mentioning `OpenAI__PublicSourceApiKey` means the public-source key setting was not deployed or resolved.

If Azure Functions reports repeated execution failures, inspect the storage account backing `AzureWebJobsStorage` for:

```text
public-knowledge-run-jobs
public-knowledge-run-jobs-poison
```

Do not paste queue bodies or stored run artifacts into non-public contexts without checking that they are public-safe. The repo is public, but job messages and run outputs may include provider responses or source excerpts that still need review before publication.

## Recovery Notes

Before replaying or requeueing a job:

- check whether the job already has terminal state in `runs/jobs/{jobId}.json`;
- check whether a latest run exists for each case under `runs/latest/`;
- use the operator snapshot and needs-Greg report before manually replaying;
- prefer re-submitting the specific regression case over replaying an old queue body when the input can be reconstructed from public repo data.

## Relationship To The Private Workflow Queue

`public-knowledge-run-jobs` is for public-source research work.

`notary-workflow-events` is the private operational workflow queue consumed by `glirette/Jotform-Stripe-Bridge`.

Do not merge these queue contracts. If public knowledge output needs to influence private operations, route that through a reviewed public artifact or a separate private work item rather than embedding private operational data in this public repo's queue messages.
