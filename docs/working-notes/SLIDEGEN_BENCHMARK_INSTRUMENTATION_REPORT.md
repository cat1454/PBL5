# SlideGen Benchmark Instrumentation Report

## Changed files

| File | Change | Risk |
|---|---|---|
| `src/ELearnGamePlatform.Services/AI/SlideGeneratorService.cs` | Added `DocumentId` to existing `section-summaries` logs. | Low; logging-only metadata, no generation behavior, prompt, endpoint, or schema change. |
| `scripts/analyze-slidegen-logs.ps1` | Added a lightweight PowerShell parser for copied `[SlideGen:*]` backend logs. | Low; standalone local script, read-only against a supplied log file. |
| `docs/working-notes/SLIDEGEN_RUNTIME_BENCHMARK_GUIDE.md` | Added manual benchmark guide for duplicate start, 2-section, and 10-section runs. | Low; documentation only. |
| `docs/working-notes/SLIDEGEN_BENCHMARK_INSTRUMENTATION_REPORT.md` | Added this implementation report. | Low; documentation only. |

## What this enables

This patch makes it easier to measure the true runtime of Workspace Slide Generation before optimizing the pipeline. The runtime check can now answer:

- one click creates one `POST generate/start`
- one returned `jobId` owns one polling stream
- 2 selected sections create about 2 section summary AI calls
- 10 selected sections create about 10 section summary AI calls
- each section summary call duration
- duplicate jobs or repeated section summaries

## Manual test checklist

- 1 click -> 1 POST `generate/start`.
- 1 `jobId` -> 1 polling stream.
- 2 sections -> about 2 section summary calls.
- 10 sections -> about 10 section summary calls.
- No section is repeated without a clear failure/retry reason.
- Do not run real AI generation unless intentionally doing the benchmark.

## Commands run

| Command | Result |
|---|---|
| `[System.Management.Automation.PSParser]::Tokenize(...)` on `scripts/analyze-slidegen-logs.ps1` | Passed. |
| `.\scripts\analyze-slidegen-logs.ps1` with a temp sample log | Passed; reported 1 jobId, 2 completed section summaries, and duration stats. |
| `cd H:\pbl5\client; BUILD_PATH=build-codex-slidegen-benchmark npm run build` | Passed; React production build compiled successfully. |
| `cd H:\pbl5; dotnet build src\ELearnGamePlatform.API\ELearnGamePlatform.API.csproj --no-restore` | Failed because API DLLs were locked by running `ELearnGamePlatform.API` process `22568`. |
| `cd H:\pbl5; dotnet build src\ELearnGamePlatform.API\ELearnGamePlatform.API.csproj --no-restore -p:OutputPath=H:\pbl5\.tmp\codex-api-output\` | Passed with 0 warnings and 0 errors. |

If the API DLL is locked again, use:

```powershell
dotnet build src\ELearnGamePlatform.API\ELearnGamePlatform.API.csproj --no-restore -p:OutputPath=H:\pbl5\.tmp\codex-api-output\
```

No real AI generation was run.
