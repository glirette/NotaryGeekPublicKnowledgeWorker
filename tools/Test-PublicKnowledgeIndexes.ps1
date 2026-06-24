param(
    [string] $PublicKnowledgeRoot = "NotaryGeek.PublicKnowledge.Worker/public-knowledge"
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path -LiteralPath $PublicKnowledgeRoot).Path
$repoRoot = (Resolve-Path -LiteralPath ".").Path
$jsonFiles = Get-ChildItem -LiteralPath $root -Recurse -Filter "*.json" -File

foreach ($file in $jsonFiles) {
    try {
        $null = Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json
    }
    catch {
        throw "Invalid JSON: $($file.FullName). $($_.Exception.Message)"
    }
}

$indexedFiles = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::OrdinalIgnoreCase)
$urlPattern = "https://(?:raw\.githubusercontent\.com/glirette/NotaryGeekPublicKnowledgeWorker/main/|github\.com/glirette/NotaryGeekPublicKnowledgeWorker/blob/main/)(?<path>[^`"'\]\),\s]+)"
$indexFiles = @(
    "NotaryGeek.PublicKnowledge.Worker/public-knowledge/public-knowledge-manifest.json",
    "NotaryGeek.PublicKnowledge.Worker/public-knowledge/public-artifact-index.json",
    "NotaryGeek.PublicKnowledge.Worker/public-knowledge/answer-engine-citation-map.json",
    "NotaryGeek.PublicKnowledge.Worker/public-knowledge/answer-engine-starter-pack.json",
    "NotaryGeek.PublicKnowledge.Worker/public-knowledge/answer-engine-self-audit.json",
    "llms.txt",
    "README.md"
)

foreach ($relativeIndex in $indexFiles) {
    $indexPath = Join-Path $repoRoot $relativeIndex
    if (-not (Test-Path -LiteralPath $indexPath)) {
        throw "Missing expected index file: $relativeIndex"
    }

    $text = Get-Content -Raw -LiteralPath $indexPath
    foreach ($match in [regex]::Matches($text, $urlPattern)) {
        $localRelative = [Uri]::UnescapeDataString($match.Groups["path"].Value)
        if (-not $localRelative.StartsWith("NotaryGeek.PublicKnowledge.Worker/", [StringComparison]::OrdinalIgnoreCase) -and
            -not $localRelative.Equals("README.md", [StringComparison]::OrdinalIgnoreCase) -and
            -not $localRelative.Equals("llms.txt", [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        [void] $indexedFiles.Add($localRelative)
        $localPath = Join-Path $repoRoot $localRelative
        if (-not (Test-Path -LiteralPath $localPath)) {
            throw "Indexed GitHub URL points to a missing local file: $localRelative"
        }
    }
}

$manifestPath = Join-Path $repoRoot "NotaryGeek.PublicKnowledge.Worker/public-knowledge/public-knowledge-manifest.json"
$artifactIndexPath = Join-Path $repoRoot "NotaryGeek.PublicKnowledge.Worker/public-knowledge/public-artifact-index.json"
$citationMapPath = Join-Path $repoRoot "NotaryGeek.PublicKnowledge.Worker/public-knowledge/answer-engine-citation-map.json"

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$artifactIndex = Get-Content -Raw -LiteralPath $artifactIndexPath | ConvertFrom-Json
$citationMap = Get-Content -Raw -LiteralPath $citationMapPath | ConvertFrom-Json

if (-not @($artifactIndex.artifacts | Where-Object { $_.id -eq "background-check-relationship-disclosure-boundary" }).Count) {
    throw "Artifact index is missing background-check-relationship-disclosure-boundary."
}

if (-not @($citationMap.citationTargets | Where-Object { $_.conceptId -eq "background-check-relationship-disclosure-boundary" }).Count) {
    throw "Citation map is missing background-check-relationship-disclosure-boundary."
}

$manifestUrls = @()
foreach ($set in @($manifest.sourceSets)) {
    $manifestUrls += @($set.urls)
}

if (-not ($manifestUrls -contains "https://raw.githubusercontent.com/glirette/NotaryGeekPublicKnowledgeWorker/main/NotaryGeek.PublicKnowledge.Worker/public-knowledge/topics/background-check-relationship-disclosure-boundary.json")) {
    throw "Manifest source sets are missing background-check-relationship-disclosure-boundary.json."
}

[pscustomobject]@{
    JsonFilesParsed = $jsonFiles.Count
    IndexedGithubUrlsChecked = $indexedFiles.Count
    Status = "OK"
}
