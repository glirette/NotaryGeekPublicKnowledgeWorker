# Public Knowledge Run Queue

Reviewed: 2026-08-03

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
  "providerOverride": null,
  "runKind": "regression",
  "authorityLane": "notary"
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
PublicKnowledge__TimerEnabled=true
PublicKnowledge__TimerBatches=DailySourceIngestion
PublicKnowledge__TimerProvider=OpenAI
OpenAI__PublicSourceApiKey=<the dedicated public-source project key, stored in Azure app settings or Key Vault reference>
OpenAI__AuthorityReasoningEffort=minimal
OpenAI__AuthorityMaxOutputTokens=6000
OpenAI__RepairMaxOutputTokens=8000
OpenAI__MaxProviderAttempts=2
```

The authority lane unconditionally requires `OpenAI__PublicSourceApiKey`. It never falls back to generic `OpenAI__ApiKey`; the legacy `PublicKnowledge__RequirePublicSourceOpenAiKey` switch is not part of the repaired source contract.

An API response does not prove incentive treatment or final request cost. Protected run evidence must leave both unknown unless independently verified.

The existing `PublicKnowledgeResearchTimer` schedule is:

```text
0 17 9 * * *
```

Treat this as 09:17 UTC unless the Azure Functions host is explicitly configured with another timer time zone.

The GitHub Actions workflow `run-public-knowledge-batch.yml` remains available for manual/on-demand protected submits, defaulting to `DailySourceIngestion` with provider `OpenAI`. Do not add a second daily submitter unless duplicate OpenAI public-source runs are intentional.

The daily ingestion lanes are intentionally PR-gated:

- `DailySourceIngestion` creates Notary public-authority candidates; `TechnicalSourceIngestion` creates reusable technical source-trail candidates.
- Strict Responses API Structured Outputs and local validation must both pass. Incomplete, empty, invalid, stale, or unfetched-citation output is failed even after HTTP 200.
- Atomic daily selection, fresh-success suppression, source freshness, and deterministic candidate IDs prevent repeat fixed-case spend and duplicate promotion.
- The two-hour pump makes no provider calls. It refreshes health/index artifacts and refuses provider execution from legacy pump envelopes.
- Raw provider output remains private. Only the sanitized typed candidate feed is eligible for destination promotion.
- Destination repositories revalidate candidates and own source-scoped branches and draft pull requests.
- A protected promotion receipt suppresses a candidate from later feeds as soon as its draft PR exists; a separate protected publication receipt is recorded only after that same-repository PR merges to `main`.
- Destination workflows must preflight `can_approve_pull_request_reviews` and fail closed when their job-scoped `GITHUB_TOKEN` cannot create pull requests.
- No auto-merge is allowed until a later reviewed contract changes that rule.

See [Public Authority Machine](public-authority-machine.md) for the operating and health contract.

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
