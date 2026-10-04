# W29 source/replay integration

Synthetic public-safe verification only. No source, provider, queue or cloud service
was contacted by worker/test processes. No merge, activation or readiness claim.

## Source identity and integration

Starting #29: `5cfa849c00ea31cd5e5cc8a7c2cf1bb99d9be6bb`.
Old stack base: `3bb29f7ef42b636f32e04bc2a487123ec62e78a2`.
Exact W20 dependency: `a180fa6c50a2a9ec8164cec7285f7e328adc5f89`.
Recorded old main: `3c48b8c631f15b7f1ba4d8e1fe45d7d2a77da4d3`;
refreshed main: `56fed9286622038ad5c1cded7d629f60aff104f0`.
Main's content merge is excluded. Two-parent integration commit
`7d21ab82f2f84948aaec9f88b704e3e4a20a9bd2` preserves both original
histories; its tree is `a98e567398801196460b338c15f2f0a01ca64c8c`.
All original W02/W08/W20 fixtures, evidence and review bindings remain historical.

The production queue order is **reservation → fetch → provider → archive → derived
publication**. W29 does not reorder that contract. A source-stage interruption after
reservation may therefore leave an unknown outcome with no provider effect and no
automatic liveness guarantee. A rejected source body cannot enter the source prompt;
a recorded failure result can still describe a rejected source.

## Executable harness

`NotaryGeek.PublicKnowledge.Worker.Tests/W29IntegratedContracts/` adds a separate
worker entry point, scheduler, independent source oracle, operation reducer,
exhaustive small domain, historical replay and precise production mutations.
The worker invokes the actual queued function/research/storage/promotion classes
and installed Azure SDK clients. No replacement admission/recovery algorithm or
Functions shim is used. Production named source registration is instantiated and
its automatic-redirect property checked before replacing transport with pipes.
The old-head baseline alone uses the prior equivalent named registration.

W08's SQLite byte/ETag authority and process scheduler are reused unchanged, with
attribution retained. The authority never decides admission or replay. The oracle
uses explicit submitted original/final URL/body triples, compares provider-input
body identity, archive provenance and immutable hashes, and compares published
candidate fields with archived candidates. Body hashes describe synthetic prompt
inputs; production archives do not themselves store an independent source-body
hash. No stronger provenance guarantee is inferred.

Every W29 worker probes inherited IPv4, IPv6 and Unix socket denial before reading
configuration. The launcher clears the environment, disables diagnostics, installs
an inherited seccomp denial, probes an exec child, and enforces a real 900-second
outer deadline. Individual sessions have a real 90-second watchdog and 3,500-action
bound; workers also have a real 120-second cancellation deadline. Worker groups are
reaped in finally blocks; the launcher is a subreaper for deadline cleanup.
Timeout/unreached-boundary results fail. Completed schedule records are checkpointed
to a scratch JSONL file. No emulator startup or earlier isolation rejection is retried.

## Reproduction

Use an existing trusted .NET 10 SDK and a task-local cache containing the repository's
unchanged declared packages. Package acquisition is setup, separate from fenced
worker execution. The Functions SDK also generates a net8 WorkerExtensions project;
its declared packages must be available before an offline full build.
No project/dependency/lockfile changes are required.

Set shell variables `sdk`, `cache`, `cli`, `tests` and `out` to absolute task-local paths;
`tests` is the W29IntegratedContracts directory. Prefix each execution below with:

```bash
python3 "$tests/isolate.py" "$sdk" "$cache" "$cli"
```

The remaining command arguments are:

```bash
# Compile the entire repository; test runner failure is not a test pass.
"$sdk" build NotaryGeekPublicKnowledgeWorker.slnx --no-restore -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=false
"$sdk" test NotaryGeekPublicKnowledgeWorker.slnx --no-build --no-restore -m:1
# Generate a scratch-only project referencing actual production.
python3 "$tests/build.py" --out "$out/build"
"$sdk" build "$out/build/W29Worker.csproj" -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=false
python3 "$tests/explore.py" --dotnet "$sdk" --worker "$out/build/bin/Debug/net10.0/W29Worker.dll" --suite matrix --out "$out/matrix.json"
python3 "$tests/small_domain.py" --dotnet "$sdk" --worker "$out/build/bin/Debug/net10.0/W29Worker.dll" --out "$out/small.json"
python3 -m unittest discover -s "$tests" -p '*_tests.py'
```

`explore.py --suite baseline` runs selection/path/query/whitespace controls against
an **old-head-built** worker, reduces operations by rerunning production, and checks
all eight subsets of the three-operation domain. Never label that command a green
combined-tree suite: its expected result is four source-control failures.
`mutants.py` makes new scratch copies with exactly one specified production edit:
archive create-only protection removed, or an existing reservation returned as newly
admitted. Build those copies and run `explore.py --mutant archive|reservation` with
`--source-root` pointing to that copy. An expected oracle violation is required.

`history.py` creates a candidate-commit crash state through an original-source
worker, then `--read` recovers through the combined worker without source/provider
operations. Retain its byte bundle privately in scratch; published historical
evidence contains hashes and operation traces, not raw synthetic provider responses.

## Scope of conclusions

The matrix covers distinct path/query case, trailing slash, escaped delimiters,
aliases versus distinct final resources, rejected/looping/malformed/interrupted
redirects, concurrent preparation, duplicate deliveries and crash/lost-ack boundaries.
Source content changes after the initial attempts; recovery must preserve archived
bytes and never refetch or invoke a provider again. Partial listings use one-object
pages; normal concurrent SDK requests exercise stale ETags.

The small exhaustive domain is only the six legal orderings of two pending source
request/response pairs. One source/effect projection is compared with all six;
this does **not** validate pruning of the entire job state space. Other concrete
matrix schedules and seeds are sampled, not exhaustive. Abstract permutation tests
are distinct from concrete processes and actual SIGKILLs. Reducer minima are proven
only within the declared three-operation subset domain.

Safety does not imply unconditional liveness. Recovery requires recorded evidence,
fair redelivery, terminating operations, faults ceasing and settled writers/pointers.
Unknown reservations without evidence can remain unresolved indefinitely. There is
no transaction across all pointers and no universal/external exactly-once claim.
Synthetic byte/ETag behavior is not Azure wire/consistency, trigger lease, DNS/TLS,
real provider or emulator evidence. Native transport and full Functions host behavior
remain outside these process tests.

Final executed counts, source bindings, controls, review and remaining gates are
recorded in the final evidence checkpoint accompanying this document.
