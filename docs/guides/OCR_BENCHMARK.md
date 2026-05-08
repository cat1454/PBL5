# OCR Benchmark Workflow

This workflow runs local benchmark documents through the current extraction/OCR pipeline and writes JSON plus Markdown reports with document quality, page quality, retry metadata, timing, token estimates, and threshold recommendations.

## Input And Output

- Put local benchmark files in `benchmarks/input-documents`.
- Reports are written to `benchmarks/output`.
- Supported inputs: `.pdf`, `.docx`, `.png`, `.jpg`, `.jpeg`.
- `benchmarks/input-documents` and `benchmarks/output` are ignored by Git except for `.gitkeep` placeholders. Do not commit private sample PDFs/images.

## Run

```powershell
dotnet run --project benchmarks/OcrBenchmark/OcrBenchmark.csproj
```

The benchmark uses the runtime OCR settings from `src/ELearnGamePlatform.API/appsettings.json` and the existing processors:

- `PdfProcessor` for PDF direct text, scanned-page OCR, page quality reports, and low-quality retry metadata.
- `ImageProcessor` plus `TesseractOcrService` for image OCR.
- `DocxProcessor` for DOCX extraction.

## Report Contents

Each report includes:

- Document-level duration, size, character count, word count, estimated token count, average quality, low-quality page count, and warnings.
- Page-level method, quality score, confidence, signal ratio, noise score, selected OCR variant/pass, retry summary, and warnings.
- Stage timing from progress callbacks where the processor emits progress.
- Run-level summary metrics across all documents.
- Threshold recommendations for `MinAcceptablePageQuality`, `RetryThreshold`, `RetryPdfDpi`, and retry effectiveness.

## Tuning Notes

Treat the recommendation section as a review aid, not an automatic config change. First inspect any failed or empty pages because missing Poppler, missing `tessdata`, or unreadable local files can make threshold recommendations misleading.

Useful signals:

- High low-quality rate usually means the corpus needs manual review before raising `MinAcceptablePageQuality`.
- Effective retries show up as retried pages with positive quality gain.
- No retried pages means the current `RetryThreshold` did not trigger on the selected corpus; add noisier scanned pages before lowering retry settings.
- A high 25th percentile quality score can justify a stricter quality threshold after manual spot checks.
