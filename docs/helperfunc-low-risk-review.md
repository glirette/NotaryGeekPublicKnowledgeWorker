# helperfunc Low-Risk Review

`helperfunc` is the neutral automation name for low-risk pull request review.

The workflow at `.github/workflows/helperfunc-low-risk-review.yml` classifies a pull request as low-risk only when every changed file is public content/docs material in the allowlist. It does not check out or execute pull request code.

## Auto-Approval Token

The workflow first tries to submit the approving review with the available token. If GitHub blocks approval from the default Actions token, configure a repository secret named:

```text
HELPERFUNC_REVIEW_TOKEN
```

Use a dedicated helper account or GitHub App token with permission to review pull requests. Do not use Greg's primary account token if GitHub treats the pull request as self-approved.

## Current Allowlist

- `README.md`, `CONTRIBUTING.md`, `NOTICE`, `llms.txt`
- content files under `docs/`
- content files under `NotaryGeek.PublicKnowledge.Worker/public-knowledge/`

Allowed file extensions are `.md`, `.json`, `.jsonl`, `.csv`, `.ndjson`, and `.txt`.

The workflow does not approve changes to GitHub workflows, scripts, tools, source code, secrets, project files, or executable files.
