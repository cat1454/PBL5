param(
    [switch]$NoRestore,
    [switch]$ClearOldCaches,
    [switch]$SkipDatabaseUpdate
)

$ErrorActionPreference = "Stop"

$apiRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $apiRoot "..\..")
$runtimeRoot = Join-Path $repoRoot ".runtime-h"

$paths = @{
    DOTNET_CLI_HOME = Join-Path $runtimeRoot "dotnet-home"
    NUGET_PACKAGES = Join-Path $runtimeRoot "nuget-packages"
    NUGET_HTTP_CACHE_PATH = Join-Path $runtimeRoot "nuget-http-cache"
    NUGET_SCRATCH = Join-Path $runtimeRoot "nuget-scratch"
    TEMP = Join-Path $runtimeRoot "temp"
    TMP = Join-Path $runtimeRoot "temp"
    MSBUILDTEMPPATH = Join-Path $runtimeRoot "msbuild-temp"
}

foreach ($path in $paths.Values) {
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

$env:DOTNET_CLI_HOME = $paths.DOTNET_CLI_HOME
$env:NUGET_PACKAGES = $paths.NUGET_PACKAGES
$env:NUGET_HTTP_CACHE_PATH = $paths.NUGET_HTTP_CACHE_PATH
$env:NUGET_SCRATCH = $paths.NUGET_SCRATCH
$env:TEMP = $paths.TEMP
$env:TMP = $paths.TMP
$env:MSBUILDTEMPPATH = $paths.MSBUILDTEMPPATH
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = "0"
$env:NUGET_XMLDOC_MODE = "skip"

Write-Host "Using H-drive runtime paths:" -ForegroundColor Cyan
foreach ($entry in $paths.GetEnumerator()) {
    Write-Host ("  {0} = {1}" -f $entry.Key, $entry.Value)
}

Push-Location $repoRoot
try {
    if ($ClearOldCaches) {
        Write-Host "Clearing current NuGet locals..." -ForegroundColor Yellow
        dotnet nuget locals all --clear
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }

    if (-not $NoRestore) {
        Write-Host "Running dotnet restore..." -ForegroundColor Yellow
        dotnet restore .\src\ELearnGamePlatform.API
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }

    Write-Host "Restoring local dotnet tools..." -ForegroundColor Yellow
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    if (-not $SkipDatabaseUpdate) {
        Write-Host "Running database migrations..." -ForegroundColor Yellow
        dotnet tool run dotnet-ef database update --project .\src\ELearnGamePlatform.Infrastructure --startup-project .\src\ELearnGamePlatform.API
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }

    Push-Location $apiRoot
    try {
        Write-Host "Running dotnet run --no-restore..." -ForegroundColor Yellow
        dotnet run --no-restore
        exit $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}
finally {
    Pop-Location
}
