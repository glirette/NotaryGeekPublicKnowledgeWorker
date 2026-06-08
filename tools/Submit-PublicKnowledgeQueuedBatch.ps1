param(
    [string] $BaseUrl = $env:PUBLIC_KNOWLEDGE_BASE_URL,
    [string] $FunctionKey = $env:PUBLIC_KNOWLEDGE_FUNCTION_KEY,
    [ValidateSet("All", "Core", "Platform", "Apostille", "Recipient", "NNA")]
    [string] $Batch = "Core",
    [string] $CaseId = "",
    [ValidateSet("", "Default", "OpenAI", "Straico")]
    [string] $Provider = "",
    [switch] $DryRun,
    [switch] $NoWait,
    [int] $PollSeconds = 15,
    [int] $TimeoutMinutes = 45,
    [string] $OutDir = "",
    [switch] $FailOnBadStatus,
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

function Test-TerminalStatus {
    param([string] $Status)

    return @("completed", "completed-with-errors", "completed-empty", "failed") -contains $Status
}

function Get-ProviderCalled {
    param([object] $Value)

    if ($null -ne $Value.ProviderCalled) {
        return [bool] $Value.ProviderCalled
    }

    return [bool] $Value.OpenAiCalled
}

function Save-Json {
    param(
        [object] $Value,
        [string] $Name
    )

    if ([string]::IsNullOrWhiteSpace($OutDir)) {
        return
    }

    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $Value |
        ConvertTo-Json -Depth 40 |
        Set-Content -LiteralPath (Join-Path $OutDir $Name) -Encoding UTF8
}

function Select-ReceiptTable {
    param([object[]] $Receipts)

    $Receipts |
        Select-Object `
            CaseId,
            Ok,
            Status,
            ScoreVerdict,
            @{ Name = "MustHold"; Expression = { if ($null -ne $_.MustHoldTotal) { "{0}/{1}" -f $_.MustHoldPassed, $_.MustHoldTotal } else { "" } } },
            @{ Name = "FailSig"; Expression = { if ($null -ne $_.FailureSignalTotal) { "{0}/{1}" -f $_.FailureSignalsObserved, $_.FailureSignalTotal } else { "" } } },
            @{ Name = "ProviderCalled"; Expression = { Get-ProviderCalled $_ } },
            SourceCount,
            WarningCount,
            ErrorCount,
            LatestBlobName
}

$submitQuery = @{
    code = $FunctionKey
    execute = if ($DryRun) { "false" } else { "true" }
}

if (-not [string]::IsNullOrWhiteSpace($Provider) -and $Provider -ne "Default") {
    $submitQuery.provider = $Provider
}

if ([string]::IsNullOrWhiteSpace($CaseId)) {
    $submitQuery.batch = $Batch
}
else {
    $submitQuery.case = $CaseId
}

$submitUri = New-PublicKnowledgeUri -Path "/api/public-knowledge/runs/submit-batch" -Query $submitQuery
$submit = Invoke-RestMethod -Uri $submitUri -Method Get
Save-Json -Value $submit -Name "submit.json"

if ($NoWait) {
    if ($PassThru) {
        return $submit
    }

    [pscustomobject]@{
        Ok = [bool] $submit.ok
        Status = [string] $submit.status
        JobId = [string] $submit.jobId
        Batch = [string] $submit.batch
        Execute = [bool] $submit.execute
        CaseCount = [int] $submit.caseCount
        Provider = [string] $submit.provider
        StatusPath = [string] $submit.statusPath
    }
    return
}

$jobId = [string] $submit.jobId
if ([string]::IsNullOrWhiteSpace($jobId)) {
    throw "Submit response did not include a jobId."
}

$deadline = (Get-Date).ToUniversalTime().AddMinutes($TimeoutMinutes)
$jobResponse = $null
do {
    Start-Sleep -Seconds ([Math]::Max(1, $PollSeconds))
    $jobUri = New-PublicKnowledgeUri -Path "/api/public-knowledge/runs/jobs/$([Uri]::EscapeDataString($jobId))" -Query @{ code = $FunctionKey }
    $jobResponse = Invoke-RestMethod -Uri $jobUri -Method Get
    $job = $jobResponse.job
    $status = [string] $job.Status
    Write-Host ("{0} status={1} completed={2}/{3}" -f (Get-Date -Format "HH:mm:ss"), $status, $job.CompletedCount, $job.TotalCount)
}
while (-not (Test-TerminalStatus -Status $status) -and (Get-Date).ToUniversalTime() -lt $deadline)

Save-Json -Value $jobResponse -Name "job-final.json"

if (-not (Test-TerminalStatus -Status $status)) {
    try {
        $jobsUri = New-PublicKnowledgeUri -Path "/api/public-knowledge/runs/jobs" -Query @{ code = $FunctionKey; take = "10" }
        $jobsResponse = Invoke-RestMethod -Uri $jobsUri -Method Get
        Write-Host "Recent queued jobs before timeout:"
        ($jobsResponse.jobs |
            Select-Object `
                SubmittedAtUtc,
                Status,
                Batch,
                Trigger,
                @{ Name = "Done"; Expression = { "{0}/{1}" -f $_.CompletedCount, $_.TotalCount } },
                IsStale,
                ActiveAgeMinutes,
                JobId) |
            Format-Table -AutoSize
    }
    catch {
        Write-Host "Could not fetch recent queued jobs before timeout: $($_.Exception.Message)"
    }

    throw "Timed out waiting for queued job '$jobId'. Last status: $status."
}

if ($FailOnBadStatus -and $status -ne "completed") {
    throw "Queued job '$jobId' finished with status '$status'."
}

if ($PassThru) {
    return $jobResponse
}

Select-ReceiptTable -Receipts @($jobResponse.job.Receipts) | Format-Table -AutoSize
