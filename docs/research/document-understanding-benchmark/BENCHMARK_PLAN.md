# Document Understanding Benchmark Plan

## Goal

This benchmark measures whether Document Understanding improves document extraction signals without reducing the quality of the legacy OCR/text pipeline.

The benchmark has three jobs:

- Measure extraction/OCR runtime and page quality.
- Measure Document Understanding confidence, status, regions, warnings, and fallback behavior.
- Keep a stable baseline so changes to OCR, layout analysis, tables, formulas, diagrams, and vision wiring can be compared across runs.

The default command is:

```powershell
dotnet run --project benchmarks/OcrBenchmark/OcrBenchmark.csproj
```

Reports are written to:

```text
benchmarks/output/ocr-benchmark-*.json
benchmarks/output/ocr-benchmark-*.md
```

## Dataset Checklist

Use local/manual files under `benchmarks/input-documents/`. Do not commit heavy sample documents unless the repo later defines a clear sample-data policy.

| Case | Local corpus path suggestion | Required signals |
| --- | --- | --- |
| PDF text clean | `benchmarks/input-documents/pdf-text/` | Direct text extraction, high confidence, low fallback count |
| DOCX | `benchmarks/input-documents/docx/` | OpenXML extraction, one-page quality fallback when needed |
| Clear scanned PDF | `benchmarks/input-documents/pdf-scan-clear/` | OCR pages, high enough page confidence |
| Blurry scanned PDF | `benchmarks/input-documents/pdf-scan-blurry/` | Low confidence, retry/fallback evidence, review warnings |
| Document photo image | `benchmarks/input-documents/image-document/` | Image OCR path, confidence and warnings |
| Table document | `benchmarks/input-documents/table/` | `TableLikeText` or `TableLowConfidence` regions |
| Diagram document | `benchmarks/input-documents/diagram/` | `DiagramCandidate` regions |
| Chart document | `benchmarks/input-documents/chart/` | Figure/diagram candidate regions and review warnings when text is sparse |
| Formula document | `benchmarks/input-documents/formula/` | `FormulaCandidate` regions and review warnings |

## Metrics

The report includes the legacy OCR metrics plus a `Document Understanding` section.

Per document:

- `extractionTimeMs`
- `documentConfidence`
- `pageCount`
- `regionCount`
- `fallbackCount`
- `visionCallCount`
- `failureReasons`
- `understandingStatus`
- `needsReview`

Run summary:

- total pages
- total regions
- total fallback count
- total vision calls
- total failures
- average confidence
- status counts
- top failure reasons

Vision analysis is disabled in the default benchmark. A default `visionCallCount` of `0` is expected and should not be treated as missing data. If a future benchmark mode enables real vision calls, it must use an explicit opt-in flag and document the local Ollama vision model requirement.

## Regression Criteria

Compare the newest Markdown/JSON report against the last accepted baseline.

Treat a run as needing review when:

- extraction time increases materially for the same corpus without a known reason;
- `documentConfidence` drops on clean PDF/DOCX inputs;
- `fallbackCount` increases;
- `regionCount` drops to zero for table, diagram, chart, or formula cases;
- `failureReasons` gains new extraction, layout, or quality warnings for unchanged documents;
- blurry/low-quality inputs are incorrectly marked as clean enough for auto generation.

Do not fake benchmark numbers in code. If a demo report needs illustrative numbers, keep it in a separate manual/demo document and label it clearly as demo data.

## Safety Notes

- The benchmark is a console project, not API startup code.
- It does not change persisted data, API contracts, migrations, frontend behavior, or appsettings.
- It does not require Ollama or a vision model in the default mode.
- It should remain safe to run on a small local corpus during development.
