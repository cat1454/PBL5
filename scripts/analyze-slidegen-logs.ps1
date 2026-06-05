param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Path
)

if (-not (Test-Path -LiteralPath $Path)) {
    Write-Error "Log file not found: $Path"
    exit 1
}

$lines = Get-Content -LiteralPath $Path
$jobRegex = '\[SlideGen:(?<jobId>[^\]]+)\]'
$sectionRegex = 'Phase=section-summaries\s+Step=(?<step>section-ai-completed|section-ai-failed).*?Section=(?<index>\d+)\/(?<total>\d+).*?SectionId=(?<sectionId>\S+).*?TextLength=(?<textLength>\d+).*?DurationMs=(?<durationMs>\d+)'
$postRegex = 'POST\s+\/api\/slides\/folders\/(?<folderId>\d+)\/generate\/start|POST\s+\/slides\/folders\/(?<folderIdAlt>\d+)\/generate\/start|Phase=request\s+Step=received\s+FolderId=(?<folderIdLog>\d+)'

$jobIds = [System.Collections.Generic.HashSet[string]]::new()
$requests = New-Object System.Collections.Generic.List[object]
$sectionEvents = New-Object System.Collections.Generic.List[object]

foreach ($line in $lines) {
    $jobMatch = [regex]::Match($line, $jobRegex)
    if ($jobMatch.Success) {
        [void]$jobIds.Add($jobMatch.Groups['jobId'].Value)
    }

    $postMatch = [regex]::Match($line, $postRegex)
    if ($postMatch.Success) {
        $folderId = $postMatch.Groups['folderId'].Value
        if ([string]::IsNullOrWhiteSpace($folderId)) { $folderId = $postMatch.Groups['folderIdAlt'].Value }
        if ([string]::IsNullOrWhiteSpace($folderId)) { $folderId = $postMatch.Groups['folderIdLog'].Value }
        $requests.Add([pscustomobject]@{
            FolderId = $folderId
            Line = $line
        })
    }

    $sectionMatch = [regex]::Match($line, $sectionRegex)
    if ($jobMatch.Success -and $sectionMatch.Success) {
        $sectionEvents.Add([pscustomobject]@{
            JobId = $jobMatch.Groups['jobId'].Value
            Step = $sectionMatch.Groups['step'].Value
            SectionIndex = [int]$sectionMatch.Groups['index'].Value
            SectionTotal = [int]$sectionMatch.Groups['total'].Value
            SectionId = $sectionMatch.Groups['sectionId'].Value
            TextLength = [int]$sectionMatch.Groups['textLength'].Value
            DurationMs = [int64]$sectionMatch.Groups['durationMs'].Value
        })
    }
}

Write-Host "SlideGen log analysis"
Write-Host "====================="
Write-Host ("JobIds found ({0}): {1}" -f $jobIds.Count, (($jobIds | Sort-Object) -join ', '))
Write-Host ("Generate/start request-like lines: {0}" -f $requests.Count)

if ($jobIds.Count -gt 1) {
    Write-Warning "Multiple jobIds found. Check whether one browser click created more than one generation job."
}

if ($requests.Count -gt 1) {
    Write-Warning "Multiple generate/start request-like lines found. Confirm Network shows only one POST per click."
}

if ($sectionEvents.Count -eq 0) {
    Write-Host "No section summary completion/failure lines found."
    exit 0
}

Write-Host ""
Write-Host "Section summaries by jobId"

$sectionEvents |
    Group-Object JobId |
    ForEach-Object {
        $events = $_.Group
        $completed = @($events | Where-Object { $_.Step -eq 'section-ai-completed' })
        $failed = @($events | Where-Object { $_.Step -eq 'section-ai-failed' })
        $durations = @($completed | ForEach-Object { $_.DurationMs })

        Write-Host ""
        Write-Host ("JobId: {0}" -f $_.Name)
        Write-Host ("  completed: {0}" -f $completed.Count)
        Write-Host ("  failed: {0}" -f $failed.Count)

        if ($durations.Count -gt 0) {
            $stats = $durations | Measure-Object -Average -Minimum -Maximum -Sum
            Write-Host ("  avg durationMs: {0:N0}" -f $stats.Average)
            Write-Host ("  min durationMs: {0}" -f $stats.Minimum)
            Write-Host ("  max durationMs: {0}" -f $stats.Maximum)
            Write-Host ("  total durationMs: {0}" -f $stats.Sum)
        }

        $duplicates = $events |
            Group-Object SectionIndex, SectionId |
            Where-Object { $_.Count -gt 1 }

        foreach ($duplicate in $duplicates) {
            Write-Warning ("JobId {0}: section appears multiple times: {1} ({2} events)" -f $_.Name, $duplicate.Name, $duplicate.Count)
        }
    }
