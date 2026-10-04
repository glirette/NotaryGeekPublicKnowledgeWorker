# W02 SDK and queued recovery evidence

Checked 2026-10-01 for issue #28 / draft PR #29. Starting parent:
`d2e5d79eb26167c3e39d2505924a1472a673068d`; dependency/base #27:
`3bb29f7ef42b636f32e04bc2a487123ec62e78a2`; observed main:
`3c48b8c631f15b7f1ba4d8e1fe45d7d2a77da4d3`.
The PR checkpoint records the published final head. Dependency and stack are unchanged.

## Evidence boundary

These are actual Azure SDK clients using `HttpClientTransport` with a synthetic
`HttpMessageHandler`, without storage sockets or account authentication. The store
implements the exercised BlockBlob upload/download/HEAD/list protocol, XML paging,
request bodies, ETags, create-only and matching-ETag writes, error responses, queue
serialization, cancellation and bounded faults. It contains no worker admission,
phase, evidence reconciliation or ordering decisions. Source/provider handlers
accept only exact synthetic URLs and fail on unexpected requests. Fixture clients
reject external storage URLs, authorization/SAS and ambient cloud identities.
Configuration is explicitly constructed without environment providers.

**No emulator or real-service test ran.** The five original Azurite facts still
skip with `PK_TEST_STORAGE_CONNECTION` unset. Their mutation/cleanup paths now
accept only the fixed loopback development account shorthand and reject ambient
cloud identity before constructing clients. The earlier emulator-start rejection
was not retried. This evidence does not prove external provider exactly-once,
Azure service consistency, authorization, large/block-staged uploads or leases.

## Reproductions and repairs

| Reproduced failure | Repair and executable evidence |
| --- | --- |
| Listed latest records returning null, invalid JSON or HTTP errors could be omitted, yielding an incomplete index or healthy empty digest. | Fail incomplete observations; validate record/pointer identity. Storage tests cover 404/403/500, null/empty/truncated shapes; worker test preserves its successful receipt while publication stays pending. |
| A persisted `admitted` phase could call the provider again; contradictory/unknown reservations were accepted. | Validate durable phases, fingerprint, timestamp, evidence identity and publication order. Five contradictory variants and three malformed-record variants assert zero provider calls and retained state. |
| Modern terminal envelopes without receipts, and unknown job statuses, could create a fresh provider admission. | Validate queued shape/status before transition or admission. Four variants assert zero calls, no new reservation and unchanged status. |
| An uncertain pre-reservation legacy parent could fan out children. | Require its durable fan-out marker. Partial acknowledged/ambiguous fan-out and duplicate children remain compatible; unmarked running legacy work stays blocked. |
| Legacy successful receipts could prevent recovery of interrupted derived publication. | Reuse a validated existing reservation and matching archive, without creating an admission for uncertain legacy work. Single-case and fan-out variants recover publication with one total provider call. |
| A pointer advance during index/digest commit could return stale completion. | Re-observe the full latest set after writing or reusing an equal view. Both views rebuild on advance; ten repeated advances exhaust the bound without returning completion, then recover when writes settle. |

Additional fixtures prove one admission among twelve synchronized callers; distinct
cases executing independently; successful and failed provider duplicates across
fresh workers; immutable archive exact replay/conflict preservation; same-second
and out-of-order latest selection; stale ETag conflicts; interrupted list paging;
ten failed phase CAS attempts; repeated candidate/index/digest interruptions;
cancellation and precommit archive failure. Lost acknowledgements are injected
after reservation, archive, latest pointer, evidence phase, receipt, candidate and
publication-marker commits. Assertions include archived bytes/identity, receipts,
publication artifacts and exact provider-call counts.

Derived views remain bounded snapshots, not a transaction across all pointers.
Another write can occur after the final observation. A temporarily stale view can
persist while a retry remains pending; no current/atomic snapshot claim is made.

## Executed checks

