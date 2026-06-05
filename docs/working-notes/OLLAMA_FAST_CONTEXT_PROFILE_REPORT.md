# Ollama Fast Context Profile Report

Date: 2026-06-06
Branch: `feature/slidegen-optimization`

## Changed files

| File | Change | Risk |
|---|---|---|
| `src/ELearnGamePlatform.Core/Interfaces/IOllamaService.cs` | Added `FastOutline` and `FastSlide` Ollama profiles. | Low: enum extension only; existing profiles unchanged. |
| `src/ELearnGamePlatform.Core/Interfaces/ISlideGenerator.cs` | Added optional `speedMode` to `GenerateSlideAsync`. | Low: internal service contract only; public API unchanged. |
| `src/ELearnGamePlatform.Infrastructure/Configuration/OllamaSettings.cs` | Added optional per-profile context token and fast temperature settings. | Low: safe defaults; existing config still binds. |
| `src/ELearnGamePlatform.Infrastructure/Services/OllamaService.cs` | Sends `options.num_ctx` when configured and logs `[OllamaCall]` before each request. | Medium: request options affect Ollama runtime profile, but only configured profiles get `num_ctx`. |
| `src/ELearnGamePlatform.Services/AI/SlideGeneratorService.cs` | Maps Fast outline/slide calls to fast Ollama profiles and adds job-correlated Ollama call logs. | Medium: Fast mode uses smaller request context; Quality mode remains on generation/analysis/verification profiles. |
| `src/ELearnGamePlatform.API/Controllers/SlidesController.cs` | Passes normalized `speedMode` into per-slide generation. | Low: no endpoint or payload contract change. |
| `src/ELearnGamePlatform.API/appsettings.json` | Configures `GenerationContextTokens=65536`, `FastOutlineContextTokens=8192`, `FastSlideContextTokens=16384`, `FastOutlineTemperature=0.3`. | Low: model names, ports, secrets, schema, and packages unchanged. |
| `tests/ELearnGamePlatform.Services.Tests/SlideExportServiceTests.cs` | Updated fake `ISlideGenerator` signature for optional `speedMode`. | Low: test-only compile adapter. |

## Current issue

Fast Preview had already skipped AI section summaries and compacted outline source context to about 10k chars, but Ollama requests still used the loaded `qwen3:4b` runtime context of `65536`.

That profile is heavier than Fast Preview needs for outline and slide calls. The fast path needs smaller per-request `num_ctx` so Ollama does less context allocation/evaluation work.

## Fast profile behavior

Fast outline:

- Profile: `fast-outline`
- Model: existing generation model, currently `qwen3:4b`
- `num_ctx`: `8192`
- Temperature: `0.3`
- Applies to initial outline, outline polish, outline retry, and JSON repair for that profile.

Fast slide generation:

- Profile: `fast-slide`
- Model: existing generation model, currently `qwen3:4b`
- `num_ctx`: `16384`
- Temperature: inherits generation temperature unless `FastSlideTemperature` is configured.
- Applies to initial slide generation, slide polish, slide retry, auto-repair generation, and JSON repair for that profile.

Fast mode still skips AI section summaries. No AI generation was run for verification.

## Quality behavior

Quality mode remains on the existing profiles:

- Section summaries: `analysis`
- Outline and slide generation: `generation`
- Slide verifier: `verification`

Configured `GenerationContextTokens=65536` preserves the currently observed quality/generation context profile. Existing model names, prompts, retry paths, verifier behavior, endpoint shape, DB schema, migrations, and packages were not changed.

## Logs added

Generic Ollama request log before every `/api/generate` call:

```text
[OllamaCall] Profile=fast-outline Model=qwen3:4b NumCtx=8192 PromptChars=... SystemChars=...
```

Slide generation correlated logs:

```text
[SlideGen:{JobId}] Phase=outline Step=ollama-call Call=initial Profile=fast-outline NumCtx=8192 PromptChars=... SystemChars=...
[SlideGen:{JobId}] Phase=slide-generation Step=ollama-call Call=initial Profile=fast-slide NumCtx=16384 PromptChars=... SystemChars=...
```

Full prompts are not logged.

## Build result

No frontend files changed, so frontend build was not run.

Commands run:

- `dotnet build src\ELearnGamePlatform.API\ELearnGamePlatform.API.csproj --no-restore` blocked by running `ELearnGamePlatform.API (444)` locking API output DLLs after Core, Infrastructure, and Services compiled.
- `dotnet build src\ELearnGamePlatform.API\ELearnGamePlatform.API.csproj --no-restore -p:OutDir=H:\pbl5\.tmp\codex-ollama-fast-profile\` pass, 0 warning / 0 error.
