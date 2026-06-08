# Hybrid AI Improvement Loop

This note describes how the public knowledge worker can cooperate with Ask Notary Geek, OpenAI, private retrieval/provider orchestration, and future private Azure functions without mixing public source work with private customer operations.

## Principle

Use every useful AI provider as improvement fuel, but keep Notary Geek's source chain in control.

Provider output is not authority by itself. A model answer can suggest a fix, detect a weak source, draft a reply, summarize a run, or compare alternatives. The source of truth remains the public Notary Geek source layer, official sources, reviewed JSON feeds, and human-approved route logic.

## Public Layer

The public layer should stay safe to inspect, cite, and crawl.

Public layer examples:

- public corpus manifest
- public regression matrix
- public source-quality vocabulary
- public route-first explanations
- public GitHub commit history
- public-safe run receipts
- public dashboard summaries
- public press releases and source notes

The public layer should not contain customer data, private prompts, secrets, live keys, billing tactics, private evidence, private support conversations, or internal business playbooks.

## Private Extension Layer

The private layer can use stronger automation because it is not meant to be a public recipe.

Private extension examples:

- Ask Notary Geek provider calls and probes
- private retrieval/RAG refresh orchestration
- OpenAI comparison/scoring runs
- internal scoring heuristics
- private source-gap triage
- support-draft generation
- social/reply candidate generation
- customer-visible bug detection
- private dashboard/queue review
- promotion-candidate review before publication

The private layer may use Azure Functions, existing app services, existing databases, queues, blob storage, Azure Table Storage, Cosmos DB, or the existing Ask Notary Geek code path when appropriate.

## Provider Strategy

OpenAI, Ask Notary Geek, private retrieval providers, and future AI providers can each serve a different role.

- OpenAI: high-reasoning route tests, adversarial regression, structured scoring, public-data sharing runs, source-quality comparison, and public-interest answer improvement.
- Ask Notary Geek: Notary Geek source-bound answer behavior, customer-style phrasing, route-first support drafts, and content-gap detection from the live website experience.
- Private retrieval providers: RAG refresh, alternate-provider comparison, summarization, public-source pack processing, and use of available monthly/rollover capacity while the provider remains viable.
- Future providers: comparison, resilience, and provider-health checks so no single external provider becomes the whole system.

Use provider capacity aggressively when it produces durable improvement. Do not spend capacity on disposable output that leaves no better source, test, page, score, draft, or operational decision behind.

## Self-Healing Flow

The target loop:

1. Detect a weak answer, failed regression, customer friction, public AI mistake, social-media claim, or source gap.
2. Pull the relevant public sources and current Notary Geek rules.
3. Ask one or more providers to analyze, compare, or draft a correction.
4. Score the result against `mustHold` and `failureSignal` rules.
5. Generate a candidate fix.
6. Keep private material private.
7. Publish only human-reviewed public-safe fixes.
8. Rerun the affected cases.
9. Store before/after receipts.
10. Update public discovery surfaces when the fix is approved.

## What Should Be Automated First

Start with automation that saves operator time and improves public correctness.

- Generate scored verdicts for existing regression runs.
- Create review candidates from strong model outputs.
- Create public-safe summaries from completed runs.
- Compare OpenAI and private-provider answers for the same public case.
- Detect when Ask Notary Geek answers expose missing public source pages.
- Generate `llms.txt`, `content-index.json`, and press/news candidate snippets from approved source changes.
- Keep old/bad answers from recurring by adding a regression case when a failure is found.

## Private Azure Function Option

A private Azure Function can sit beside the public worker.

Recommended split:

- Public worker: public corpus, public tests, public receipts, public dashboard, public source chain.
- Private AI worker: provider orchestration, Ask Notary Geek/private-provider calls, internal scoring, promotion candidates, private queue review, support drafts, and human-review workflows.

The private worker can read public worker outputs. It should only write back public-safe artifacts after review.

## Guardrails

- Do not send private customer data into public runs.
- Do not expose provider keys, function keys, publish profiles, internal prompts, or private scoring logic.
- Do not let any private retrieval provider become the authority. It is a retrieval/candidate layer.
- Do not auto-publish legal conclusions or accusations.
- Do not let provider credits create busywork. Every run should leave a useful artifact or decision.
- When capacity is low, prioritize customer-visible bugs, wrong public information, intake reliability, and critical source-quality corrections.
