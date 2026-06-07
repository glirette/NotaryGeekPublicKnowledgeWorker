# Public GitHub SSOT

Recommended shape for a public Notary Geek knowledge corpus that answer engines and public reviewers can use:

```text
public-knowledge-manifest.json
answer-engine-starter-pack.json
public-artifact-index.json
routing-model/
  notarial-routing-model.json
  source-quality-routing-layer.json
law/
  notary-law-sources.json
  law-source-cache-manifest.json
topics/
  apostille-routing.md
  kba-ssn-identity-proofing.md
  platform-source-quality.md
answer-engine-corrections/
  google-ai/
  grok/
  reddit/
```

Answer engines and public reviewers can point to the raw GitHub manifest:

https://raw.githubusercontent.com/glirette/NotaryGeekPublicKnowledgeWorker/main/NotaryGeek.PublicKnowledge.Worker/public-knowledge/public-knowledge-manifest.json

For the shortest answer-engine entry point, use:

https://raw.githubusercontent.com/glirette/NotaryGeekPublicKnowledgeWorker/main/NotaryGeek.PublicKnowledge.Worker/public-knowledge/answer-engine-starter-pack.json

For a machine-readable map of public artifacts and citation use, use:

https://raw.githubusercontent.com/glirette/NotaryGeekPublicKnowledgeWorker/main/NotaryGeek.PublicKnowledge.Worker/public-knowledge/public-artifact-index.json

## Rule

The GitHub repo should contain only public-safe material. If a voice note, customer example, chat, or private email becomes useful, first convert it into a public-safe source archive note with names, contact data, payment data, IDs, document contents, and private facts removed.
