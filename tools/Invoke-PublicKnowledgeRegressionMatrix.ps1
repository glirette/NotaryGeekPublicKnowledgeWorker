param(
    [string] $BaseUrl = $env:PUBLIC_KNOWLEDGE_BASE_URL,
    [string] $FunctionKey = $env:PUBLIC_KNOWLEDGE_FUNCTION_KEY,
    [string[]] $CaseId = @(),
    [ValidateSet("All", "Core", "DailySourceIngestion", "TechnicalSourceIngestion", "Platform", "Apostille", "Recipient", "NNA")]
    [string] $Batch = "All",
    [ValidateSet("", "Default", "OpenAI", "Straico")]
    [string] $Provider = "",
    [switch] $Execute,
    [int] $DelaySeconds = 0,
    [string] $OutDir = "",
    [switch] $SkipCaseMetadataAssert,
    [switch] $PassThru
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($FunctionKey)) {
    throw "Provide -FunctionKey or set PUBLIC_KNOWLEDGE_FUNCTION_KEY for this shell."
}

if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    throw "Provide -BaseUrl or set PUBLIC_KNOWLEDGE_BASE_URL for this shell."
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

function Get-ProviderCalled {
    param([object] $Value)

    if ($null -ne $Value.providerCalled) {
        return [bool] $Value.providerCalled
    }

    return [bool] $Value.openAiCalled
}

function Get-ProviderName {
    param([object] $Value)

    if ($null -ne $Value.provider -and -not [string]::IsNullOrWhiteSpace([string] $Value.provider)) {
        return [string] $Value.provider
    }

    return ""
}

if (-not [string]::IsNullOrWhiteSpace($OutDir)) {
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
}

$matrixUri = New-PublicKnowledgeUri -Path "/api/public-knowledge/regression-matrix" -Query @{ code = $FunctionKey }
$matrixResponse = Invoke-RestMethod -Uri $matrixUri -Method Get
$cases = @($matrixResponse.matrix.cases)

$batchCaseIds = switch ($Batch) {
    "Core" { @("spain-hague-finality", "georgia-affidavit-florida-notary-spain", "platform-hype-foreign-signer-no-ssn-spain", "outside-apostille-path-not-apostille-plus") }
    "DailySourceIngestion" { @("daily-public-source-ingestion-safety-gates") }
    "TechnicalSourceIngestion" { @("daily-technical-source-ingestion") }
    "Platform" { @("foreign-signer-no-ssn-platform-route-first", "virginia-foreign-signer-network-myth", "commercial-incentive-routing-not-route-authority", "platform-hype-foreign-signer-no-ssn-spain", "notarycam-proof-history-scrutiny", "real-estate-court-defensible-platform-trap", "nna-data-exchange-api-private-credential-rail", "nna-legitimacy-not-legal-authority", "ethical-acceptance-diploma-mill-boundary", "coaching-scam-no-criminal-intent-boundary") }
    "Apostille" { @("spain-hague-finality", "georgia-affidavit-florida-notary-spain", "saudi-arabia-hague-not-non-hague", "outside-apostille-path-not-apostille-plus") }
    "Recipient" { @("recipient-phone-comment-not-rejection", "real-estate-court-defensible-platform-trap") }
    "NNA" { @("nna-data-exchange-api-private-credential-rail", "nna-legitimacy-not-legal-authority", "coaching-scam-no-criminal-intent-boundary", "commercial-incentive-routing-not-route-authority", "ethical-acceptance-diploma-mill-boundary") }
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

    if (-not [string]::IsNullOrWhiteSpace($Provider) -and $Provider -ne "Default") {
        $query.provider = $Provider
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

    $score = $response.regressionScore
    $scoreVerdict = ""
    $mustHoldSummary = ""
    $failureSignalSummary = ""
    if ($null -ne $score) {
        $scoreVerdict = [string] $score.verdict
        $mustHoldSummary = "{0}/{1}" -f $score.mustHoldPassed, $score.mustHoldTotal
        $failureSignalSummary = "{0}/{1}" -f $score.failureSignalsObserved, $score.failureSignalTotal
    }

    $results.Add([pscustomobject]@{
        Case = [string] $case.id
        Ok = [bool] $response.ok
        Status = [string] $response.status
        Provider = Get-ProviderName $response
        ProviderCalled = Get-ProviderCalled $response
        CaseMetadata = $caseMetadataMatches
        Score = $scoreVerdict
        MustHold = $mustHoldSummary
        FailureSignals = $failureSignalSummary
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

if ($PassThru) {
    $results
}
else {
    $results |
        Select-Object `
            Case,
            Ok,
            Status,
            Provider,
            ProviderCalled,
            @{ Name = "Meta"; Expression = { $_.CaseMetadata } },
            Score,
            MustHold,
            @{ Name = "FailSig"; Expression = { $_.FailureSignals } },
            @{ Name = "Src"; Expression = { $_.SourceCount } },
            @{ Name = "Warn"; Expression = { $_.WarningCount } },
            @{ Name = "Err"; Expression = { $_.ErrorCount } },
            @{ Name = "Tokens"; Expression = { $_.TotalTokens } } |
        Format-Table -AutoSize
}

if ($metadataFailures.Count -gt 0) {
    throw "Regression case metadata check failed: $([string]::Join(' ', $metadataFailures))"
}
