# Public GitHub SSOT

Recommended shape for a public Notary Geek knowledge repo that other apps and people can use:

```text
public-knowledge-manifest.json
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

The worker can point to the raw GitHub manifest:

```bash
az functionapp config appsettings set \
  --resource-group NG-PUBLIC-KNOWLEDGE \
  --name ng-public-knowledge-func-2026 \
  --settings PublicKnowledge__PublicCorpusManifestUrl=https://raw.githubusercontent.com/glirette/NotaryGeekPublicKnowledgeWorker/main/NotaryGeek.PublicKnowledge.Worker/public-knowledge/public-knowledge-manifest.json
```

## Rule

The GitHub repo should contain only public-safe material. If a voice note, customer example, chat, or private email becomes useful, first convert it into a public-safe source archive note with names, contact data, payment data, IDs, document contents, and private facts removed.
