# Public Knowledge Worker Operator Runbook

Last reviewed: 2026-06-07

This runbook is for operating the live Azure Function without mixing it with customer data or private workflow systems.

## Operating Boundary

Use this worker only for public-safe material:

- public Notary Geek pages and JSON files
- public GitHub files in this repo
- official law/source pages
- public answer-engine examples
- public vendor help pages used only as provider-claim evidence
- public-safe voice-note/source-archive summaries after private details are removed

Do not send customer names, phone numbers, email addresses, documents, Persona data, payment data, Jotform payloads, WhatsApp chats, private SQL rows, labels, or private correspondence through this worker.

## Current Live App

- Resource group: `NG-PUBLIC-KNOWLEDGE`
- Function app: `ng-public-knowledge-func-2026`
- Host: `https://ng-public-knowledge-func-2026.azurewebsites.net`
- Stored-run container: `public-knowledge-runs`
- Default model: `gpt-5-mini`
- Default timer batch: `Core`
- Timer schedule in code: daily at `09:17 UTC`

## Daily Posture

When the worker is lit up, the intended daily path is:

1. Timer runs the configured regression batch.
2. Each case fetches allowlisted public sources.
3. OpenAI is called only because the timer and public-knowledge gates are enabled.
4. Each result is stored under a dated blob path.
5. `runs/latest/<case>.json` and `runs/latest-index.json` provide a compact review surface.

The daily timer should stay on only when the OpenAI project is configured for public input/output sharing and the source set remains public-safe.

## Safety Switches

Hard pause scheduled calls:

```bash
az functionapp config appsettings set \
  --resource-group NG-PUBLIC-KNOWLEDGE \
  --name ng-public-knowledge-func-2026 \
  --settings PublicKnowledge__TimerEnabled=false AzureWebJobs.PublicKnowledgeResearchTimer.Disabled=true
```

Enable scheduled `Core` runs:

```bash
az functionapp config appsettings set \
  --resource-group NG-PUBLIC-KNOWLEDGE \
  --name ng-public-knowledge-func-2026 \
  --settings PublicKnowledge__TimerEnabled=true AzureWebJobs.PublicKnowledgeResearchTimer.Disabled=false PublicKnowledge__TimerBatch=Core PublicKnowledge__OutputStorageConnectionStringSetting=AzureWebJobsStorage PublicKnowledge__OutputContainerName=public-knowledge-runs
```

Restart after changing timer settings:

```bash
az functionapp restart \
  --resource-group NG-PUBLIC-KNOWLEDGE \
  --name ng-public-knowledge-func-2026
```

## Quick Status

If Cloud Shell lost `$key`, reload it:

```bash
key=$(az functionapp keys list \
  --resource-group NG-PUBLIC-KNOWLEDGE \
  --name ng-public-knowledge-func-2026 \
  --query "functionKeys.default" \
  -o tsv)

if [ -z "$key" ]; then
  key=$(az functionapp keys list \
    --resource-group NG-PUBLIC-KNOWLEDGE \
    --name ng-public-knowledge-func-2026 \
    --query "masterKey" \
    -o tsv)
fi
```

Check live status:

```bash
curl "https://ng-public-knowledge-func-2026.azurewebsites.net/api/public-knowledge/status?code=$key"
```

Confirm the important switches:

- `Enabled` should be true before execute-mode calls.
- `TimerEnabled` should match the intended live posture.
- `HasOpenAiApiKey` should be true for execute-mode calls.
- `HasConnectionString` should be true before stored runs.
- `AllowedSourceHosts` should include every official/vendor source needed by the current manifest.

## Manual Stored Runs

Run and store the Core batch immediately:

```bash
curl "https://ng-public-knowledge-func-2026.azurewebsites.net/api/public-knowledge/runs/run-batch?code=$key&batch=Core"
```

Run one case:

```bash
curl "https://ng-public-knowledge-func-2026.azurewebsites.net/api/public-knowledge/runs/run-batch?code=$key&case=spain-hague-finality"
```

List latest outputs:

```bash
curl "https://ng-public-knowledge-func-2026.azurewebsites.net/api/public-knowledge/runs/latest?code=$key"
```

Export the compact latest-run index:

```bash
curl "https://ng-public-knowledge-func-2026.azurewebsites.net/api/public-knowledge/runs/export-index?code=$key"
```

## Review Checklist

For each latest run, look for:

- `Ok=true`
- `Status=completed`
- `OpenAiCalled=true` when execute was intended
- warning count near zero
- no citation warnings for fetched source URLs
- no platform-first recommendation drift
- no stale Hague/non-Hague country examples
- no vague recipient-acceptance escape hatch
- no private or customer-specific facts

If a run is useful, convert it into one of these public outputs:

- public-safe source archive note
- topic JSON update
- answer-engine correction case
- routing-model page update
- GitHub issue or PR note

Keep the blob container private by default. Publish only intentional extracts.

## Expanding The Worker

Prefer adding durable source files before adding new code:

1. Add public-safe topic or correction JSON.
2. Add it to `public-knowledge-manifest.json`.
3. Add or update a regression case.
4. Dry-run the case.
5. Execute one case.
6. Run the relevant batch.
7. Deploy only when the changed behavior requires code or manifest publication.

Add new source hosts only when the source is needed and public. Provider pages are allowed as workflow or claim evidence, not as legal authority.

## Cost Note

The worker may show zero spend when the OpenAI project qualifies for complimentary daily shared-data tokens. Treat that as helpful but not guaranteed. Keep the same public-only and intentional-run posture even when usage appears free.
