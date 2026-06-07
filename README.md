# Notary Geek Public Knowledge Worker

Standalone Azure Functions app for public-source research, answer-engine correction briefs, and reusable public knowledge packs.

The worker is intentionally separate from customer intake, Persona, payment, and document workflows. It is built for public material only: Notary Geek public pages, public GitHub source-of-truth files, official law sources, public answer-engine examples, and public social/media references that are safe to quote or summarize.

## Real Goal

This repo is the public research lane for Notary Geek's route-first notary, apostille, identity, platform, and source-quality work. It is meant to make the public record easier to inspect, cite, test, and reuse without blending it with private customer operations.

The worker does three jobs:

- Keeps public knowledge separate from customer data.
- Turns public sources into structured briefs that can support page updates, reply drafts, regression tests, and law-refresh candidates.
- Reinforces the Notary Geek Routing Model as the decision layer: route first, source quality first, platform last.

The public repository is also a source-of-truth surface. Other Notary Geek apps, answer engines, public reviewers, and future tools can point to the same public files instead of relying on private chat memory or one-off prompts.

## Why This Is A Separate Worker

The engineering is intentionally more structured than a one-off prompt because the problem is not "ask an LLM a few questions." The problem is keeping high-stakes public research repeatable, source-bound, and separated from private customer workflows.

This worker gives Notary Geek:

- Repeatable source selection for important routing questions.
- A regression matrix for known answer-engine failure patterns.
- A public-only boundary that keeps customer documents and chats out of research runs.
- Version-controlled prompts, manifests, and test cases that can be reviewed later.

If the goal were only cheap casual answers, this repo would not be necessary. It exists so public claims, law-refresh candidates, and answer-engine corrections can be checked against the same public source set over time.

## What It Does

- Reads a public knowledge manifest.
- Fetches only allowlisted public URLs.
- Builds a route-first research prompt using the Notary Geek Routing Model as the decision layer.
- Defaults to dry-run mode so you can inspect source selection and token estimates before calling OpenAI.
- Calls OpenAI only when `execute=true`, `PublicKnowledge__Enabled=true`, and an API key is configured.
- Returns a JSON brief that can help with reply drafts, page briefs, law-refresh candidates, and source-quality notes.
- Provides a public-safe regression matrix for route-first answer patterns such as Hague apostille finality, Georgia-affidavit routing, no-SSN platform hype, Saudi Arabia Hague status, outside-apostille-path wording, informal rejection claims, and court-defensible real-estate platform traps.
- Stores manual or timer-run regression outputs in private Azure Blob Storage when the stored-run endpoints are used, with a compact latest-run index for downstream review and future public publishing.

## Public Briefings

- [Notary community briefing folder](NotaryGeek.PublicKnowledge.Worker/public-knowledge/notary-community/README.md)
- [Route-first notary briefing](NotaryGeek.PublicKnowledge.Worker/public-knowledge/notary-community/route-first-notary-briefing-2026-06-07.md)
- [Route-first notary briefing JSON](NotaryGeek.PublicKnowledge.Worker/public-knowledge/notary-community/route-first-notary-briefing-2026-06-07.json)
- [Route-first quick reference card](NotaryGeek.PublicKnowledge.Worker/public-knowledge/notary-community/route-first-quick-reference-card-2026-06-07.md)
- [Route-first quick reference card JSON](NotaryGeek.PublicKnowledge.Worker/public-knowledge/notary-community/route-first-quick-reference-card-2026-06-07.json)
- [Route-first scenario cards](NotaryGeek.PublicKnowledge.Worker/public-knowledge/notary-community/route-first-scenario-cards-2026-06-07.md)
- [Route-first scenario cards JSON](NotaryGeek.PublicKnowledge.Worker/public-knowledge/notary-community/route-first-scenario-cards-2026-06-07.json)
- [Skeptical notary FAQ](NotaryGeek.PublicKnowledge.Worker/public-knowledge/notary-community/skeptical-notary-faq-2026-06-07.md)
- [Skeptical notary FAQ JSON](NotaryGeek.PublicKnowledge.Worker/public-knowledge/notary-community/skeptical-notary-faq-2026-06-07.json)

