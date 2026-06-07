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

That publishes the Function App, zips the output, sends it to Kudu ZIP Deploy, and polls the Kudu deployment record until it completes or fails. It deploys to:

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

The status response includes storage readiness. `storage.hasConnectionString` should be `true` before enabling timer output persistence.

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

To run a focused execute batch:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Invoke-PublicKnowledgeRegressionMatrix.ps1 -Batch Core -Execute -DelaySeconds 2 -OutDir .\artifacts\regression-runs
```

Available batches are `All`, `Core`, `Platform`, `Apostille`, and `Recipient`.

To run one case:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Invoke-PublicKnowledgeRegressionMatrix.ps1 -CaseId spain-hague-finality
```

The runner also validates the live response envelope. For named cases, `CaseMetadata` should be `True`; if the deployed app omits `RegressionCaseId` or `RegressionCase`, the script throws so stale deployments are easy to catch.

The runner returns PowerShell objects. If the table wraps in a narrow terminal, pipe the result to the shape you need:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Invoke-PublicKnowledgeRegressionMatrix.ps1 -CaseId georgia-affidavit-florida-notary-spain -Execute | Format-List

powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\Invoke-PublicKnowledgeRegressionMatrix.ps1 -CaseId georgia-affidavit-florida-notary-spain -Execute | Select-Object Case,TotalTokens,WarningCount,ErrorCount
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

## Timer Output

The timer is intentionally off until there is a deliberate cadence. When enabled, it runs the configured regression batch, calls OpenAI, and stores each result in the Function App storage container.

Current timer schedule in code: daily at `09:17 UTC`.

Keep the timer hard-disabled:

```bash
az functionapp config appsettings set \
  --resource-group NG-PUBLIC-KNOWLEDGE \
  --name ng-public-knowledge-func-2026 \
  --settings PublicKnowledge__TimerEnabled=false AzureWebJobs.PublicKnowledgeResearchTimer.Disabled=true
```

This keeps both the code-level timer gate and the Azure Functions timer trigger disabled. Manual dry-runs and manual `execute=true` calls still work.

To light it up for the `Core` batch:

```bash
az functionapp config appsettings set \
  --resource-group NG-PUBLIC-KNOWLEDGE \
  --name ng-public-knowledge-func-2026 \
  --settings PublicKnowledge__TimerEnabled=true AzureWebJobs.PublicKnowledgeResearchTimer.Disabled=false PublicKnowledge__TimerBatch=Core PublicKnowledge__OutputStorageConnectionStringSetting=AzureWebJobsStorage PublicKnowledge__OutputContainerName=public-knowledge-runs

az functionapp restart \
  --resource-group NG-PUBLIC-KNOWLEDGE \
  --name ng-public-knowledge-func-2026
```

Inspect latest stored timer/manual outputs:

```bash
curl "https://ng-public-knowledge-func-2026.azurewebsites.net/api/public-knowledge/runs/latest?code=<key>"

curl "https://ng-public-knowledge-func-2026.azurewebsites.net/api/public-knowledge/runs/latest?code=<key>&case=spain-hague-finality"
```

Run and store a batch immediately without waiting for the timer:

```bash
curl "https://ng-public-knowledge-func-2026.azurewebsites.net/api/public-knowledge/runs/run-batch?code=<key>&batch=Core"
```

Run and store one case:

```bash
curl "https://ng-public-knowledge-func-2026.azurewebsites.net/api/public-knowledge/runs/run-batch?code=<key>&case=spain-hague-finality"
```

For a stored no-spend dry-run, add `execute=false`.

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
