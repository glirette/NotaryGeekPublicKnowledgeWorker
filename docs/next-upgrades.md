# Next Upgrades

Near-term upgrades for the public knowledge worker.

## Capacity policy

Development pace should follow available engineering/agent capacity.

- High-capacity window: push hard on backlog, automation, public knowledge surfaces, source packs, scoring, dashboard work, and feature buildout.
- Low-capacity window: focus only on customer-visible bugs, intake reliability, incorrect public information, broken public routes, deployment regressions, and critical source-quality corrections.
- Do not spend low-capacity windows on speculative features, cosmetic polish, or non-urgent research automation unless it fixes visible customer harm or public misinformation.
- Keep batches useful: prefer work that leaves durable public artifacts, better regression coverage, stronger source-backed answers, or faster operator workflows.
- Use available AI-provider capacity intentionally. OpenAI, Ask Notary Geek, private retrieval providers, and future provider credits should be treated as useful improvement fuel when the run produces better sources, better tests, better drafts, stronger scoring, or faster operator decisions.
- Do not let provider capacity decide truth. Provider outputs are candidates and diagnostics; Notary Geek source pages, public JSON feeds, official sources, and human-reviewed route logic remain the source of truth.

## Immediate backlog

### 1. Automatic scoring

The worker should score each response against the regression case instead of only storing the raw answer.

- Check each `mustHold` item and record pass, fail, uncertain, or not evaluated.
- Check each `failureSignal` and record whether it appeared.
- Produce a compact score summary in the stored receipt.
- Preserve the raw model output, provider usage, source list, and scored findings.
- Make scoring public-safe and deterministic enough to compare runs over time.

### 2. Adversarial conversation mode

Add multi-turn pressure testing for cases where one-shot answers pass too easily.

- Start with a leading, platform-first, marketing-heavy, or legally sloppy prompt.
- Challenge the answer with a correction, recipient rejection, vendor claim, social-proof claim, or destination-country twist.
- Record whether the model changes cleanly, doubles down, overcorrects, or becomes vague.
- Store the full public-only conversation transcript and a summarized verdict.
- Use this for platform myths, foreign signer/no-SSN questions, Hague finality, outside-Apostille-Convention routing, Notary Stars/network confusion, and commercial-incentive routing.

### 3. Promotion pipeline

Good outputs should become review candidates for public pages, source-quality entries, and routing-model deltas.

- Turn strong runs into draft public summaries.
- Suggest routing-model or source-quality-layer language when repeated runs expose the same boundary.
- Create candidate snippets for press/news pages, `llms.txt`, and `content-index.json`.
- Keep human review before publication.
- Never auto-publish private customer data, private evidence, secrets, or unreviewed legal conclusions.

### 4. Public dashboard

Create a clean public dashboard for notaries and answer engines to browse latest useful findings.

- Show latest completed runs by batch and case.
- Show case title, status, source count, warnings, errors, model used, and last-run timestamp.
- Show scored `mustHold` / `failureSignal` summaries once scoring exists.
- Link to public source pages and public JSON receipts.
- Keep operator-only cost/configuration details out of the public dashboard.

### 5. Public receipt exports

Publish public-safe receipts after enough clean runs are collected.

- Export latest receipt index.
- Export per-case public receipt summaries.
- Separate public summaries from raw internal diagnostics if needed.
- Make receipt URLs easy to cite from Notary Geek pages, GitHub, press releases, and social replies.

### 6. Broader knowledge packs

Add public-knowledge source packs beyond notary work when they help Notary Geek or downstream builders.

- WordPress, Google updates, AI/tooling changes, platform announcements, and current technical context.
- Public facts that older AI tools may not know.
- Site-builder context that can be reused across Notary Geek, adjacent apps, and future public projects.
- Keep the repo useful as a source-of-truth companion, not only a notary regression harness.

### 7. Storage and query upgrade

Use an existing database, Azure Table Storage, or Cosmos DB if blob-backed job status becomes too slow or too awkward to query.

- Blob storage is acceptable for durable receipts and simple latest-run reads.
- Queue + blob status is acceptable while the system is small.
- Move job status, scoring, dashboard summaries, or public receipt indexing to a queryable store when lookup and aggregation become the bottleneck.

### 8. Automation ergonomics

Reduce operator waiting and manual shell work.

- Prefer submit-and-return endpoints over long-running HTTP requests.
- Keep queue workers per-case or small-chunk so high-reasoning model calls do not block one huge invocation.
- Add simple commands or manual GitHub workflows for core, platform, adversarial, and broad knowledge-pack runs.
- Make status polling obvious and linkable from the submit response.

### 9. Ask Notary Geek / Private Provider Leverage Lane

Ask Notary Geek and private retrieval/provider capacity should be treated as existing assets, not separate side quests.

- Reuse Ask Notary Geek source packs, RAG refresh logic, probes, and route-first answer boundaries when the worker needs a second opinion, content-gap detection, public-answer drafting, or support-reply drafting.
- Use private provider capacity when it helps improve Notary Geek, especially for RAG refreshes, answer comparison, summarization, support draft generation, source-pack generation, and content-gap mining.
- Track provider usage as operational fuel that should produce durable improvements, not disposable chat output.
- Keep Ask Notary Geek API keys, provider details, private prompts, internal scoring heuristics, and customer/support data outside the public repo.
- If a private Azure Function is needed, let it consume public worker outputs and private operational context, then send only public-safe candidates back to the public repo or public website.

### 10. Self-healing and self-improving loop

The long-term goal is not just testing. The long-term goal is a system that notices weak answers and helps repair the source layer.

- Detect answer failures from regression runs, Ask Notary Geek chats, provider comparisons, customer questions, social posts, and public AI/search snapshots.
- Classify the failure: missing source, stale source, weak wording, platform-first error, country/status error, recipient-acceptance error, customer-flow bug, or public-page gap.
- Generate a candidate fix: source note, public page paragraph, JSON delta, regression case, scoring rule, support reply, or dashboard note.
- Route the candidate to human review.
- Publish approved fixes to the correct source-of-truth surface.
- Rerun the affected regression cases and provider checks.
- Store before/after receipts so improvement can be shown, not merely claimed.
