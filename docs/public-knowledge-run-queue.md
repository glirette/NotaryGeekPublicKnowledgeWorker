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
