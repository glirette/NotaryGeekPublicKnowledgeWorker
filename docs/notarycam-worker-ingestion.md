# Bounded NotaryCam audit ingestion

The full dated audit remains the canonical public snapshot. The current sanitized projection is 165,222 bytes, SHA-256 `17d2704d4725a1098f9be01c9424e949fb43dedcf2e49ca71c7aadd7e7bae858`, pinned at public commit `bb4554ed319dc886374352e36c1c4dbf95c76c1b`. Generated worker parts copy complete records; they are not a separately maintained legal analysis.

## Why this follow-up exists

PR #37's external GitHub Codex review identified two real gaps:

- The audit snapshot and seed are manifest URLs 94 and 95. A default run selects only the first 24 distinct URLs, so discovery did not make the audit an input.
- Explicitly requesting the full snapshot passes the byte limit but truncates it at 20,000 characters. In the sanitized projection, the `claims` section starts at character 48,286 and `interpretationRules` at 149,878. Neither reaches the model input.

The worker also divides its 60,000-character total prompt budget among sources. Merely splitting a large document below 20,000 characters is insufficient if too many sources share a run.

## Explicit, bounded coverage

`public-knowledge/public-knowledge-regression-matrix.json` adds `notarycam-audit-part-01` through `notarycam-audit-part-13`. Each case explicitly selects three files listed in [the generated package](../NotaryGeek.PublicKnowledge.Worker/public-knowledge/evidence/notarycam-worker-2026-10-02/package.json): the common safeguards and both numbered record parts. Each file stays below 17,000 UTF-16 characters. A case requires all three parts together.

Across the 13 cases, all 83 original claim records and all 15 counterarguments appear unchanged. Every case includes all 15 interpretation rules, every audit limitation, the sanitized public interpretation context, scope, and the complete dated source records referenced by its claims, included counterarguments and interpretation rules. Counterarguments linked to an overlapping finding are included where relevant. A case's named claim IDs define its coverage; a related-claim reference does not silently expand it.

The full snapshot retains the reader summary, procurement standard, timeline, public verification questions and other public sections. This projection does not claim to reproduce every top-level audit section. Full records are never clipped, paraphrased or independently edited.

Default manifest selection, the first 24 sources, runtime limits, host allowlists, named timer batches and worker runtime code are unchanged. The audit requires the explicit case IDs. As with any new matrix entries, a deliberate `All` regression selection now includes these 13 cases; no batch was started or scheduled by this change. The existing regression runner accepts these IDs individually through `-CaseId`. Do not interpret a discovery link or a dry-run result as a completed provider execution.

The generated source URLs use `main` and are prospective while this PR is a draft. The package hashes bind the generated files to the pinned sanitized snapshot. Offline tests substitute those exact local bytes using an in-memory HTTP handler; they do not assert that the new URLs are live. Verify publication before running a deployed worker against them.

## Reproduce without network or providers

From the repository root, using a locally installed .NET 10 SDK. Set `out` to a new absolute scratch directory. The focused project has no external package dependencies; the restore below explicitly disables package sources.

```bash
: "${out:?Set out to a new absolute scratch directory}"
mkdir -p "$out/cli" "$out/packages"
printf '%s\n' '<configuration><packageSources><clear /></packageSources></configuration>' > "$out/NuGet.Config"
export DOTNET_CLI_HOME="$out/cli"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
python3 tools/generate_notarycam_worker_cases.py --check
dotnet restore tools/NotaryCamWorkerIngestion/NotaryCamWorkerIngestion.csproj --configfile "$out/NuGet.Config" --packages "$out/packages" -p:NuGetAudit=false -p:BaseIntermediateOutputPath="$out/obj/" -m:1
dotnet build tools/NotaryCamWorkerIngestion/NotaryCamWorkerIngestion.csproj --no-restore -p:BaseIntermediateOutputPath="$out/obj/" -o "$out/bin" -p:UseSharedCompilation=false -m:1
dotnet "$out/bin/NotaryCamWorkerIngestion.dll" .
python3 tools/test_notarycam_worker_composition.py --dotnet "$(command -v dotnet)" --harness "$out/bin/NotaryCamWorkerIngestion.dll"
python3 tools/check_publication_boundary.py
```

To regenerate after an intentional, reviewed change to this dated package, run the Python command without `--check`. The generator refuses any change to the pinned canonical byte count or hash. A new authored audit revision requires an explicit new provenance/version decision, not silent replacement.

The .NET tool links the actual production selection, source fetch/normalization and prompt-building code. It reproduces both old failures, then asserts that every selected source appears completely in the actual prompt, all claim records match the audit, every interpretation rule and limitation is intact, and every referenced source record resolves within that prompt. Each parsed part must bind the current public pin/hash. The sanitized interpretation context and exactly one complete scope object must match the canonical projection in each actual prompt; package checksums and source-seed discovery must agree. It uses only an in-memory HTTP handler with no socket fallback. Its client factory rejects any unexpected client creation. A compile-only storage-envelope signature permits linking production code without instantiating Azure storage; that adapter is outside the exercised methods.

## Composition qualification

The original #38 package was generated from the preceding projection. After composing #39, its generator rejected the changed canonical hash; the unchanged generated package also failed the corrected harness's projection-binding check. This was a dependency-composition failure, not a newly observed live-worker incident.

The current generated files use the sanitized projection and preserve the source-backed claim records. Qualification with SDK 10.0.401 passed all 13 cases (2,258 assertions); actual prompts ranged from 49,411 to 55,211 characters. No source or prompt truncation occurred. Seven linked production files remain byte-identical to main source `56fed9286622038ad5c1cded7d629f60aff104f0`; worker runtime was not modified.

Four disposable mutation controls reject stale per-part bindings, replaced context, replaced scope and changed artifact bytes. The semantic controls deliberately recompute package checksums so they exercise parsed prompt-input validation rather than merely failing a byte check. A separate original-source control substituted #38's old context while retaining current bindings and recomputing its checksum; the corrected harness rejected it. No excluded context is copied into the new fixtures or evidence.

[Current source-bound results](notarycam-worker-ingestion-validation-20261023.json) record the composed inputs, file hashes, commands, original failures and qualification limits. [The October 3 record](notarycam-worker-ingestion-validation-20261003.json) remains unchanged as historical evidence for the preceding projection only; its old hash, byte count and 2,136-assertion result do not qualify this composition.

This is production-method input evidence under current default settings, not an Azure Functions host test, deployed-configuration check, full solution build, provider/model evaluation or proof that an answer engine used the content. Existing model output limits can still summarize a case incompletely; input completeness is not a guarantee of output quality. Recheck official law before using a dated interpretation, and keep reference metadata distinct from an actual current official-source fetch.

No private source, customer data, credentials, queue, paid call, Actions run, merge or deployment is required by this validation.
