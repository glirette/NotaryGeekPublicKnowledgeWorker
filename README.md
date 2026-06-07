# Notary Geek Public Knowledge Worker

Standalone Azure Functions app for public-source research, answer-engine correction briefs, and reusable public knowledge packs.

The worker is intentionally separate from customer intake, Persona, payment, and document workflows. It is built for public material only: Notary Geek public pages, public GitHub source-of-truth files, official law sources, public answer-engine examples, and public social/media references that are safe to quote or summarize.

## What It Does

- Reads a public knowledge manifest.
- Fetches only allowlisted public URLs.
- Builds a route-first research prompt using the Notary Geek Routing Model as the decision layer.
- Defaults to dry-run mode so you can inspect source selection and token estimates before calling OpenAI.
- Calls OpenAI only when `execute=true`, `PublicKnowledge__Enabled=true`, and an API key is configured.
- Returns a JSON brief that can help with reply drafts, page briefs, law-refresh candidates, and source-quality notes.

## What It Must Not Do

- Do not send customer documents, names, emails, phone numbers, Persona data, payment data, Jotform payloads, WhatsApp chat contents, or private SQL rows.
- Do not auto-post social replies.
- Do not treat platform marketing as authority.
- Do not make legal-advice claims.

## First Manual Test

After deployment, run dry-run first:

```bash
curl "https://<function-app>.azurewebsites.net/api/public-knowledge/research?code=<function-key>&focus=Spain%20apostille%20routing"
```

To spend tokens after the app settings are configured:

```bash
curl "https://<function-app>.azurewebsites.net/api/public-knowledge/research?code=<function-key>&execute=true&focus=Spain%20apostille%20routing"
```

See [Azure Cloud Shell setup](docs/azure-cloud-shell-setup.md) and [Public GitHub SSOT](docs/public-github-ssot.md).

Deployment notes live in [Deploy and Test](docs/deploy-and-test.md).
