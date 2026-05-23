param(
    [string]$InputPath = "src/ELearnGamePlatform.API/logs/auto-repair-evidence.jsonl",
    [string]$OutputPath = "artifacts/auto-repair-log-report.md"
)

$ErrorActionPreference = "Stop"

function Format-Rate([int]$numerator, [int]$denominator) {
    if ($denominator -eq 0) {
        return "0.00%"
    }

    return ("{0:N2}%" -f (($numerator / $denominator) * 100))
}

function Limit-Text([string]$value, [int]$limit = 220) {
    if ([string]::IsNullOrWhiteSpace($value)) {
        return ""
    }

    $normalized = (($value -replace "\r|\n|\t", " ") -replace "\s{2,}", " ").Trim()
    if ($normalized.Length -le $limit) {
        return $normalized
    }

    return $normalized.Substring(0, $limit)
}

if (-not (Test-Path -LiteralPath $InputPath)) {
    throw "Input log file not found: $InputPath"
}

$records = Get-Content -LiteralPath $InputPath |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { $_ | ConvertFrom-Json }

$rawRecords = @($records | Where-Object { $_.stage -eq "RawOutputValidation" })
$total = $rawRecords.Count
$invalidRaw = @($rawRecords | Where-Object { $_.rawOutputValid -eq $false }).Count
$invalidFinal = @($rawRecords | Where-Object { $_.finalOutputValid -eq $false }).Count
$rawRate = Format-Rate $invalidRaw $total
$finalRate = Format-Rate $invalidFinal $total
$absoluteReductionPoints = if ($total -eq 0) { 0 } else { (($invalidRaw - $invalidFinal) / $total) * 100 }
$relativeReduction = if ($invalidRaw -eq 0) { 0 } else { (($invalidRaw - $invalidFinal) / $invalidRaw) * 100 }

$topErrorTypes = @($rawRecords |
    Where-Object { $_.errorType -and $_.errorType -ne "None" } |
    Group-Object -Property errorType |
    Sort-Object -Property Count -Descending |
    Select-Object -First 10)

$examples = @($rawRecords |
    Where-Object { $_.rawOutputValid -eq $false -or $_.autoRepairTriggered -eq $true } |
    Select-Object -First 3)

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$report = New-Object System.Collections.Generic.List[string]
$report.Add("# Auto-Repair JSON Evidence Report")
$report.Add("")
$report.Add("Generated: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss K")")
$report.Add("")
$report.Add("## Before/After Summary")
$report.Add("")
$report.Add("| Metric | Value |")
$report.Add("|---|---:|")
$report.Add("| Total AI outputs tested | $total |")
$report.Add("| Invalid raw JSON outputs | $invalidRaw |")
$report.Add("| Raw JSON error rate | $rawRate |")
$report.Add("| Outputs still invalid after Auto-repair | $invalidFinal |")
$report.Add("| Final JSON error rate | $finalRate |")
$report.Add("| Absolute reduction | $(""{0:N2} percentage points"" -f $absoluteReductionPoints) |")
$report.Add("| Relative reduction | $(""{0:N2}%"" -f $relativeReduction) |")
$report.Add("")
$report.Add("## Top Error Types")
$report.Add("")
if ($topErrorTypes.Count -eq 0) {
    $report.Add("No raw JSON errors were recorded.")
} else {
    $report.Add("| Error type | Count |")
    $report.Add("|---|---:|")
    foreach ($group in $topErrorTypes) {
        $report.Add("| $($group.Name) | $($group.Count) |")
    }
}

$report.Add("")
$report.Add("## Representative Examples")
$report.Add("")
if ($examples.Count -eq 0) {
    $report.Add("No invalid or repaired examples were found in the log.")
} else {
    $index = 1
    foreach ($example in $examples) {
        $report.Add("### Example $index")
        $report.Add("")
        $report.Add("| Field | Value |")
        $report.Add("|---|---|")
        $report.Add("| timestamp | $($example.timestamp) |")
        $report.Add("| correlationId | $($example.correlationId) |")
        $report.Add("| documentId | $($example.documentId) |")
        $report.Add("| module | $($example.module) |")
        $report.Add("| before error | $($example.errorType): $(Limit-Text $example.errorMessage) |")
        $report.Add("| repair result | triggered=$($example.autoRepairTriggered), success=$($example.repairSuccess) |")
        $report.Add("| final validation | finalOutputValid=$($example.finalOutputValid) |")
        $report.Add("| raw preview | $(Limit-Text $example.rawOutputPreview) |")
        $report.Add("| repaired preview | $(Limit-Text $example.repairedOutputPreview) |")
        $report.Add("")
        $index += 1
    }
}

Set-Content -LiteralPath $OutputPath -Value $report -Encoding UTF8
Write-Host "Generated report: $OutputPath"
