# Public Authority Machine

Reviewed: 2026-08-03

This repository is the reviewed source for the Azure Function public-authority worker. A deployment made from local-only or closed-PR code is drift and must not be treated as reproducible production state.

## Reconciled Source Boundary

The daily ingestion code and dedicated public-source key boundary that were previously present only in a local deployment and closed pull requests are now represented in source. Authority calls always require `OpenAI__PublicSourceApiKey`; `OpenAI__ApiKey` is never a fallback, regardless of legacy deployed settings.

This repair does not deploy, change Azure settings, merge a pull request, or delete stored data. A reviewed deployment remains required before the live Function has this behavior.

## Execution Lanes

`PublicKnowledgeResearchTimer` is the daily authority scheduler. Its 09:17 UTC timer submits configured authority batches after atomic daily selection and freshness checks.

- `DailySourceIngestion` routes notary, apostille, authentication, and public-law candidates to `glirette/NotaryGeekPublicKnowledgeWorker`.
- `TechnicalSourceIngestion` routes reusable public-safe technical/API/cloud/platform source trails to `glirette/thisstuffiswaytootech`.

Regression batches are explicit diagnostics. The two-hour `PublicKnowledgePumpTimer` performs no provider calls; it refreshes indexes, digest, and health metrics. The execution policy also neutralizes legacy queued messages whose trigger is `pump-timer`.

## Usable Provider Output

The OpenAI Responses request uses strict Structured Outputs through `text.format` with JSON Schema. Authority runs use minimal reasoning by default, a 6,000-token output budget, and at most one bounded repair attempt with an 8,000-token ceiling.

HTTP 200 is transport evidence only. The worker marks output usable only when all of these hold:

- response status is `completed`;
- visible output is non-empty JSON;
- the response matches the strict schema and local validation;
- citations and candidate sources are exact fetched HTTPS URLs;
- reviewed dates satisfy the configured freshness window;
- at least one candidate exists with public-safe required fields.

`incomplete`, empty, invalid JSON, schema mismatch, stale sources, and unfetched citations produce an actionable failure reason and `Ok=false`. Input, output, and reasoning-token counts remain separate in durable provider evidence.

Current API behavior is based on the official [Structured Outputs guide](https://developers.openai.com/api/docs/guides/structured-outputs), [reasoning guide](https://developers.openai.com/api/docs/guides/reasoning), and [GPT-5 mini model page](https://developers.openai.com/api/docs/models/gpt-5-mini).

## Incremental State And Promotion

Daily selection is atomic per UTC date and batch. A successful validated result remains fresh for `PublicKnowledge__AuthorityFreshnessHours`; source review remains fresh for `PublicKnowledge__SourceFreshnessDays`. Candidate IDs are deterministic SHA-256 values, and candidate storage uses create-if-absent semantics.

Private stored runs may contain provider output and remain private. Only typed, locally validated candidate fields are copied into the public promotion store. The anonymous feed never includes raw provider output, private Blob URLs, credentials, prompts, or unknown fields:

```text
GET /api/public-knowledge/promotion-feed?destination=glirette/NotaryGeekPublicKnowledgeWorker
GET /api/public-knowledge/promotion-feed?destination=glirette/thisstuffiswaytootech
```

The technical feed schema is `technical-source-candidate-feed/v1`. `recheckBeforeUse` is a boolean and tells the destination to re-check current documentation before acting. The exact fields are in the [machine-readable contract](../NotaryGeek.PublicKnowledge.Worker/public-knowledge/topics/daily-public-source-ingestion-contract.json).

Destination repositories own branches and draft pull requests using their job-scoped `GITHUB_TOKEN`; this Function owns no GitHub PAT or App secret. Each promotion job must validate the feed again, reject unknown fields, and preflight the repository Actions setting `can_approve_pull_request_reviews`. If that setting is false, PR creation must fail closed. Changing that protected repository setting is outside this repair.

After a draft PR is created, the destination sends a function-key-protected promotion receipt to `POST /api/public-knowledge/promotion-ack` with exactly `candidateId`, `destination`, `promotedAtUtc`, and `pullRequestUrl`. Promoted candidate IDs are excluded from later feeds, preventing scheduled duplicate PRs. A draft PR is not a publication.

Only the destination's trusted default-branch `pull_request: closed` workflow sends a publication receipt after a same-repository authority PR is merged to `main`. It does not check out or execute PR code. It reads the merged candidate files through the GitHub API and posts exactly `candidateId`, `destination`, `publishedAtUtc`, and `pullRequestUrl` to the function-key-protected `POST /api/public-knowledge/publication-ack`. Publication is accepted only when the candidate exists and an earlier promotion receipt has the same exact canonical destination and destination-repository PR URL.

Standing issues such as `glirette/BootStrapCritical#138` are status boards, not provider-result queues or fake patch targets. Promotion occurs through source-scoped branches and draft pull requests, never repetitive result comments.

## Health And Retention

The protected status and operator snapshot return:

- configured provider, dedicated-key presence, model, reasoning and output limits;
- approximate live queue messages plus exact job-envelope count; new envelopes carry status metadata for active/completed/failed/stale counts, while pre-repair envelopes are reported as unknown legacy status without rewriting them;
- latest-run provider/auth-mode/model sets and aggregate input/output/reasoning usage;
- last usable output and recent actionable provider failure reasons;
- candidate, promoted, and published counts plus separate last-candidate, last-successful-promotion, and last-successful-publication timestamps;
- configured retention windows.

Retention is policy-only in this repair: 90 days for run history and 30 days for job envelopes by default. No deletion or compaction runs. A future reviewed implementation must preserve latest indexes, selection state, promotion candidates, publication acknowledgements, provider evidence, and actionable failures before removing history.

## Safe Verification

Run locally:

```powershell
dotnet test .\NotaryGeekPublicKnowledgeWorker.slnx
```

After review and a separately authorized deployment, verify the protected status and operator snapshot, then perform an explicit dry run before an executing authority batch. Do not paste function keys, connection strings, queue bodies, or raw stored runs into public issues or pull requests.
