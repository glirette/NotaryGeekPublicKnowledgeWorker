# W20 source contract evidence

W20 preserves draft #27 and its history. Starting head: `3bb29f7ef42b636f32e04bc2a487123ec62e78a2`; base, current main, and merge-base: `3c48b8c631f15b7f1ba4d8e1fe45d7d2a77da4d3`. The publication checkpoint on #27 identifies the final commit and tree, avoiding a self-referential commit identifier in this file.

## Reproduced defects and repairs

| Mechanism | Minimized replay | Starting behavior | Repair |
| --- | --- | --- | --- |
| Typed Location enumeration hides whitespace | Start at `https://source.example/start`; return 301 with a single `Location` value containing three spaces | Sends a second request to `https://source.example/%20%20%20`, then accepts a synthetic 200 | Read `Headers.NonValidated` before emptiness/shape checks; no second send |
| Case-distinct source omitted | One research request containing `/L` and `/l`; also `/?E` and `/?e` | Only the first source is fetched | Compare validated authority-normalized, path/query-sensitive identities |
| Batch reuses wrong source | Two commands requesting `/L` then `/l`; also `/?E` then `/?e` | Second result carries the first command's source; only one request sent | Require ordinal-equal requested sequences for prepared-source reuse |

The pair reducer replayed the production functions and reduced `/LongDocument` versus `/longDocument` to `/L` versus `/l`, and `/doc?key=VALUE` versus `/doc?key=value` to `/?E` versus `/?e`. Final exact-signature controls reproduce both `second-case-distinct-source-dropped` and `second-command-reuses-first-source`. The whitespace recipe is minimal by redirect-step deletion, not a claim of minimum whitespace length.

Independent review found an intermediate regression in the initial ordinal-only selection fix: two host-case aliases displaced a distinct source at the source cap. That failure was executed before repair. Final selection normalizes only validated identities, with a separate key prefix for inadmissible forms; a second regression proves a disallowed punycode spelling cannot shadow an allowed Unicode source. Citation normalization and SourceIndex production code needed no repair.

## Executed checks

- All 115 starting checkout blobs matched their Git blob hashes. Repository instructions, routed docs/corpus indexes, full #26/#27 discussions, review, refs/merge-base and open-PR inventory were refreshed. The routed technical cache and its URL fixture were searched read-only.
- Existing synthetic baseline: **23 passed, 0 failed**, direct-method invocation with the documented assertion shim.
- Final reproducible runner: **37 passed, 0 failed**, including the 23 baseline invocations. Compiles the actual source retrieval/citation services, models, configuration, helper and tests against the installed .NET 10 shared framework. One nullable warning in an unchanged baseline method arises from the minimal assertion shim's missing null-flow annotation; zero errors.
- Explorer: **1,180 redirect recipes**; seed `0x20C17E`; generated lengths 0–8; status outcomes: 695 terminal, 222 policy refusal, 143 invalid-location refusal, 75 loop refusal, 45 limit refusal. **484 citation pairs** through each of two production admission paths; **14 validation cases**. Coverage bounds and exclusions are in [the contract guide](source-retrieval-contract-tests.md).
- Parent and exec-child IPv4/IPv6/Unix socket denial verified with EPERM; ambient child environment and package sources cleared. No source/provider/queue/cloud request ran.
- Normal `dotnet build NotaryGeekPublicKnowledgeWorker.slnx` with isolated empty package sources: **blocked, NU1100**, required Azure/Functions/xUnit packages unavailable. Normal `dotnet test` with isolated restore configuration: **blocked, NU1100**. An earlier `test --no-restore` returned no test output; it is not counted as a test pass. **No VSTest execution is claimed.** Network namespaces were unavailable; no isolation relaxation was used.
- One intermediate custom-body fixture hung during cancellation because its buffered-stream path did not receive the test token. The 120-second supervisor timed out and reaped it. The fixture was corrected to cancel during a token-bearing stream read and fail boundedly on a missing token; final tests pass. This was a synthetic-adapter error, not a claimed production defect.
- `git diff --check`, Python syntax compilation, allowed-path checks and source binding checks passed. No project/package/lockfile, corpus, provider/default registration, queued/storage or workflow changes.

Exact runnable command from the repository root (replace paths with a trusted installed SDK and a fresh output location):

```bash
python NotaryGeek.PublicKnowledge.Worker.Tests/W20SourceContracts/run_direct.py \
  --dotnet /absolute/path/to/trusted/dotnet \
  --out /absolute/path/to/new-disposable-output
```

Each output directory contains `build.log`, `run.log`, `isolation.log` and `source-bindings.json`. Standard build/test attempts used `--configfile` / `-p:RestoreConfigFile` pointing to an untracked NuGet configuration with `<packageSources><clear /></packageSources>`, `-m:1`, and `--nologo`, under the same socket-denial/clean-environment boundary.

## Negative controls

All controls compiled and executed in separate disposable copies under the published runner. Each exited 1 with test failures. They are coverage evidence, not assertions that the final source has these defects.

| Scratch variant | Observed detection |
| --- | --- |
| `per-hop` | Oracle sees an unallowlisted second send and unexpected terminal content; existing blocked-hop tests also fail |
| `auto-redirect` | Real DI handler inspection fails with source `AllowAutoRedirect=True`, before substitution |
| `wrong-name` | Production research consumer attempts the OpenAI client; synthetic transport rejects that name before any provider send |
| `collapsed-identity` | `/doc` versus `/Doc` is incorrectly admitted by structured citation validation |
| `original-selection` | Exact-signature source-drop/batch-reuse counterexamples replay and reduce |
| `original-location` | Whitespace recipe requests an escaped-space path; explicit no-second-send regression fails |

## Independent review and remaining boundaries

A separate read-only reviewer was requested through the available agent tool with configuration `gpt-6.1-sol`, high reasoning. The tool accepted that configuration; the reviewer could not independently inspect its runtime model identity. No invented model switch, paid API, hosted review job or reviewer test execution is claimed. Review was source-bound using SHA256s, not inferred from these tests. Initial findings were the authority-cap regression, overly broad reduction witnesses and missing consumer fault/cancellation coverage; all were addressed and re-reviewed. The final review addendum and hashes are recorded in the #27 checkpoint.

The full Azure Functions host/build, repository package graph, standard VSTest runner, native transport parsing/wire/TLS/DNS behavior, arbitrary schedules and process-crash recovery remain untested here. #29's dependency still pins the original #27 head; its integration validation is a separate gate. No merge, readiness transition, deployment, workflow dispatch, service restart, or corpus meaning changes were made.

Before publication, workflow inspection found deploy/batch workflows manual-only, validation on pull requests/main pushes, and the `pull_request_target` review job guarded against drafts. Publication uses `[skip ci]` and retains draft state. No hosted job is dispatched.

Next concrete step: in an approved isolated environment with the existing package graph available, build/test the complete solution and run standard VSTest with isolated local runner communication, then validate the dependent #29 stack separately. This evidence does not mark either draft ready.
