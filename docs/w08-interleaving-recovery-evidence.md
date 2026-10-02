# W08 bounded queued-recovery interleavings

Public synthetic evidence for issue #28 / existing draft #29, checked 2026-10-02.
Starting production head: `27097b551b75d2d64a9e3f1df26c61e73a3eb792`.
Read-only stack base #27: `3bb29f7ef42b636f32e04bc2a487123ec62e78a2`.
Observed main: `3c48b8c631f15b7f1ba4d8e1fe45d7d2a77da4d3`.
The PR checkpoint identifies the published head and review results.

## Implemented boundary

`NotaryGeek.PublicKnowledge.Worker.Tests/W08ModelChecking/` contains an
independent executable specification, a finite state explorer, a durable
byte/ETag protocol authority, an actual-SDK C# subprocess, and a deterministic
parent scheduler. This extends W02's useful SDK coverage; W02's sequential
dictionary-snapshot process fixture is preserved and is not counted as
concurrent-process or SIGKILL evidence.

Each C# child constructs the production research function, storage, promotion,
and queue services. Installed Azure.Storage.Blobs/Queues clients serialize HTTP
requests and parse real SDK responses. An injected `HttpMessageHandler` sends
those requests through stdin/stdout pipes. The parent separately chooses request
linearization and response delivery. No storage socket, emulator, application
host, cloud account, source server, or external provider is used.

The authority owns a disposable SQLite database with full-synchronous commits.
It stores opaque bytes, ETags, queue messages, and append-only synthetic effect
observations. It implements conditional Blob writes, reads, XML paginated lists,
and Queue XML/base64. It does not know admission, phase, receipt, latest-order or
recovery decisions. Provider effects are committed to that authority before the
worker receives a provider response; killing the worker cannot erase them.

The scheduler checks artifacts after linearizations, and a separate adapter
feeds normalized observations to the independent model oracle. Actual requests,
ETags, object identities, phase flags, status, commit/reply distinction, and
source/executable SHA256 bindings are retained. Raw HTTP/provider payloads are
not included in saved histories. All fixture values are generated public-safe
data. Configuration does not load environment providers; transports reject
unexpected endpoints, credential headers/SAS, and ambient cloud identity.

## Independent specification and assumptions

The abstract model specifies job/case identity, work fingerprints, conditional
admission, provider uncertainty, immutable results, receipts, publication
predecessors, submitted-time ordering and ordinal evidence-name tie-breaks.
It is a transition system derived from the issue and public contract, not a
copy of production branches.

Safety includes one provider boundary per admitted identity; unchanged evidence;
successful-receipt precedence; candidate → index → digest predecessors; identity
isolation; nonregressing latest pointers; and no completed case without its
documented receipt/publication backing. A committed write whose response never
arrives remains committed. A reservation without evidence grants no fresh
provider authority, regardless of worker death or elapsed time.

Conditional liveness is deliberately narrower: after faults cease, requests
terminate, writers/pointers settle, and every case with recorded evidence is
fairly redelivered, its pending publication can finish without another provider
operation. An uncertain reservation without evidence may remain unresolved
forever. This is not a liveness failure or permission to retry a provider.

Storage primitives assume atomic per-object conditional writes and linearizable
individual reads. List pages are ordered continuations; the pages and subsequent
per-object reads are not a global transaction. No atomic transaction spans a
reservation, archive, candidate, receipt, pointer, index, or digest. The derived
views may temporarily be stale while a rebuild is pending, including after a
final bounded observation. The oracle does not demand a simultaneous snapshot
across all pointers.

## Concrete reproduced defects and repairs

Every row failed against starting production code through the actual SDK/process
harness and passes with the repairs. `w08_negative_control.py` rebuilds the two
production files at the starting revision in a disposable copy, leaving the
working checkout unchanged. It checks the named failure classifications.

| Reproduction | Narrow repair | Reduced operation sequence |
| --- | --- | --- |
| An unsubmitted child creates a synthetic failure receipt and can change an already completed job to completed-with-errors. | Validate child membership before changing the job envelope. | Create job; deliver unknown child. |
| A legacy child resolves to `case-a` in one catalog and `CASE-A` in another. While the first provider reply is held, a second raw-case reservation admits a second provider operation. | Require the exact submitted case spelling before admission; reject catalog identity drift without rekeying existing state. | Create parent; record fan-out; hold first provider reply and deliver the alternate catalog child. |
| A replay with a missing catalog replaces a failed provider's immutable-result receipt with a synthetic job-blob receipt. Completed status then lacks the required evidence-phase backing. | Missing catalog entries throw without manufacturing another case result; recorded receipts/reservations remain recoverable when the correct catalog returns. | Create; finish recorded failed result; replay with absent catalog. |
| Reordering equivalent fingerprint-map JSON members after an archive commit passes job comparison but fails the serialized reservation fingerprint, stranding publication. | After validation, orchestration uses the durable envelope's fingerprint-map ordering for execution identity. | Create; kill after archive commit; replay reordered map. |

