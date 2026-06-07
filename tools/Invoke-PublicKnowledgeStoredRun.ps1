param(
    [string] $BaseUrl = "https://ng-public-knowledge-func-2026.azurewebsites.net",
    [string] $FunctionKey = $env:PUBLIC_KNOWLEDGE_FUNCTION_KEY,
    [ValidateSet("Status", "Latest", "ExportIndex", "RunBatch")]
    [string] $Command = "Latest",
    [ValidateSet("All", "Core", "Platform", "Apostille", "Recipient")]
    [string] $Batch = "Core",
    [string] $CaseId = "",
    [switch] $DryRun,
    [switch] $NoSave,
    [string] $OutFile = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($FunctionKey)) {
    throw "Provide -FunctionKey or set PUBLIC_KNOWLEDGE_FUNCTION_KEY for this shell."
}

function New-PublicKnowledgeUri {
    param(
        [string] $Path,
        [hashtable] $Query
    )

    $builder = [System.UriBuilder]::new($BaseUrl.TrimEnd("/") + $Path)
    $pairs = New-Object System.Collections.Generic.List[string]
    foreach ($key in $Query.Keys) {
        $value = [string] $Query[$key]
        if ([string]::IsNullOrWhiteSpace($value)) {
            continue
        }

        $pairs.Add(("{0}={1}" -f [Uri]::EscapeDataString($key), [Uri]::EscapeDataString($value)))
    }

    $builder.Query = [string]::Join("&", $pairs)
    return $builder.Uri.AbsoluteUri
}

$query = @{ code = $FunctionKey }
$path = switch ($Command) {
    "Status" { "/api/public-knowledge/status" }
    "Latest" {
        if (-not [string]::IsNullOrWhiteSpace($CaseId)) {
            $query.case = $CaseId
        }

        "/api/public-knowledge/runs/latest"
    }
    "ExportIndex" {
        if ($NoSave) {
            $query.save = "false"
        }

        "/api/public-knowledge/runs/export-index"
    }
    "RunBatch" {
        $query.execute = if ($DryRun) { "false" } else { "true" }
        if (-not [string]::IsNullOrWhiteSpace($CaseId)) {
            $query.case = $CaseId
        }
        else {
            $query.batch = $Batch
        }

        "/api/public-knowledge/runs/run-batch"
    }
}

$uri = New-PublicKnowledgeUri -Path $path -Query $query
$response = Invoke-RestMethod -Uri $uri -Method Get

if (-not [string]::IsNullOrWhiteSpace($OutFile)) {
    $directory = Split-Path -Path $OutFile -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $response | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $OutFile -Encoding UTF8
}

switch ($Command) {
    "Status" {
        $response
    }
    "Latest" {
        if ($response.latestRuns) {
            $response.latestRuns | Select-Object CaseId, StoredAtUtc, Trigger, Batch, Ok, Status, OpenAiCalled, SourceCount, WarningCount, ErrorCount, BlobName, LatestBlobName
        }
        elseif ($response.latest) {
            $response.latest
        }
        else {
            $response
        }
    }
    "ExportIndex" {
        [pscustomobject]@{
            Ok = [bool] $response.ok
            Saved = [bool] $response.saved
            GeneratedAtUtc = $response.index.generatedAtUtc
            RunCount = $response.index.runCount
            LatestIndexBlobName = $response.index.latestIndexBlobName
        }
    }
    "RunBatch" {
        $response.receipts | Select-Object CaseId, Ok, Status, OpenAiCalled, SourceCount, WarningCount, ErrorCount, BlobName, LatestBlobName
    }
}
