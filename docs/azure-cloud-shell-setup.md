# Azure Cloud Shell Setup

The first Function App has already been created:

- Resource group: `NG-PUBLIC-KNOWLEDGE`
- Storage account: `ngpublicknowledge2026`
- Function App: `ng-public-knowledge-func-2026`
- Hostname: `ng-public-knowledge-func-2026.azurewebsites.net`
- Runtime: Azure Functions v4, .NET isolated, Windows Consumption
- .NET stack: `netFrameworkVersion = v10.0`
- Public corpus manifest: `https://raw.githubusercontent.com/glirette/NotaryGeekPublicKnowledgeWorker/main/NotaryGeek.PublicKnowledge.Worker/public-knowledge/public-knowledge-manifest.json`

## Existing App Check

```bash
az functionapp show \
  --resource-group NG-PUBLIC-KNOWLEDGE \
  --name ng-public-knowledge-func-2026 \
  --query "{name:name,state:state,httpsOnly:httpsOnly,netFrameworkVersion:siteConfig.netFrameworkVersion,defaultHostName:defaultHostName}" \
  -o table
```

## Baseline Settings

These settings keep OpenAI calls disabled until dry-run tests work.

```bash
az functionapp config appsettings set \
  --resource-group NG-PUBLIC-KNOWLEDGE \
  --name ng-public-knowledge-func-2026 \
  --settings \
    FUNCTIONS_WORKER_RUNTIME=dotnet-isolated \
    FUNCTIONS_EXTENSION_VERSION=~4 \
    PublicKnowledge__Enabled=false \
    PublicKnowledge__TimerEnabled=false \
    PublicKnowledge__PublicBaseUrl=https://notary.cx \
    PublicKnowledge__PublicCorpusManifestUrl=https://raw.githubusercontent.com/glirette/NotaryGeekPublicKnowledgeWorker/main/NotaryGeek.PublicKnowledge.Worker/public-knowledge/public-knowledge-manifest.json \
    OpenAI__BaseUrl=https://api.openai.com \
    OpenAI__EndpointPath=/v1/responses \
    OpenAI__Model=gpt-5-mini \
    OpenAI__ReasoningEffort=low
```

Azure redacts values in the appsettings response. `value: null` in the table usually means the CLI is not echoing secrets/settings back, not that the value is missing.

## HTTPS And TLS

For this Azure CLI version, use `az functionapp update --set httpsOnly=true`; `az functionapp config set --https-only true` may fail.

```bash
az functionapp update \
  --resource-group NG-PUBLIC-KNOWLEDGE \
  --name ng-public-knowledge-func-2026 \
  --set httpsOnly=true

az functionapp config set \
  --resource-group NG-PUBLIC-KNOWLEDGE \
  --name ng-public-knowledge-func-2026 \
  --min-tls-version 1.2
```

## Windows .NET 10 Note

The app is Windows Consumption. The failed command below is expected and should not be chased:

```bash
az functionapp config set --linux-fx-version "DOTNET-ISOLATED|10.0"
```

`linuxFxVersion` applies to Linux apps. For this app, the relevant value is `siteConfig.netFrameworkVersion = v10.0`.

## Add OpenAI Key Later

Do not paste the OpenAI key into chat. Add it in Azure Portal, or run this only in Cloud Shell when ready:

```bash
az functionapp config appsettings set \
  --resource-group NG-PUBLIC-KNOWLEDGE \
  --name ng-public-knowledge-func-2026 \
  --settings OpenAI__ApiKey="<OPENAI_API_KEY>"
```

Then enable manual OpenAI calls:

```bash
az functionapp config appsettings set \
  --resource-group NG-PUBLIC-KNOWLEDGE \
  --name ng-public-knowledge-func-2026 \
  --settings PublicKnowledge__Enabled=true
```

Leave `PublicKnowledge__TimerEnabled=false` until the manual execute path has been tested.
