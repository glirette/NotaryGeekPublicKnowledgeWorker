param(
    [string] $BaseUrl = "https://ng-public-knowledge-func-2026.azurewebsites.net",
    [string] $FunctionKey = $env:PUBLIC_KNOWLEDGE_FUNCTION_KEY,
    [string[]] $CaseId = @(),
    [ValidateSet("All", "Core", "Platform", "Apostille", "Recipient")]
    [string] $Batch = "All",
    [switch] $Execute,
    [int] $DelaySeconds = 0,
    [string] $OutDir = "",
    [switch] $SkipCaseMetadataAssert
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

function Get-ProviderTotalTokens {
    param([string] $UsageJson)

    if ([string]::IsNullOrWhiteSpace($UsageJson)) {
        return $null
    }

    try {
        $usage = $UsageJson | ConvertFrom-Json
        return $usage.total_tokens
    }
    catch {
        return $null
    }
}

if (-not [string]::IsNullOrWhiteSpace($OutDir)) {
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
}

$matrixUri = New-PublicKnowledgeUri -Path "/api/public-knowledge/regression-matrix" -Query @{ code = $FunctionKey }
$matrixResponse = Invoke-RestMethod -Uri $matrixUri -Method Get
$cases = @($matrixResponse.matrix.cases)

$batchCaseIds = switch ($Batch) {
    "Core" { @("spain-hague-finality", "georgia-affidavit-florida-notary-spain", "platform-hype-foreign-signer-no-ssn-spain", "outside-apostille-path-not-apostille-plus") }
    "Platform" { @("platform-hype-foreign-signer-no-ssn-spain", "notarycam-proof-history-scrutiny", "real-estate-court-defensible-platform-trap") }
    "Apostille" { @("spain-hague-finality", "georgia-affidavit-florida-notary-spain", "saudi-arabia-hague-not-non-hague", "outside-apostille-path-not-apostille-plus") }
    "Recipient" { @("recipient-phone-comment-not-rejection", "real-estate-court-defensible-platform-trap") }
    default { @() }
}

if ($CaseId.Count -eq 0 -and $batchCaseIds.Count -gt 0) {
    $CaseId = $batchCaseIds
}

if ($CaseId.Count -gt 0) {
    $wanted = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::OrdinalIgnoreCase)
    foreach ($item in $CaseId) {
        [void] $wanted.Add($item)
    }

    $cases = @($cases | Where-Object { $wanted.Contains([string] $_.id) })
}

if ($cases.Count -eq 0) {
    throw "No regression cases matched. Available cases: $([string]::Join(', ', @($matrixResponse.matrix.cases | ForEach-Object { $_.id })))"
}

$results = New-Object System.Collections.Generic.List[object]
$metadataFailures = New-Object System.Collections.Generic.List[string]
foreach ($case in $cases) {
    $query = @{
        code = $FunctionKey
        case = [string] $case.id
    }

    if ($Execute) {
        $query.execute = "true"
    }

    $uri = New-PublicKnowledgeUri -Path "/api/public-knowledge/research" -Query $query
    Write-Host "Running $($case.id) (execute=$($Execute.IsPresent))"
    $response = Invoke-RestMethod -Uri $uri -Method Get
    $responseCaseId = [string] $response.regressionCaseId
    $responseCaseObjectId = [string] $response.regressionCase.id
    $caseMetadataMatches =
        $responseCaseId.Equals([string] $case.id, [StringComparison]::OrdinalIgnoreCase) -and
        $responseCaseObjectId.Equals([string] $case.id, [StringComparison]::OrdinalIgnoreCase)

    if (-not [string]::IsNullOrWhiteSpace($OutDir)) {
        $safeName = ([string] $case.id) -replace "[^a-zA-Z0-9._-]", "-"
        $response | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath (Join-Path $OutDir "$safeName.json") -Encoding UTF8
    }

    $results.Add([pscustomobject]@{
        Case = [string] $case.id
        Ok = [bool] $response.ok
        Status = [string] $response.status
        OpenAiCalled = [bool] $response.openAiCalled
        CaseMetadata = $caseMetadataMatches
        SourceCount = [int] $response.sourceCount
        WarningCount = @($response.warnings).Count
        ErrorCount = @($response.errors).Count
        TotalTokens = Get-ProviderTotalTokens -UsageJson ([string] $response.providerUsageJson)
    }) | Out-Null

    if (-not $SkipCaseMetadataAssert -and -not $caseMetadataMatches) {
        $metadataFailures.Add("Case '$($case.id)' returned RegressionCaseId='$responseCaseId' and RegressionCase.Id='$responseCaseObjectId'.") | Out-Null
    }

    if ($DelaySeconds -gt 0) {
        Start-Sleep -Seconds $DelaySeconds
    }
}

$results

if ($metadataFailures.Count -gt 0) {
    throw "Regression case metadata check failed: $([string]::Join(' ', $metadataFailures))"
}
