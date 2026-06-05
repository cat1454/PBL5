# PDF Render Fast Mode Guard Report

Date: 2026-06-05
Branch: `feature/slidegen-optimization`

## Why PDF Pages Are Rendered To Images

PBL5 uses `pdftoppm` for two image-based PDF workflows:

- OCR/ingestion for scanned PDFs, where pages must become images before Tesseract can read them.
- Optional visual enrichment, where a PDF page image is used for Document Understanding vision or for cropping a source PDF region into a slide image candidate.

This patch does not change OCR ingestion for scanned PDFs.

## Call Path Audit

Relevant render callers:

- `TesseractOcrService`: renders selected PDF pages for OCR. This is required for scanned PDF ingestion and remains unchanged.
- `VisionPageImageProvider`: renders one PDF page image for vision or PDF-region crop callers.
- `NoOpDocumentUnderstandingOrchestrator`: calls `VisionPageImageProvider` only when `DocumentUnderstanding.Enabled=true` and `EnableVisionAnalysis=true`.
- `SlidePdfImageAssetService`: calls `VisionPageImageProvider` to crop `FigureCandidate`, `DiagramCandidate`, `ChartCandidate`, or `ProcessCandidate` regions from the source PDF.
- `SlideImageService`: calls `SlidePdfImageAssetService` before generated-image fallback.
- `SlidesController.RunGenerateSlidesJobAsync`: calls `SlideImageService.SourceImagesForItemAsync(...)` after each completed slide.

The repeated log

```text
Using bundled pdftoppm executable for vision at H:\pbl5\poppler-25.12.0\Library\bin\pdftoppm.exe
```

was coming from `VisionPageImageProvider` construction/resolution during the slide image sourcing path, not from the slide text generation phase itself.

## Fast Mode Guard

Workspace generate deck now sends `speedMode: "fast"` through the existing generate-start request payload.

For `speedMode=fast`, slide image sourcing uses `SlideImageSourcingOptions.FastPreview`:

- skips Qwen image planning
- skips PDF-region extraction
- skips `VisionPageImageProvider` PDF render
- skips OpenAI image generation
- clears image candidates and selected image key
- marks the slide image plan as `fast-mode-skipped`
- keeps the slide safe as text-only / editor-ready

This keeps Fast Preview focused on getting an editable deck quickly.

## Quality Mode Preserved

For `speedMode=quality`, or document-level generation without explicit fast mode, slide image sourcing keeps the previous behavior:

- Qwen image planning can run
- PDF-region extraction can crop source PDF regions
- `VisionPageImageProvider` can render PDF pages
- generated-image fallback remains available when configured

Manual image refresh still uses the existing quality path.

## Log Changes

`VisionPageImageProvider` now caches the resolved `pdftoppm` path process-wide and logs the bundled path only once per process/path.

When a page is actually rendered, it logs timing separately:

```text
[PdfRender] Page={Page} Dpi={Dpi} DurationMs={DurationMs}
```

Fast Workspace deck generation should not emit repeated `VisionPageImageProvider` / `pdftoppm` path logs because the PDF render path is skipped.

## Build Result

- `dotnet build src\ELearnGamePlatform.API\ELearnGamePlatform.API.csproj --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:OutDir=H:\pbl5\.tmp\codex-api-fastmode-build\ -v:minimal`: pass, 0 warnings, 0 errors.
- `dotnet test tests\ELearnGamePlatform.Services.Tests\ELearnGamePlatform.Services.Tests.csproj --no-restore --filter SlideImagePlannerServiceTests -m:1 -nr:false -p:UseSharedCompilation=false -v:minimal`: pass, 11/11.
- `dotnet build ELearnGamePlatform.sln --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -v:minimal`: pass, 0 warnings, 0 errors.
- `cd client; BUILD_PATH=build-codex-pdf-render-fast-guard npm run build`: pass.