## What It Must Not Do

- Do not send customer documents, names, emails, phone numbers, Persona data, payment data, Jotform payloads, WhatsApp chat contents, or private SQL rows.
- Do not auto-post social replies.
- Do not treat platform marketing as authority.
- Do not make legal-advice claims.

## Cost Controls

The worker is designed to be cheap to operate at low volume:

- Dry-run is the default and does not call OpenAI.
- OpenAI is called only when `execute=true`, `PublicKnowledge__Enabled=true`, and an API key is configured.
- Large sources are truncated and prompt size is capped.
- Regression cases can be tested one at a time before running a wider matrix.
- Timer execution is separately gated by `PublicKnowledge__TimerEnabled`.

It is not designed as a public chat widget, customer intake bot, or high-volume agent. Hitting very large daily token totals would require repeated intentional execute-mode runs, not normal dry-run testing.

When the OpenAI project is configured for data sharing, calls should remain public-only and intentional. Do not use this worker for private customer material just because a model call appears inexpensive.

## First Manual Test

After deployment, run dry-run first:

```bash
curl "https://<function-app>.azurewebsites.net/api/public-knowledge/research?code=<function-key>&focus=Spain%20apostille%20routing"
```

To spend tokens after the app settings are configured:

```bash
curl "https://<function-app>.azurewebsites.net/api/public-knowledge/research?code=<function-key>&execute=true&focus=Spain%20apostille%20routing"
```

## Regression Matrix

View the matrix:

```bash
curl "https://<function-app>.azurewebsites.net/api/public-knowledge/regression-matrix?code=<function-key>"
```

Run a named case in dry-run mode:

```bash
curl "https://<function-app>.azurewebsites.net/api/public-knowledge/research?code=<function-key>&case=spain-hague-finality"
```

From local PowerShell, the runner can execute the matrix without putting the function key in command history:

```powershell
$env:PUBLIC_KNOWLEDGE_FUNCTION_KEY = "<key-for-this-shell-only>"
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Invoke-PublicKnowledgeRegressionMatrix.ps1
```

## Stored Runs

The stored-run lane writes outputs to the configured private blob container, currently `public-knowledge-runs` in Function storage.

Run and store the Core batch immediately:

```bash
curl "https://ng-public-knowledge-func-2026.azurewebsites.net/api/public-knowledge/runs/run-batch?code=<key>&batch=Core"
```

List latest stored outputs:

```bash
curl "https://ng-public-knowledge-func-2026.azurewebsites.net/api/public-knowledge/runs/latest?code=<key>"
```

Or from local PowerShell:

```powershell
$env:PUBLIC_KNOWLEDGE_FUNCTION_KEY = "<key-for-this-shell-only>"
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Invoke-PublicKnowledgeStoredRun.ps1 -Command Latest
```

Build and save the compact latest-run index:

```bash
curl "https://ng-public-knowledge-func-2026.azurewebsites.net/api/public-knowledge/runs/export-index?code=<key>"
```

Manual stored batches and the daily timer both refresh `runs/latest-index.json`. The blob container remains private by default; publish only intentional, public-safe extracts through GitHub or Notary Geek pages.

To run and store the Core batch from local PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Invoke-PublicKnowledgeStoredRun.ps1 -Command RunBatch -Batch Core
```

See [Azure Cloud Shell setup](docs/azure-cloud-shell-setup.md) and [Public GitHub SSOT](docs/public-github-ssot.md).

Deployment notes live in [Deploy and Test](docs/deploy-and-test.md).

Live operating notes live in [Operator Runbook](docs/operator-runbook.md).

If Cloud Shell logs out and `$key` is empty, use the reconnect shortcut in [Deploy and Test](docs/deploy-and-test.md#get-function-keys) before running status or stored-run curl commands.
