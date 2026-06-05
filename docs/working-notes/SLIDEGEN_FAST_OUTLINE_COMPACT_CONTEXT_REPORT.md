# SlideGen Fast Outline Compact Context Report

Date: 2026-06-06
Branch: `feature/slidegen-optimization`

## Root cause

Fast mode had already bypassed the per-section AI summary loop, but outline generation still used the shared outline path with no explicit fast-context contract.

Runtime context resolution could still produce a large selected scope, for example:

`SectionCount=10 ChunkCount=145 TextLength=136814`

The previous fast-local section plans were compact, but the outline call did not measure prompt/context size and did not explicitly cap representative source evidence for fast mode. This made it hard to prove that `Building fast outline` was not feeding too much source material into Ollama.

## Fast outline context limit

`speedMode=fast` now builds a dedicated compact outline prompt before the first outline Ollama call.

The fast outline context:

- uses section order from local `SlideSectionPlan.FirstChunkNumber`
- keeps section title first
- keeps the local fast summary, clamped to 420 chars
- keeps key facts from representative chunks plus local key ideas
- keeps source refs, but only representative refs in the prompt line
- selects at most 3 representative chunks per section
- prefers primary section chunk, highest-teachability chunk, and earliest chunk
- clamps each representative excerpt to 650 chars
- clamps total compact outline context to `10_000` chars

Fast outline no longer relies on full OCR text for the outline prompt. The prompt uses compact analyzed metadata plus the bounded compact outline context.

## Quality mode preserved

Quality mode keeps the existing outline prompt path:

- existing section-plan outline prompt remains unchanged
- existing retry outline path remains unchanged
- existing outline polish call remains unchanged
- slide generation, verifier, repair, image sourcing, editor/import image/elements/autosave/export/presentation are unchanged

Only shared outline completion timing was added.

## New logs

Before the fast outline Ollama call:

`[SlideGen:{JobId}] Phase=outline Step=prompt-built SpeedMode=fast SectionCount=... ChunkCount=... PromptChars=... ContextChars=...`

After `GenerateOutlineAsync` completes, including polish/retry/fallback:

`[SlideGen:{JobId}] Phase=outline Step=completed DurationMs=...`

The existing controller-level outline completion log is still preserved.

## Progress behavior

Fast mode still reports:

- `Preparing selected content`
- `Building fast outline`

Fast mode does not emit active `section-summaries` progress counters while building the outline.

## Verification

No AI generation or backend runtime generation was executed.

Commands run:

- `dotnet build src\ELearnGamePlatform.Services\ELearnGamePlatform.Services.csproj --no-restore` pass
- `dotnet build ELearnGamePlatform.sln` blocked by running `ELearnGamePlatform.API (444)` locking API bin DLLs after Core, Infrastructure, Services, and OcrBenchmark compiled
- `dotnet build src\ELearnGamePlatform.Core\ELearnGamePlatform.Core.csproj --no-restore` pass
- `dotnet build src\ELearnGamePlatform.Infrastructure\ELearnGamePlatform.Infrastructure.csproj --no-restore` pass
- `dotnet build src\ELearnGamePlatform.API\ELearnGamePlatform.API.csproj --no-restore --no-dependencies -p:OutDir=H:\pbl5\.tmp\codex-fast-outline-api\` pass
- `dotnet build ELearnGamePlatform.sln -p:OutDir=H:\pbl5\.tmp\codex-fast-outline-sln\` pass

Frontend build was not run because no frontend files changed.
