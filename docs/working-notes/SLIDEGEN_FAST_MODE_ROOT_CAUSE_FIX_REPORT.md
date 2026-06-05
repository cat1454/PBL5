# SlideGen Fast Mode Root Cause Fix Report

Date: 2026-06-06
Branch: `feature/slidegen-optimization`

## Root cause

Runtime `SpeedMode=fast` was reaching `FolderStudio.js`, `client/src/services/api.js`, and `SlidesController.cs`, but it stopped at the controller/image-sourcing layer. `SlideGeneratorService.GenerateOutlineAsync` did not receive speed mode context, so every run still called `GenerateSectionPlansAsync`.

Inside `GenerateSectionPlansAsync`, each section called:

`_ollamaService.GenerateStructuredResponseAsync<SlideSectionSummaryDraft>(...)`

That is the per-section AI summary loop shown in logs as `Phase=section-summaries Step=started`, and it could timeout after the Ollama 300 second request window.

## Fast mode behavior changed

Fast mode now passes `speedMode` into `ISlideGenerator.GenerateOutlineAsync` and `SlideGeneratorService.GenerateOutlineAsync`.

When `speedMode=fast`, `GenerateSectionPlansAsync` now:

- builds section plans locally from selected coverage chunks
- preserves section order using the first chunk number
- uses local headings/section keys for titles, clamped to 120 chars
- uses key facts, chunk summary, or first 1-2 excerpt sentences for summary, clamped to 420 chars
- keeps source chunk refs through `SourceChunkIds`
- returns before the per-section AI summary loop

Fast mode no longer calls:

`GenerateStructuredResponseAsync<SlideSectionSummaryDraft>`

Fast mode emits:

`[SlideGen:{JobId}] Phase=section-summaries Step=fast-local-plans SkippedAi=true SectionCount=...`

## Quality mode preserved

`speedMode=quality` and default document generation keep the existing path:

- AI section summaries still run
- outline generation still runs through the existing AI outline path
- slide content generation, verifier, repair, and quality image sourcing remain unchanged
- existing quality progress messages are preserved except shared safety guards for superseded jobs

## Section id/title/log clamp

`SlideSectionPlan.SectionId` no longer stores a full OCR text fallback. It now uses a stable local id:

- raw section key/heading/chunk id normalized
- long ids are clamped to about 96 chars with a SHA-256 hash suffix
- section titles are clamped to 120 chars
- section-summary logs clamp `SectionId` to 160 chars

This prevents full OCR paragraphs from appearing as section ids/titles/log fields.

## Job overlap handling

`SlideGenerationJobStore` now marks older jobs for the same document or folder as `superseded` when a newer job is created.

Controller safeguards were added so superseded jobs stop before major progress/save points:

- after outline generation and before placeholder deck save
- before each slide iteration
- after slide content generation
- before image sourcing and slide save
- before final deck completion save

Frontend progress now treats `superseded`, `cancelled`, and `canceled` as terminal statuses so polling does not continue forever.

Remaining limitation: this is not full cancellation-token cancellation. If a newer job starts while an older job is already inside one awaited DB write or image call, that single awaited operation cannot be interrupted by this patch. The next guard stops later updates/saves.

## Fast progress messages

Fast mode now avoids `section 5/10` AI-summary style progress because it no longer emits active `section-summaries` counters.

Fast progress messages now include:

- `Preparing selected content`
- `Building fast outline`
- `Generating slides`
- `Saving deck`

## Verification

No AI generation or backend runtime generation was executed.

Commands run:

- `node --check client\src\components\FolderStudio.js` pass
- `node --check client\src\services\api.js` pass
- `node --check client\src\services\progress.js` pass
- `dotnet build ELearnGamePlatform.sln` blocked by running `ELearnGamePlatform.API (22224)` locking API bin DLLs
- `dotnet build src\ELearnGamePlatform.Core\ELearnGamePlatform.Core.csproj --no-restore` pass
- `dotnet build src\ELearnGamePlatform.Services\ELearnGamePlatform.Services.csproj --no-restore` pass
- `dotnet build src\ELearnGamePlatform.API\ELearnGamePlatform.API.csproj --no-restore --no-dependencies -p:OutDir=H:\pbl5\.tmp\codex-out-api\` pass
- `cd client; $env:BUILD_PATH='build-codex-fast-mode-root-cause'; npm run build` pass

During the blocked solution build, `ELearnGamePlatform.Core`, `ELearnGamePlatform.Infrastructure`, and `ELearnGamePlatform.Services` compiled before the API copy step failed on locked DLLs.
