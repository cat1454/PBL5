# SlideGen Runtime Benchmark Guide

## Goal

Measure the real runtime of Workspace Slide Generation, especially the `section-summaries` phase, before optimizing the pipeline.

## Prerequisites

- Backend is running.
- Frontend is running.
- Browser DevTools is open on Network and Console.
- Backend terminal is showing `[SlideGen:*]` logs.
- Do not run this unless you are ready to spend real local Ollama time.

## Test A: duplicate generate request

Steps:

1. Open a Workspace with a completed source.
2. Select a few sections.
3. Click Generate once.
4. Check Network and Console.

Expected:

- Only 1 `POST /slides/folders/{folderId}/generate/start`.
- Only 1 `jobId` is returned.
- Progress polling uses that same `jobId`.
- Frontend console shows one `generate-click`, one `job-received`, and one polling stream for that `jobId`.

## Test B: 2 sections

Steps:

1. Select exactly 2 sections.
2. Generate a deck.
3. Copy backend logs containing `[SlideGen:{JobId}]`.
4. Count `section-ai-completed` and `section-ai-failed` lines.

Expected:

- About 2 section summary AI calls.
- No section is repeated without a clear failure/retry reason.
- Each section line includes section index, total, section id, text length, and `DurationMs`.

## Test C: 10 sections

Repeat Test B with exactly 10 selected sections.

Expected:

- About 10 section summary AI calls.
- Total section-summary time is close to the sum of per-section `DurationMs` when the phase is running sequentially.

## Optional parser

Save copied backend logs to a file, for example:

```powershell
.\scripts\analyze-slidegen-logs.ps1 .\slidegen-log.txt
```

The parser reports:

- jobIds found
- generate/start request-like line count
- section summary completed count per jobId
- failed section summary count per jobId
- average, min, max, and total `DurationMs`
- warning if multiple jobIds are present
- warning if the same section index and section id appears multiple times

## How to conclude

- If 2 sections still create 10 summary calls, investigate selected scope/source mapping.
- If 2 sections create 2 calls but each call is very slow, the bottleneck is AI call runtime, prompt length, or local Ollama runtime.
- If 10 sections create 10 calls back to back, the bottleneck is sequential orchestration.
- If multiple `jobId` values appear after one click, duplicate start is still present.
- If the same section is summarized multiple times, investigate loop, retry, or cache behavior.

## Logs to paste back

Ask the tester to paste:

- frontend console `[SlideGen]` lines from generate click until completed or failed
- backend `[SlideGen:{JobId}]` lines from request received until section summaries finish
- Network summary: number of POST `generate/start` requests and returned `jobId`
