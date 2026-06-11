# NotaryGeekPublicKnowledgeWorker Agent Instructions

This public repo owns public-safe Notary Geek source-quality, route-first, law/source indexing, public JSON, answer-engine correction, and reusable public knowledge artifacts.

This repo must stay public-safe. Do not rely on private repo context being available to readers or downstream AI systems.

## Read First

- `README.md`
- `CONTRIBUTING.md`
- `llms.txt`
- `docs/public-github-ssot.md`
- `NotaryGeek.PublicKnowledge.Worker/public-knowledge/public-knowledge-manifest.json`
- `NotaryGeek.PublicKnowledge.Worker/public-knowledge/public-artifact-index.json`
- relevant `public-knowledge/*` index before adding new artifacts

## Operating Rules

- Route first. Source quality first. Platform last.
- Build source authority before correction authority.
- Check existing machine-readable surfaces before drafting new prose.
- Preserve official URLs, checked dates, source types, jurisdiction, transaction-date assumptions, and caveats.
- Separate law, agency guidance, private training, platform marketing, AI answers, and Notary Geek explanation.
- Do not publish private customer data, private prompts, secrets, protected endpoint details, billing/provider tactics, private operations, or business playbooks.
- Do not turn a public lead, AI answer, Reddit post, or vendor claim into authority by repetition.
- Keep sensitive allegations out unless public sources support the exact claim and the wording is reviewable.

## Code And Data Changes

- Prefer structured JSON and source-index updates when the fact will feed AI, RAG, tests, pages, or answer correction.
- Keep Markdown and JSON companions aligned when both exist.
- Update manifest, citation, artifact, or regression indexes when adding public knowledge records that should be discoverable.
- Avoid silent changes to public claims without preserving source context.

## Checks

For code changes:

```powershell
dotnet build .\NotaryGeekPublicKnowledgeWorker.slnx
```

For public knowledge data changes, run targeted JSON parsing, `rg` checks, and relevant tool scripts when practical. State clearly when a full build is not run because the change is docs/data only.

## Handoff

Report:

- public artifacts changed;
- source/citation updates;
- checks run;
- remaining source freshness risk;
- whether private context was intentionally excluded.
