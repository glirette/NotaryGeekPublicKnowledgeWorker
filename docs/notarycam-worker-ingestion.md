# Bounded NotaryCam audit ingestion

The full dated audit remains the canonical public snapshot. Its 167,039 bytes and SHA-256 `84021763580edfcd5c0bcb8560dfbc3e0e4b421c4b9bebc6d6c02c00b5ff14ba` are unchanged. Generated worker parts copy complete records; they are not a separately maintained legal analysis.

## Why this follow-up exists

PR #37's external GitHub Codex review identified two real gaps:

- The audit snapshot and seed are manifest URLs 94 and 95. A default run selects only the first 24 distinct URLs, so discovery did not make the audit an input.
- Explicitly requesting the full snapshot passes the byte limit but truncates it at 20,000 characters. The `claims` section starts at character 48,460 and `interpretationRules` at 151,693. Neither reaches the model input.

The worker also divides its 60,000-character total prompt budget among sources. Merely splitting a large document below 20,000 characters is insufficient if too many sources share a run.

## Explicit, bounded coverage

`public-knowledge/public-knowledge-regression-matrix.json` adds `notarycam-audit-part-01` through `notarycam-audit-part-13`. Each case explicitly selects three files listed in [the generated package](../NotaryGeek.PublicKnowledge.Worker/public-knowledge/evidence/notarycam-worker-2026-10-02/package.json): the common safeguards and both numbered record parts. Each file stays below 17,000 UTF-16 characters. A case requires all three parts together.

Across the 13 cases, all 83 original claim records and all 15 counterarguments appear unchanged. Every case includes all 15 interpretation rules, every audit limitation, the investigation frame, scope, and the complete dated source records referenced by its claims, included counterarguments and interpretation rules. Counterarguments linked to an overlapping finding are included where relevant. A case's named claim IDs define its coverage; a related-claim reference does not silently expand it.

The full snapshot retains the reader summary, procurement standard, timeline, open verification questions, correspondence and other sections. This projection does not claim to reproduce every top-level audit section. Full records are never clipped, paraphrased or independently edited.

Default manifest selection, the first 24 sources, runtime limits, host allowlists, named timer batches and worker runtime code are unchanged. The audit requires the explicit case IDs. As with any new matrix entries, a deliberate `All` regression selection now includes these 13 cases; no batch was started or scheduled by this change. The existing regression runner accepts these IDs individually through `-CaseId`. Do not interpret a discovery link or a dry-run result as a completed provider execution.

The generated source URLs use `main` and are prospective while this PR is a draft. The package hashes bind the generated files to the unchanged immutable snapshot. Offline tests substitute those exact local bytes using an in-memory HTTP handler; they do not assert that the new URLs are live. Verify publication before running a deployed worker against them.

## Reproduce without network or providers

From the repository root:

```bash
python tools/generate_notarycam_worker_cases.py --check
dotnet run --project tools/NotaryCamWorkerIngestion -- .
```

To regenerate after an intentional, reviewed change to this dated package, run the Python command without `--check`. The generator refuses any change to the pinned canonical byte count or hash. A new authored audit revision requires an explicit new provenance/version decision, not silent replacement.

The .NET tool links the actual production selection, source fetch/normalization and prompt-building code. It reproduces both old failures, then asserts that every selected source appears completely in the actual prompt, all claim records match the audit, every interpretation rule and limitation is intact, and every referenced source record resolves within that prompt. It uses only an in-memory HTTP handler with no socket fallback. Its client factory rejects any unexpected client creation. A compile-only storage-envelope signature permits linking production code without instantiating Azure storage; that adapter is outside the exercised methods.

At source `56fed9286622038ad5c1cded7d629f60aff104f0`, the focused tool passed 2,136 assertions across 13 cases. Actual prompts ranged from 49,462 to 55,262 characters, below the 60,000 default. No source or prompt truncation occurred. The seven linked production files matched their Git blob hashes. [The evidence record](notarycam-worker-ingestion-validation-20261003.json) preserves the baseline, per-case counts and source bindings.

This is production-method input evidence under current default settings, not an Azure Functions host test, deployed-configuration check, full solution build, provider/model evaluation or proof that an answer engine used the content. Existing model output limits can still summarize a case incompletely; input completeness is not a guarantee of output quality. Recheck official law before using a dated interpretation, and keep reference metadata distinct from an actual current official-source fetch.

No private source, customer data, credentials, queue, paid call, Actions run, merge or deployment is required by this validation.