The reducer removes whole operations and reruns production processes, retaining
the exact failure code. Its result is deletion-minimal under that operation
vocabulary, not a claim of the globally shortest distributed trace. The abstract
history reducer separately minimizes oracle examples; those examples alone are
not called production defects.

No reservation schema, execution policy, source-fetch behavior, provider retry
policy, candidate authority/destination/content, or dependency version changed.
Case spelling now fails conservatively rather than creating another reservation.
Existing reservation fingerprints are not rewritten. The map-order repair uses
the persisted submission order; historical reservations created from a different
order than their own stored envelope remain an explicit compatibility limit.

## Executable commands

Requires .NET SDK 10 and Python 3 standard library. Tests were executed using
SDK 10.0.100/runtime 10.0.0 and the repository's unchanged packages (Blob 12.24.0,
Queue 12.27.0, Azure.Core 1.55.0). Set `PK_TEST_STORAGE_CONNECTION` absent; do not
configure real credentials. The five original Azurite tests remain separate.

From the repository root, with `dotnet` on PATH:

```bash
dotnet build NotaryGeek.PublicKnowledge.Worker.Tests/W08ModelChecking/W08Worker.csproj -m:1 --nologo
python3 NotaryGeek.PublicKnowledge.Worker.Tests/W08ModelChecking/w08_model_tests.py
python3 NotaryGeek.PublicKnowledge.Worker.Tests/W08ModelChecking/w08_trace_adapter.py
python3 -m unittest discover -s NotaryGeek.PublicKnowledge.Worker.Tests/W08ModelChecking -p '*_tests.py'
python3 NotaryGeek.PublicKnowledge.Worker.Tests/W08ModelChecking/w08_explore.py --mode matrix --out /tmp/w08-matrix
python3 NotaryGeek.PublicKnowledge.Worker.Tests/W08ModelChecking/w08_search.py --out /tmp/w08-search
python3 NotaryGeek.PublicKnowledge.Worker.Tests/W08ModelChecking/w08_extended.py --out /tmp/w08-extended
python3 NotaryGeek.PublicKnowledge.Worker.Tests/W08ModelChecking/w08_regressions.py --out /tmp/w08-regressions
python3 NotaryGeek.PublicKnowledge.Worker.Tests/W08ModelChecking/w08_negative_control.py --out /tmp/w08-original
dotnet build NotaryGeekPublicKnowledgeWorker.slnx -m:1 --nologo
dotnet test NotaryGeekPublicKnowledgeWorker.slnx -m:1 --nologo
```

All subprocess commands accept `--dotnet /absolute/path/to/dotnet`. Builds can use
an existing task-local NuGet cache. The negative-control build restores unchanged
dependencies and uses no provider/source/storage requests. It never rewrites the
working branch or replays an external operation.

For a minimal original-source counterexample, the negative-control command emits
the operation sequence and complete sanitized failing history. A selected rebuilt
worker can run it directly, for example:

```bash
python3 NotaryGeek.PublicKnowledge.Worker.Tests/W08ModelChecking/w08_regressions.py --case legacy-catalog-casing --operations create,prepare,subject --out /tmp/w08-one
```

Core and extended schedule artifacts also support `--mode replay --schedule FILE`
and `--replay FILE`, respectively. Replay requires the same bound source and
executable bytes. A clean build in a different path may produce different binary
hashes; rerun the named scenario to create its new binding rather than relabeling
the old evidence. The extended replay digest normalizes only generated candidate
filenames to stable first-seen identities because production timestamps remain
real. Exact immutable bytes are checked within each execution, not asserted equal
across independent provider runs.

## Bounds, results and limits

