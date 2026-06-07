param(
    [string] $ResourceGroup = "NG-PUBLIC-KNOWLEDGE",
    [string] $FunctionAppName = "ng-public-knowledge-func-2026",
    [string] $Configuration = "Release",
    [string] $PublishProfilePath = "",
    [string] $PublishProfileXml = $env:PUBLIC_KNOWLEDGE_PUBLISH_PROFILE
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
        Invoke-RestMethod `
            -Uri "https://$scmHost/api/zipdeploy" `
            -Method Post `
            -Headers @{ Authorization = "Basic $basicAuth" } `
            -ContentType "application/zip" `
            -InFile $zipPath `
            -TimeoutSec 300 | Out-Null
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

Write-Host "Deployment submitted to $FunctionAppName."
Write-Host "Zip package: $zipPath"
