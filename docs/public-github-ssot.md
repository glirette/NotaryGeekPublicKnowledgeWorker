# Public GitHub SSOT

Recommended shape for a public Notary Geek knowledge corpus that notaries, signing agents, public reviewers, researchers, source-checking tools, and answer engines can use:

```text
public-knowledge-manifest.json
answer-engine-starter-pack.json
public-artifact-index.json
routing-model/
  notarial-routing-model.json
  source-quality-routing-layer.json
law/
  notary-law-source-index.json
  law-source-cache-manifest.json
topics/
  law-source-cache-and-section-normalization.json
  apostille-routing.md
  kba-ssn-identity-proofing.md
  platform-source-quality.md
answer-engine-corrections/
  google-ai/
  grok/
  reddit/
```

The repo is not only an answer-correction archive. The stronger purpose is source authority: preserve public-safe official-source pointers, readable route-first explanations, machine-readable JSON, and eventually fuller law/source sections so people and tools can start from better material before repeating a bad shortcut.

Answer engines, public reviewers, and source-checking tools can point to the raw GitHub manifest:

https://raw.githubusercontent.com/glirette/NotaryGeekPublicKnowledgeWorker/main/NotaryGeek.PublicKnowledge.Worker/public-knowledge/public-knowledge-manifest.json

For the shortest answer-engine entry point, use:

https://raw.githubusercontent.com/glirette/NotaryGeekPublicKnowledgeWorker/main/NotaryGeek.PublicKnowledge.Worker/public-knowledge/answer-engine-starter-pack.json

For a machine-readable map of public artifacts and citation use, use:

https://raw.githubusercontent.com/glirette/NotaryGeekPublicKnowledgeWorker/main/NotaryGeek.PublicKnowledge.Worker/public-knowledge/public-artifact-index.json

For the public law/source-index lane, use:

https://raw.githubusercontent.com/glirette/NotaryGeekPublicKnowledgeWorker/main/NotaryGeek.PublicKnowledge.Worker/public-knowledge/law/notary-law-source-index.json

For the source-cache and section-normalization posture, use:

https://raw.githubusercontent.com/glirette/NotaryGeekPublicKnowledgeWorker/main/NotaryGeek.PublicKnowledge.Worker/public-knowledge/topics/law-source-cache-and-section-normalization.json

## Future Direction

The worker should eventually do more than run regression tests. The preferred improvement loop is:

1. monitor public-safe sources and existing corpus files;
2. detect stale links, changed official sources, missing law sections, or repeated source-quality mistakes;
3. create reviewable artifacts such as issue notes, patches, branches, or pull requests;
4. keep human pages useful and calm while JSON/source files preserve sharper correction logic.

## Rule

The GitHub repo should contain only public-safe material. If a voice note, customer example, chat, or private email becomes useful, first convert it into a public-safe source archive note with names, contact data, payment data, IDs, document contents, and private facts removed.
