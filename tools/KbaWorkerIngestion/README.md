# Bounded KBA worker inputs

This is an offline ingestion and qualification successor to [KBA research #40](https://github.com/glirette/NotaryGeekPublicKnowledgeWorker/pull/40). It adapts the complete-record packing and linked production-method harness reviewed in [#38](https://github.com/glirette/NotaryGeekPublicKnowledgeWorker/pull/38) at `07959e7b074c0c48fa3b1d120f83fa9c2f6d2085`. It adds no research engine or worker runtime behavior.

## Inputs and selection

The [generated package](../../NotaryGeek.PublicKnowledge.Worker/public-knowledge/evidence/kba-worker-2026-10-30/package.json) binds both original research JSON files at `e4c31b4a0b3c2f223978dcb06c6af64f6bb4385d` by immutable URL, exact byte count and SHA-256. Regeneration refuses changed original bytes. It neither refetches nor newly adjudicates the cited law and provider pages. Their checked date remains October 30, 2026.

The original formatted matrix reaches `sourceRefreshLimits` only after character 24,529. The actual production fetch method cuts the unsplit input at 20,000 characters, omitting those qualifications. The generated three-part package packs whole records below 17,000 UTF-16 characters each, with no clipping or paraphrasing. Normalization removes only the final newline.

Every case supplies all three parts, preserving all 24 claims, 22 dated source records, four contrary interpretations, seven proposals and 22 synthetic examples. All other fields of both original JSON documents appear exactly once across the three parts, including scope, measured-fact limits, refresh limitations, route facts, alternative-method evidence, Virginia methods, privacy boundaries and unresolved questions. The broad context is intentional: each focus selects one proposal or example, while the competing readings and qualifications remain available.

[regression-inputs.json](../../NotaryGeek.PublicKnowledge.Worker/public-knowledge/evidence/kba-worker-2026-10-30/regression-inputs.json) contains 29 `PublicKnowledgeRegressionCase` inputs: `kba-rule-r01` through `kba-rule-r07`, and `kba-example-e01` through `kba-example-e22`. `mustHold` and `failureSignals` come directly from the reviewed proposals; examples also preserve their exact proposed disposition and meaning. These fields are expectations for future review, not observed answers. The current production prompt builder does not itself insert these scoring fields; their complete originating records are included in the fetched parts and checked there.

These cases are **not registered**. Shared manifests, source seeds, regression matrix, default first-24 source selection, named batches and runtime limits are unchanged. Even the current shared `All` selection does not contain these new cases. The [discovery proposal](../../NotaryGeek.PublicKnowledge.Worker/public-knowledge/evidence/kba-worker-2026-10-30/discovery-proposal.json) remains unapplied. Generated `main` URLs are prospective until a separate reviewed integration publishes them there; local substitution establishes no anonymous HTTP availability or live ingestion.

## Reproduce

Use official .NET SDK **10.0.401** and Python 3. The focused project needs no external NuGet packages. Its project and compile-only storage record adapter are copied unchanged from #38. Seven linked production files are hash-checked against the unchanged source in #40 and #38. No SDK or PowerShell files belong in the repository; PowerShell is unnecessary for this Python/.NET path.

Set `out` to a fresh absolute scratch directory and ensure `dotnet` selects that SDK:

```bash
: "${out:?Set out to a fresh absolute scratch directory}"
mkdir -p "$out/cli" "$out/packages"
printf '%s\n' '<configuration><packageSources><clear /></packageSources></configuration>' > "$out/NuGet.Config"
export DOTNET_CLI_HOME="$out/cli" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
python3 tools/KbaWorkerIngestion/generate.py --check
dotnet restore tools/KbaWorkerIngestion/KbaWorkerIngestion.csproj --configfile "$out/NuGet.Config" --packages "$out/packages" -p:NuGetAudit=false -p:BaseIntermediateOutputPath="$out/obj/" -m:1
dotnet build tools/KbaWorkerIngestion/KbaWorkerIngestion.csproj --no-restore -p:BaseIntermediateOutputPath="$out/obj/" -o "$out/bin" -p:UseSharedCompilation=false -m:1
dotnet "$out/bin/KbaWorkerIngestion.dll" .
python3 tools/KbaWorkerIngestion/test_mutations.py --dotnet "$(command -v dotnet)" --harness "$out/bin/KbaWorkerIngestion.dll"
python3 tools/KbaResearchValidation/validate.py
git diff --check
```

Run the generator without `--check` only to reproduce its six designated JSON artifacts. Its output is deterministic. It writes no shared manifest or original research file. The qualification JSON reports below are executed results, not generator outputs.

The harness calls actual production `ParseRegressionMatrix`, `SelectSourceUrls`, `FetchSourceAsync`, `NormalizeSourceText`, `BuildPrompt` and `EstimateTokens`. Only an in-memory `HttpMessageHandler` supplies exact local bytes. Unknown requests throw; no DNS/socket fallback exists, and the client factory forbids external or provider clients. No Functions host, storage, queue or provider method is invoked.

The harness requires every actual fetched body to occur completely in the actual prompt, without the snippet-truncation marker, within current source/byte/prompt/token limits. It compares the complete parsed records and context against the immutable originals, checks source references and proposal/example expectations, and verifies that discovery remains unapplied. This validates input integrity, not legal truth. The production scorer is lexical and is not invoked here; its future verdict would not establish a meaning-review pass.

Thirteen disposable controls challenge original byte drift, generated byte drift, stale pins, omitted claim and refresh qualifications, incorrect claim/source binding, changed source date, changed contrary interpretation, changed example meaning, changed worker expectation, incomplete case source selection and discovery activation. Semantic mutations recompute artifact checksums before running the compiled harness, ensuring the intended record or meaning comparison rejects them.

## Executed evidence and review boundary

- [Actual production-method result](harness-result.json): 29 cases, 3,854 assertions, complete bodies in prompts of 42,168–42,222 characters. Each case includes the entire dated research JSON content across three sources. Input evidence only.
- [Mutation controls](mutation-result.json): all 13 rejected at their intended diagnostic.
- [Qualification and source identities](qualification.json): SDK integrity, exact source hashes, full solution build, 43 existing tests, original research checks and publication checker provenance.

Full solution build passed with zero warnings/errors; all 43 existing tests passed. The unchanged publication checker and its 11 tests were used from [#39](https://github.com/glirette/NotaryGeekPublicKnowledgeWorker/pull/39) at `5deafd015029752b8619e9f9e7cae55151516f18`, outside its owned source paths. Screening applies to this additive delta, plus manual review; it is not proof that unchanged historical content is publication-ready.

Research meaning review remains distinct from mechanical input qualification. This successor preserves the reviewed research and its pending customer-guidance status. No fresh legal conclusion, live model-answer pass, current-source refresh, completed transaction, recipient acceptance, public-site activation or deployment is implied. No private context or customer records are included. No provider calls, queue submissions, merge or activation occurred.
