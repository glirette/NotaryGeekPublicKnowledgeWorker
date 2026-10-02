# Public Knowledge Run Queue

Reviewed: 2026-08-04

The worker accepts bounded jobs for public-source research and regression checks.

## Public Boundary

Jobs must be reconstructable from public repository data and public sources. They must not contain:

- customer, identity, payment, or private case data;
- secrets, credentials, protected endpoints, or private storage locations;
- private prompts, raw model responses, execution telemetry, or provider-account details;
- private workflow names, repository references, deployment settings, schedules, or operating runbooks.

Only sanitized, source-backed artifacts belong in public branches and pull requests. Official source text and source metadata must remain distinct from summaries or interpretation.

## Review Boundary

A completed job is not authority and is not automatically publishable. Recheck controlling official sources, validate the artifact shape, and require normal repository review before merge.

Private workflow integration, deployment, recovery, and retention procedures are intentionally documented outside this public repository.

## Replay and result evidence

New queued jobs bind the submitted case snapshots and execution settings to a durable job identity. Unknown job statuses and terminal modern envelopes missing case receipts fail conservatively. Durable reservations accept only consistent unknown-outcome or evidence-recorded phases; the transient `admitted` return value is never a persisted authorization. Publication markers require recorded evidence and follow candidate → index → digest order. Each case reserves its own provider boundary through a conditional storage write. A repeated delivery can reuse immutable recorded evidence and finish pending candidate, latest-index, and digest publication. A reservation with no result is explicitly an unknown outcome; expiration, cancellation, or another delivery cannot authorize another provider operation. Queued OpenAI work uses one request attempt. This does not establish external exactly-once execution.

Archived result names include the job/case identity and full timestamp precision. Existing content must match an idempotent replay; latest-case pointers use conditional writes ordered by the submitted run time. Publication remains reviewable derived output, not a corpus claim or source authority. Jobs can show `publishing` with successful case receipts while candidate, index, or digest work awaits replay; the case phase records completed steps.

The global index and digest are rebuildable snapshots of latest-case pointers. Conditional writes compare the complete per-case evidence set. Listed records that are unreadable, malformed, or have conflicting identities fail the rebuild instead of being silently omitted. After a derived write (or reuse of an equal saved view), the worker re-observes the latest set and rebuilds if it changed. This is a bounded snapshot observation, not a transaction across all case pointers; another completion can advance a pointer after the final observation. A temporarily stale persisted view can exist while a rebuild is pending. If concurrent updates prevent completion within the bounded retry limit, the case stays in `publishing` for a later replay without a provider call.

Legacy jobs with a completed receipt retain that result. Older `running` jobs without a receipt or reservation have an unknown provider outcome and cannot safely be executed again. For a legacy batch first seen in `queued` or `preparing`, the worker conditionally records fan-out admission before sending child messages; repeated partial fan-out may send duplicate children, whose per-case reservations prevent another provider operation. Older `running` batches without that marker cannot fan out again. A legacy case with a validated reservation and matching immutable evidence can finish interrupted publication without a provider call; absent reservations remain unresolved rather than guessing whether operations already ran. No new scheduler or automatic repair loop is introduced.

New technical candidate evidence uses the neutral destination `technical-review` while retaining the `technical` authority lane. This changes its candidate ID and storage segment; earlier candidate blobs remain immutable and may coexist with a new candidate for the same public source. Reviewers should compare candidates before any separate promotion decision. Routing beyond this public review boundary remains outside this repository.

Local executable SDK evidence and its limits: [W02 recovery evidence](w02-sdk-recovery-evidence.md).

W08's concurrent-process observations require the exact submitted child/case
spelling at the reservation boundary. A missing or changed catalog entry does not
manufacture a new failed result or replace recorded evidence. Equivalent
fingerprint-map member ordering is reconciled using the persisted job envelope's
ordering. Existing reservation fingerprints are not migrated or rewritten.
See [W08 bounded interleaving evidence](w08-interleaving-recovery-evidence.md) for
the executable specification, actual-SDK pipe harness, minimized reproductions,
fairness assumptions and explicit synthetic-consistency limits.
