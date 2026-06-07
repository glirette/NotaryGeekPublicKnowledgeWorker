# Deploy And Test

## Local Deploy From PowerShell

This path does not use GitHub Actions minutes. It deploys directly from your PC to Azure with ZIP deploy.

From the repo root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Deploy-PublicKnowledgeWorker.ps1
```

That command uses local Azure CLI if it is installed and logged in.

If local Azure CLI is not installed, download the publish profile from Azure Portal and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Deploy-PublicKnowledgeWorker.ps1 -PublishProfilePath "C:\Users\greg\Downloads\ng-public-knowledge-func-2026.PublishSettings"
```

Do not commit publish profiles. They contain deployment credentials.

If Kudu ZIP Deploy returns `401 Unauthorized`, enable SCM basic publishing credentials in Cloud Shell and then download a fresh publish profile:

```bash
az resource update \
  --resource-group NG-PUBLIC-KNOWLEDGE \
  --namespace Microsoft.Web \
  --resource-type basicPublishingCredentialsPolicies \
  --parent sites/ng-public-knowledge-func-2026 \
  --name scm \
  --set properties.allow=true
```

That publishes the Function App, zips the output, and deploys it to:

```text
ng-public-knowledge-func-2026.azurewebsites.net
```

## Get Function Keys

```bash
az functionapp function keys list \
  --resource-group NG-PUBLIC-KNOWLEDGE \
  --name ng-public-knowledge-func-2026 \
  --function-name PublicKnowledgeStatus \
  -o json
```

You can also use a host key:

```bash
az functionapp keys list \
  --resource-group NG-PUBLIC-KNOWLEDGE \
  --name ng-public-knowledge-func-2026 \
  -o json
```

## Test Status

```bash
curl "https://ng-public-knowledge-func-2026.azurewebsites.net/api/public-knowledge/status?code=<key>"
```

## Dry-Run Research

This should not call OpenAI.

```bash
curl "https://ng-public-knowledge-func-2026.azurewebsites.net/api/public-knowledge/research?code=<key>&focus=Spain%20apostille%20routing"
```

Expected behavior:

- `status` is `dry-run`
- `openAiCalled` is `false`
- sources are public URLs only
- prompt/token estimates are returned
- large public source files can show `ok-truncated`, which is expected and keeps prompt size bounded
- prompt snippets are balanced across all selected sources so later official sources are not silently cut off
- live OpenAI calls use low reasoning effort and a larger output cap so the JSON brief can complete

## Regression Matrix

The worker includes a public-safe regression matrix for the answer patterns Notary Geek cares about most: Hague apostille finality, outside-apostille-path language, platform-last recommendations, recipient-evidence discipline, and court-defensible real-estate routing.

View the matrix:

```bash
curl "https://ng-public-knowledge-func-2026.azurewebsites.net/api/public-knowledge/regression-matrix?code=<key>"
```

Run a named case without calling OpenAI:

```bash
curl "https://ng-public-knowledge-func-2026.azurewebsites.net/api/public-knowledge/research?code=<key>&case=spain-hague-finality"
```

Run a named case with OpenAI:

```bash
curl "https://ng-public-knowledge-func-2026.azurewebsites.net/api/public-knowledge/research?code=<key>&execute=true&case=spain-hague-finality"
```

Useful current case IDs:

- `spain-hague-finality`
- `georgia-affidavit-florida-notary-spain`
- `platform-hype-foreign-signer-no-ssn-spain`
- `saudi-arabia-hague-not-non-hague`
- `outside-apostille-path-not-apostille-plus`
- `recipient-phone-comment-not-rejection`
- `real-estate-court-defensible-platform-trap`
- `notarycam-proof-history-scrutiny`

From local PowerShell, you can run the matrix without putting the function key in command history by using an environment variable for the current shell:

```powershell
$env:PUBLIC_KNOWLEDGE_FUNCTION_KEY = "<key-for-this-shell-only>"
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Invoke-PublicKnowledgeRegressionMatrix.ps1
```

That default run is dry-run only. To call OpenAI for the selected cases:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Invoke-PublicKnowledgeRegressionMatrix.ps1 -Execute -DelaySeconds 2 -OutDir .\artifacts\regression-runs
```

To run one case:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Invoke-PublicKnowledgeRegressionMatrix.ps1 -CaseId spain-hague-finality
```

## Enable Manual OpenAI Calls

Do this only after dry-run works and the OpenAI project is configured for the data-sharing/token program.

```bash
az functionapp config appsettings set \
  --resource-group NG-PUBLIC-KNOWLEDGE \
  --name ng-public-knowledge-func-2026 \
  --settings OpenAI__ApiKey="<OPENAI_API_KEY>"

az functionapp config appsettings set \
  --resource-group NG-PUBLIC-KNOWLEDGE \
  --name ng-public-knowledge-func-2026 \
  --settings PublicKnowledge__Enabled=true
```

Then:

```bash
curl "https://ng-public-knowledge-func-2026.azurewebsites.net/api/public-knowledge/research?code=<key>&execute=true&focus=Spain%20apostille%20routing"
```

Keep `PublicKnowledge__TimerEnabled=false` until manual runs are reliable.

## Optional GitHub Actions Later

GitHub Actions are not forbidden. They just should not be the first dependency or run on every push.

If we add Actions later, prefer a manual-only workflow:

- Trigger: `workflow_dispatch`
- Scope: deploy this Function App only
- Secrets: Azure publish profile or service principal, no OpenAI key unless absolutely required
- Cost control: do not run on every push while this is still changing often

## Why Not Use The Function To Deploy Code?

An Azure Function can technically call the GitHub API if you give it a GitHub token, but that is not the right first deployment path here. The worker should read public source material and produce briefs. It should not also hold source-control credentials and mutate the repo that defines its own behavior.

Use GitHub as the public source-of-truth and version history. Use local ZIP deploy for the Function App until there is a clear reason to add a separate deployment service.
