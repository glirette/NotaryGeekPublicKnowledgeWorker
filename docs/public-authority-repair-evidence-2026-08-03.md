# Public Authority Repair Evidence — 2026-08-03

This is a sanitized source-control checkpoint for `glirette/BootStrapCritical#138`. It contains no key values, private run payloads, or raw provider output.

## Verified Pre-Repair Baseline

- The Azure Function was running code absent from GitHub `main`; the relevant earlier pull requests were closed unmerged after a local deployment.
- The dedicated public-source OpenAI key resolved and provider calls returned HTTP 200.
- Latest-run accounting contained 33 cases, including incomplete, empty, invalid-JSON, and not-scored output that had been conflated with successful provider transport.
- The live output ceiling was 1,600 tokens with medium reasoning. One observed response consumed the visible-output budget in reasoning and returned no response text.
- The two-hour pump was configured with fixed regression batches, creating repeated spend without incremental selection state.
- The protected operator snapshot reported unhealthy state and review attention while no job was actively running.
- Stored run history and job envelopes were large enough to require an explicit retention policy, but not unreviewed deletion.

## Source Repair Evidence

- Closed-PR daily ingestion and dedicated-key behavior were reconciled into the current source branch.
- Strict Responses API JSON Schema plus local validation separates usable output from HTTP success.
- Failure classification covers incomplete, empty, invalid JSON, schema-invalid, stale, and unfetched-citation results.
- Provider evidence records provider, auth mode, model, attempts, response status, input/output/reasoning tokens, and actionable failure reason.
- Daily authority selection is freshness-aware and idempotent; candidate storage is deterministically deduplicated.
- The pump is metadata-only and legacy `pump-timer` messages cannot call a provider.
- Validated candidates route independently to the Notary public corpus or technical source-trail repository.
- Public feeds contain sanitized typed candidates only; raw runs remain private.
- Retention settings are visible but deletion is not implemented or executed.

## Review And Live Boundary

The code must be reviewed, merged, and separately deployed before the live Function changes. Destination draft-PR workflows also remain blocked until their repository Actions policy permits `GITHUB_TOKEN` pull-request creation. This repair records and fails closed at that boundary; it does not change the protected repository setting.
