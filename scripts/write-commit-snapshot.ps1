param(
    [string]$OutputDirectory = "commit-history"
)

$ErrorActionPreference = "Stop"

$repoRoot = (& git rev-parse --show-toplevel 2>$null)
if (-not $repoRoot) {
    exit 0
}

Push-Location $repoRoot
try {
    $stagedNames = @(& git diff --cached --name-only --diff-filter=ACMRTUXB)
    if (-not $stagedNames -or $stagedNames.Count -eq 0) {
        exit 0
    }

    $outputPath = Join-Path $repoRoot $OutputDirectory
    New-Item -ItemType Directory -Force $outputPath | Out-Null

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $filePath = Join-Path $outputPath "$timestamp.md"
    $branch = (& git rev-parse --abbrev-ref HEAD).Trim()
    $nameStatus = @(& git diff --cached --name-status --no-renames)
    $stat = @(& git diff --cached --stat --no-renames)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Commit Snapshot")
    $lines.Add("")
    $lines.Add("- Created at: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')")
    $lines.Add("- Branch: $branch")
    $lines.Add("- Staged file count: $($stagedNames.Count)")
    $lines.Add("")
    $lines.Add("## Files")
    $lines.Add("")
    $lines.Add('```text')
    foreach ($line in $nameStatus) {
        $lines.Add($line)
    }
    $lines.Add('```')
    $lines.Add("")
    $lines.Add("## Diff Stat")
    $lines.Add("")
    $lines.Add('```text')
    foreach ($line in $stat) {
        $lines.Add($line)
    }
    $lines.Add('```')
    $lines.Add("")

    [System.IO.File]::WriteAllLines($filePath, $lines, [System.Text.Encoding]::UTF8)
    & git add -- $filePath | Out-Null
}
finally {
    Pop-Location
}
