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