The four abstract graphs use two workers, at most three deliveries, one fault,
depth 60 and a 50,000-state cap per graph. They explore duplicate delivery,
independent cases under one job, submitted-time ordering, and equal-time evidence
ordering. The graphs exhausted their configured frontiers: **60,545 states,
148,992 transitions, no truncation**. Worker permutations and previously visited
complete states are pruned without erasing case/job/order identities. **568**
durable recovery projections have finite fault-free fair completion witnesses.
Transitions count schedule-prefix edges, not all unpruned permutations.

The concrete search is separately bounded to 12 schedules and 70 branching
actions. It branches on competing same-object HTTP operations with a writer;
duplicate prefixes and selected locally independent read/object choices are
pruned. Its remaining frontier is reported as truncated. It is not a distributed
state-space proof. The matrix separately explores response loss, cancellation,
real SIGKILL before/after commit at admission, provider effect, archive, latest
pointer, evidence phase, receipt, candidate, index/digest writes and markers,
and completion-record publication. Fresh workers also recover while another
process remains paused, and after all writers stop. Each session has a 90-second
parent bound, 3,500 actions and at most three fault-free recovery deliveries;
the child has its own 120-second lifetime and 1,000-request limit.

Primitive tests independently verify bytes, stable ETags, concurrent conditional
winners, paging, commit-without-reply survival across process death, and a
deliberately broken CAS implementation. Oracle negative controls include illegal
admission, altered evidence, downgraded receipts and premature publication.
The final committed evidence records actual per-suite results and source hashes.

The original full suite passed **131 / 0 failed / 5 skipped**. The original build
had one existing nullable warning in queued-state validation; it is not reported
as a new clean-warning result. The final solution build passed with the same
warning. Final standard `dotnet test` was attempted but VSTest aborted before
test execution because its local communication socket was denied by the current
environment. That aborted run is not reported as a passing test suite.

`W08InProcessTests.csproj` provides a socket-free fallback for this unchanged
suite: it discovers the actual Fact/InlineData cases and invokes their original
methods and assertions sequentially, rejecting unsupported data/lifecycle
features and checking exactly 136 cases. It passed **131 / 0 failed / 5 skipped**,
including W02 and #27 regressions. It is direct-method evidence, not VSTest or a
general substitute for the xUnit runner. Its first run exposed the runner's
handling of `InlineData(null)`, corrected before the passing run. A later fallback
build also reported two NuGet audit-source warnings when package audit metadata
was unavailable; no package/version was changed.

```bash
dotnet build NotaryGeek.PublicKnowledge.Worker.Tests/W08ModelChecking/W08InProcessTests.csproj -m:1 --nologo
dotnet exec NotaryGeek.PublicKnowledge.Worker.Tests/bin/W08InProcessTests/Debug/net10.0/W08InProcessTests.dll
```

Final commands and review are recorded in the PR checkpoint and evidence summaries.

Important limits:

- Synthetic protocol execution is not Azurite/Azure service-consistency evidence.
  The earlier emulator rejection was not retried. No external provider exactly-once
  claim is made.
- Queue serialization/fan-out is exercised. Azure Functions trigger leasing,
  visibility timeouts, poison queues and service-side dequeue/delete acknowledgement
  are not modeled. Completion here means the documented durable job publication.
- The connected model checks marker dependencies; candidate-set completeness and
  complete derived-view observations are explicitly not claimed by that adapter.
  Separate production schedules check actual candidate bytes and quiescent
  index/digest evidence sets.
- Arbitrary process/kernel/power/storage failures, large staged Blob uploads,
  leases, every SDK retry mode, and unbounded workers/faults are outside these bounds.
- Provider fixture content and submitted times are fixed. Production clock fields
  and candidate hashes can vary between runs. Controlled boundary choices and
  invariant outcomes, rather than cross-run body equality, are reproducible.
- No public corpus/source claim is changed. A completed technical candidate remains
  reviewable evidence under the existing review boundary.

## Review and safe stop

Self-review separated fixture bugs, overly strong oracle assumptions and production
findings. It repaired cleanup on startup/EOF failures, source-bound replay parsing,
fractional timestamp comparison, malformed immutable overwrite detection and
successful-receipt omission checks. A preliminary matrix was rerun after unrelated
harness changes made its source binding unstable; that preliminary run is not the
final stable-source evidence. The PR checkpoint names any separately executed
reviewer and the exact reviewed head; test passes are not independent model reviews.

All workers are owned local subprocesses. Cleanup kills/waits outstanding process
groups, continues past individual cleanup failures and removes disposable state.
The final checkpoint records process readback and explicit ownership release.
