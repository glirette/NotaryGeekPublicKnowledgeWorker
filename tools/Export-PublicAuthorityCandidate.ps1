param(
    [string] $FeedUrl = "",
    [string] $FeedPath = "",
    [string] $Destination = "glirette/NotaryGeekPublicKnowledgeWorker",
    [string] $OutputRoot = "NotaryGeek.PublicKnowledge.Worker/public-knowledge/source-ingestion/candidates"
)

$ErrorActionPreference = "Stop"

function Assert-ExactProperties {
    param($Object, [string[]] $Allowed, [string] $Label)

    $actual = @($Object.PSObject.Properties.Name)
    $unknown = @($actual | Where-Object { $_ -notin $Allowed })
    $missing = @($Allowed | Where-Object { $_ -notin $actual })
    if ($unknown.Count -gt 0 -or $missing.Count -gt 0) {
        throw "$Label field mismatch. Unknown=[$($unknown -join ',')]; missing=[$($missing -join ',')]."
    }
}

if ([string]::IsNullOrWhiteSpace($FeedUrl) -eq [string]::IsNullOrWhiteSpace($FeedPath)) {
    throw "Provide exactly one of FeedUrl or FeedPath."
}

$feed = if (-not [string]::IsNullOrWhiteSpace($FeedPath)) {
    Get-Content -LiteralPath $FeedPath -Raw | ConvertFrom-Json
} else {
    Invoke-RestMethod -Method Get -Uri $FeedUrl -TimeoutSec 60
}
Assert-ExactProperties $feed @("schema", "generatedAtUtc", "candidates") "feed"
if ($feed.schema -ne "notary-public-authority-candidate-feed/v1") {
    throw "Unexpected feed schema '$($feed.schema)'."
}

$candidates = @($feed.candidates)
if ($candidates.Count -gt 20) {
    throw "Feed exceeds the 20-candidate contract."
}

$candidateFields = @("candidateId", "destination", "topicId", "title", "summary", "reviewedAtUtc", "recheckBeforeUse", "sources", "supports", "doesNotProve", "generatorEvidence")
$sourceFields = @("url", "title", "publisher", "kind", "reviewedAtUtc", "supports")
$evidenceFields = @("provider", "authMode", "model", "runId", "generatedAtUtc", "usage")
$usageFields = @("inputTokens", "outputTokens", "reasoningTokens")
$written = @()

foreach ($candidate in $candidates) {
    Assert-ExactProperties $candidate $candidateFields "candidate"
    if ($candidate.destination -ne $Destination) { throw "Candidate destination mismatch." }
    if ($candidate.recheckBeforeUse -isnot [bool] -or -not $candidate.recheckBeforeUse) { throw "Candidate must require source recheck." }
    if ([string]::IsNullOrWhiteSpace($candidate.candidateId) -or [string]::IsNullOrWhiteSpace($candidate.topicId)) { throw "Candidate identity is missing." }

    $sources = @($candidate.sources)
    if ($sources.Count -lt 1 -or $sources.Count -gt 12) { throw "Candidate source count is outside 1..12." }
    foreach ($source in $sources) {
        Assert-ExactProperties $source $sourceFields "source"
        $uri = [Uri] $source.url
        if ($uri.Scheme -ne "https") { throw "Candidate source must use HTTPS." }
    }

    Assert-ExactProperties $candidate.generatorEvidence $evidenceFields "generatorEvidence"
    Assert-ExactProperties $candidate.generatorEvidence.usage $usageFields "usage"
    if ($candidate.generatorEvidence.provider -ne "openai" -or $candidate.generatorEvidence.authMode -ne "dedicated_public_source_key") {
        throw "Candidate generator evidence does not use the dedicated public-source OpenAI lane."
    }

    $safeTopic = ($candidate.topicId.ToLowerInvariant() -replace '[^a-z0-9_-]', '-')
    $safeId = ($candidate.candidateId.ToLowerInvariant() -replace '[^a-f0-9]', '')
    if ($safeId.Length -lt 12) { throw "Candidate ID is not a usable SHA-256 identifier." }
    $path = Join-Path $OutputRoot "$safeTopic-$($safeId.Substring(0, 12)).json"
    $parent = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    $candidate | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $path -Encoding utf8
    $written += $path
}

$written