Ubuntu 24.04 x64; .NET SDK 10.0.100, runtime 10.0.0, MSBuild 18.0.2.
Resolved packages: Azure.Storage.Blobs 12.24.0, Azure.Storage.Queues 12.27.0,
Azure.Storage.Common 12.28.0, Azure.Core 1.55.0; xUnit 2.9.3,
Microsoft.NET.Test.Sdk 17.14.1. Commands ran with telemetry disabled,
`PK_TEST_STORAGE_CONNECTION` unset and a task-local package directory.

- `dotnet build NotaryGeekPublicKnowledgeWorker.slnx -m:1 --nologo`: passed, zero warnings/errors.
- `dotnet test NotaryGeekPublicKnowledgeWorker.slnx -m:1 --nologo`: **131 passed, 0 failed, 5 skipped**, including dependency #27's regressions.
- Focused filter `FullyQualifiedName~SyntheticQueued|FullyQualifiedName~SyntheticLatest|FullyQualifiedName~LocalStorageSafety`: **55 passed, 0 failed, 0 skipped**.
- `git diff --check`: passed.
- Original source plus only client injection seams: **33 passed, 22 expected failures, 0 skipped** across the same 55 fixtures. The reproducible negative-control script verifies exact failing methods and variant counts:
  `python3 NotaryGeek.PublicKnowledge.Worker.Tests/SyntheticProcessFixture/run-negative-control.py [dotnet-path]`.

Initial fixture development exposed a queue XML BOM parsing error, corrected by
loading the request as an XML byte stream before counting legacy reproductions.
Initial default parallel restore failed without diagnostic errors; single-node
MSBuild succeeded. SDK extraction required `--no-same-owner`. A provisional
non-C# project extension failed its harness build and was replaced by `.csproj`.
An overlapping provisional harness/test build produced a transient metadata
warning; final checks were serialized and clean. Assertions/skips were not weakened.

## Separate-process recovery

Build `NotaryGeek.PublicKnowledge.Worker.Tests/SyntheticProcessFixture/SyntheticProcessFixture.csproj`
with `dotnet build ... -m:1 --nologo`. Then run its DLL using `dotnet exec` twice:

```bash
W02_FIXTURE_STATE="$(mktemp -d)"
dotnet exec NotaryGeek.PublicKnowledge.Worker.Tests/bin/SyntheticProcessFixture/Debug/net10.0/SyntheticProcessFixture.dll interrupt "$W02_FIXTURE_STATE"
# Expected exit 42: one provider call; immutable successful receipt; publication pending.
dotnet exec NotaryGeek.PublicKnowledge.Worker.Tests/bin/SyntheticProcessFixture/Debug/net10.0/SyntheticProcessFixture.dll recover "$W02_FIXTURE_STATE"
# Exit 0: zero provider calls; identical archive SHA256; one candidate; completed index/digest.
```

Both exits/assertions were observed. Bodies persist at synthetic request boundaries
and are loaded by the fresh process; synthetic ETags are regenerated on load.
This is sequential process-restart evidence, not concurrent multi-process storage,
a hard-kill durability test or an Azure server emulator. Thread concurrency tests
use shared protocol storage and deterministic barriers with ten-second bounds.

## Review and next step

Adversarial self-review traced admission, all transitive queued calls, immutable
replay, late failures, legacy boundaries, snapshot races, cancellation, retry
limits, cleanup and public-data boundaries. It produced the extra legacy recovery
and terminal-envelope reproductions above. No independent reviewer ran for this
follow-up; prior review of the starting head does not cover these changes. Tests
are verification, not an independent model review. Session instructions identify
the GPT-6 family; no exact selectable runtime model identifier was exposed.

Next: review this draft's repaired storage/legacy/snapshot boundaries, then
revalidate the combined source after #27 integration. The permitted isolated
emulator/real-service comparison and separate candidate-destination review remain
activation limits. No live source/provider/storage/queue data, hosted execution,
merge, readiness transition, deployment or corpus content change was performed.
