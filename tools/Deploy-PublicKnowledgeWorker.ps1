param(
    [string] $ResourceGroup = "NG-PUBLIC-KNOWLEDGE",
    [string] $FunctionAppName = "ng-public-knowledge-func-2026",
    [string] $Configuration = "Release",
    [string] $PublishProfilePath = "",
    [string] $PublishProfileXml = $env:PUBLIC_KNOWLEDGE_PUBLISH_PROFILE,
    [int] $DeploymentPollTimeoutSeconds = 300,
    [int] $DeploymentPollIntervalSeconds = 5
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "NotaryGeek.PublicKnowledge.Worker\NotaryGeek.PublicKnowledge.Worker.csproj"
$publishDir = Join-Path $repoRoot "artifacts\publish"
$zipPath = Join-Path $repoRoot "artifacts\notary-geek-public-knowledge-worker.zip"

if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path (Split-Path $zipPath -Parent) | Out-Null

dotnet publish $projectPath -c $Configuration -o $publishDir

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

if (-not [string]::IsNullOrWhiteSpace($PublishProfilePath)) {
    $PublishProfileXml = Get-Content -LiteralPath $PublishProfilePath -Raw
}

if (-not [string]::IsNullOrWhiteSpace($PublishProfileXml)) {
    [xml] $publishProfile = $PublishProfileXml
    $profile = @($publishProfile.publishData.publishProfile) |
        Where-Object { $_.publishMethod -eq "ZipDeploy" } |
        Select-Object -First 1

    if ($null -eq $profile) {
        $profile = @($publishProfile.publishData.publishProfile) |
            Where-Object { $_.publishMethod -eq "MSDeploy" } |
            Select-Object -First 1
    }

    if ($null -eq $profile) {
        throw "No ZipDeploy or MSDeploy profile was found in the publish profile XML."
    }

    $publishUrl = [string] $profile.publishUrl
    if ($publishUrl.StartsWith("http", [StringComparison]::OrdinalIgnoreCase)) {
        $scmHost = ([Uri] $publishUrl).Host
    }
    else {
        $scmHost = ($publishUrl -split "/")[0]
        $scmHost = ($scmHost -split ":")[0]
    }

    if ([string]::IsNullOrWhiteSpace($scmHost) -or $scmHost.IndexOf(".scm.", [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Could not determine SCM host from publishUrl '$publishUrl'."
    }

    $username = [string] $profile.userName
    $password = [string] $profile.userPWD
    if ([string]::IsNullOrWhiteSpace($username) -or [string]::IsNullOrWhiteSpace($password)) {
        throw "Publish profile did not include deployment credentials."
    }

    $basicAuth = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${username}:$password"))
    Write-Host "Deploying package to $scmHost with Kudu ZIP Deploy."
    try {
        $zipDeployUri = "https://$scmHost/api/zipdeploy?isAsync=true"
        $zipDeployParams = @{
            Uri = $zipDeployUri
            Method = "Post"
            Headers = @{ Authorization = "Basic $basicAuth" }
            ContentType = "application/zip"
            InFile = $zipPath
            TimeoutSec = 300
        }

        if ((Get-Command Invoke-WebRequest).Parameters.ContainsKey("UseBasicParsing")) {
            $zipDeployParams.UseBasicParsing = $true
        }

        $submitResponse = Invoke-WebRequest @zipDeployParams
        $deploymentStatusUri = [string] $submitResponse.Headers["Location"]
        if ([string]::IsNullOrWhiteSpace($deploymentStatusUri)) {
            $deploymentStatusUri = "https://$scmHost/api/deployments/latest"
        }
        elseif ($deploymentStatusUri.StartsWith("/", [StringComparison]::Ordinal)) {
            $deploymentStatusUri = "https://$scmHost$deploymentStatusUri"
        }

        Write-Host "Kudu deployment accepted. Polling $deploymentStatusUri"
        $deadline = (Get-Date).AddSeconds([Math]::Max(30, $DeploymentPollTimeoutSeconds))
        $lastStatusText = $null
        $deployment = $null

        do {
            Start-Sleep -Seconds ([Math]::Max(1, $DeploymentPollIntervalSeconds))
            $deployment = Invoke-RestMethod `
                -Uri $deploymentStatusUri `
                -Method Get `
                -Headers @{ Authorization = "Basic $basicAuth" } `
                -TimeoutSec 60

            $status = if ($null -ne $deployment.status) { [int] $deployment.status } else { -1 }
            $statusText = [string] $deployment.status_text
            $message = [string] $deployment.message
            $progress = [string] $deployment.progress
            $display = "status=$status"
            if (-not [string]::IsNullOrWhiteSpace($statusText)) { $display += "; statusText=$statusText" }
            if (-not [string]::IsNullOrWhiteSpace($message)) { $display += "; message=$message" }
            if (-not [string]::IsNullOrWhiteSpace($progress)) { $display += "; progress=$progress" }

            if ($display -ne $lastStatusText) {
                Write-Host "Kudu deployment: $display"
                $lastStatusText = $display
            }

            $complete = $false
            if ($deployment.PSObject.Properties.Name -contains "complete") {
                $complete = [bool] $deployment.complete
            }
            elseif ($status -eq 3 -or $status -eq 4) {
                $complete = $true
            }

            if ($complete) {
                if ($status -eq 4) {
                    Write-Host "Kudu deployment completed successfully."
                    break
                }

                throw "Kudu deployment completed with non-success status $status. Review deployment id '$($deployment.id)' on $scmHost."
            }
        }
        while ((Get-Date) -lt $deadline)

        if ($null -eq $deployment -or ((Get-Date) -ge $deadline -and [int] $deployment.status -ne 4)) {
            throw "Timed out waiting for Kudu deployment to finish after $DeploymentPollTimeoutSeconds seconds. Review $deploymentStatusUri."
        }
    }
    catch {
        $statusCode = $null
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            $statusCode = [int] $_.Exception.Response.StatusCode
        }

        if ($statusCode -eq 401) {
            throw "Kudu ZIP Deploy returned 401 Unauthorized. Enable SCM basic publishing credentials for the Function App or download a fresh publish profile, then rerun this script."
        }

        throw
    }
}
else {
    $az = Get-Command az -ErrorAction SilentlyContinue
    if ($null -eq $az) {
        throw "Azure CLI was not found. Install Azure CLI, or pass -PublishProfilePath, or set PUBLIC_KNOWLEDGE_PUBLISH_PROFILE."
    }

    az functionapp deployment source config-zip `
        --resource-group $ResourceGroup `
        --name $FunctionAppName `
        --src $zipPath
}

Write-Host "Deployment completed for $FunctionAppName."
Write-Host "Zip package: $zipPath"
