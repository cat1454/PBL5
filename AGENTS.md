# AGENTS.md

This repo is a .NET + React MVP for document ingestion, OCR, AI analysis, quiz/flashcards, and slide generation.

## Repo Map

- `src/ELearnGamePlatform.API`: ASP.NET Core API entrypoint, controllers, DI, `Program.cs`, appsettings, runtime uploads.
- `src/ELearnGamePlatform.Core`: domain entities, interfaces, shared extensions.
- `src/ELearnGamePlatform.Infrastructure`: EF Core `ApplicationDbContext`, migrations, repositories, external integrations.
- `src/ELearnGamePlatform.Services`: OCR, document processing, AI generation/verification services.
- `client`: React 18 app built with `react-scripts`.
- `poppler-25.12.0`: bundled Poppler used as fallback/scan OCR dependency.

## Source Of Truth

- Prefer runtime source over older docs when they disagree.
- Backend URL is `http://localhost:5001` in `src/ELearnGamePlatform.API/Program.cs`.
- Frontend dev proxy also targets `http://127.0.0.1:5001` in `client/package.json`.
- `global.json` currently pins .NET SDK `9.0.306`; some docs still mention .NET 8, so do not assume docs are fully current.
- Database is PostgreSQL via EF Core, not MongoDB. Ignore stale MongoDB guidance in older docs.

## Run And Verify

- Restore/build solution: `dotnet restore ELearnGamePlatform.sln` then `dotnet build ELearnGamePlatform.sln`
- Run backend: `dotnet run --project src/ELearnGamePlatform.API`
- Run frontend: `cd client` then `npm install` and `npm start`
- Frontend production build: `cd client` then `npm run build`

## Environment Expectations

- PostgreSQL should be reachable at the configured `DefaultConnection`.
- Ollama settings live in `src/ELearnGamePlatform.API/appsettings.json` and may differ from README examples.
- OCR assets are expected under `src/ELearnGamePlatform.API/tessdata`.
- API startup applies EF migrations automatically and creates an `uploads` directory under the API working directory.

## Editing Rules

- Make focused changes and preserve the current MVP architecture unless the task explicitly asks for refactoring.
- Treat the existing worktree as user-owned: do not revert unrelated changes.
- When changing backend contracts, also inspect `client/src/services/api.js` and affected React screens.
- When changing slide/image pipeline code, check both API configuration and frontend slide preview/editor flows.
- Keep comments sparse and high-signal.
- Follow the bilingual UI rule in `BILINGUAL_UI_REQUIREMENTS.md` for any user-facing frontend change.

## Frontend Encoding Rule

- All frontend files must be read and written as UTF-8.
- Never save frontend files as ANSI, Windows-1252, Latin-1, or any unspecified default encoding.
- Do not leave mojibake text such as `Ã`, `Ä`, `áº`, `á»`, `Æ`, `Â`, or `ï¿½` in `client/src`.
- If a frontend file is already mojibake, repair the Vietnamese text back to correct Unicode before changing any logic.
- Preserve Vietnamese UI strings exactly; for example, never turn `Đăng xuất` into `ÄÄƒng xuáº¥t` or `Không gian dạy học` into `KhÃ´ng gian dáº¡y há»c`.
- After frontend text edits, run a mojibake search over `client/src` and fix any remaining broken strings before considering the task complete.

## Task Decomposition

- For backend-only tasks, inspect controller -> service -> persistence/config flow before editing.
- For frontend-only tasks, inspect screen -> `client/src/services/api.js` -> translation or shared styling surfaces before editing.
- For AI/OCR tasks, inspect prompt/config, chunking/verification behavior, and any coupled UI status/progress surface before changing generation logic.
- For slide pipeline tasks, inspect generation, stored deck/item shape, HTML preview, and Slide Studio editing flow together to avoid one-sided fixes.
- For schema or contract changes, treat persistence, API payloads, and frontend consumers as one change set even if only one layer appears broken first.

## Review Checklist

- Confirm the real source of truth in code before following README or older notes.
- Check whether the change introduces contract drift across API, frontend service helpers, or persisted models.
- Check for regression risk in OCR, AI analysis, quiz generation, flashcards, and slide generation flows that share the same document data.
- Prefer focused fixes over broad refactors unless the task explicitly asks for architecture work.
- Call out stale docs, hidden assumptions, or follow-up risks when they materially affect the changed area.

## Verification Loop

- Inspect first, then make the smallest change that can satisfy the request.
- After editing, re-read the affected call path to confirm inputs, outputs, and config binding still line up.
- Run the smallest relevant verification surface:
  - backend: `dotnet build ELearnGamePlatform.sln`
  - frontend: `cd client && npm run build`
  - cross-surface change: prefer both checks when dependencies are available
- If a full runtime check matters, prefer `dotnet run --project src/ELearnGamePlatform.API` for startup-sensitive backend work.
- If verification cannot run, state exactly what was skipped and why.

## Codex Workflow Guardrails

- Treat repo-local guidance as the default workflow surface; do not import large external agent packs into this repo by default.
- If adopting outside agent patterns or hooks, prefer copying the smallest proven practice into local docs or overrides instead of syncing full external configs.
- Do not duplicate hooks, MCP definitions, or plugin settings without first comparing them to `.codex/config.toml` and existing repo guidance.
- Keep Codex-related changes additive and reviewable so the current local workflow remains understandable to contributors who do not use the same tooling stack.

## Bilingual UI Rule

- Any new frontend feature or change to user-facing UI text must be implemented in both English (`en`) and Vietnamese with proper diacritics (`vi`) in the same task.
- Do not leave new buttons, labels, messages, empty states, validation errors, loading states, or settings text in only one language.
- When updating frontend copy, update the shared translation source for both languages before considering the task complete.

## Do Not Assume

- Do not claim test coverage exists by default; the repo currently has no established test projects.
- Do not introduce auth assumptions; the frontend still uses `demo-user`.
- Do not hardcode new ports, model names, or connection strings without checking `Program.cs` and `appsettings*.json`.
- Do not rely on README/DEVELOPMENT docs alone if code says otherwise.

## Done Checklist

- Build the smallest relevant surface after code changes.
- For backend changes, prefer at least `dotnet build` on the solution or affected project.
- For frontend changes, prefer `npm run build` or the smallest relevant validation if dependencies are already installed.
- For changes that touch AI/OCR, question generation, or slide generation, sanity-check the related config or coupled UI flow even if no runtime test is available.
- Call out any verification you could not run because of missing services, missing dependencies, or sandbox limits.
