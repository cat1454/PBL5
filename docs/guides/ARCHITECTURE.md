# Architecture - ELearn Game Platform

Verified from source: 2026-05-07.

## Overview

ELearn Game Platform is an MVP+ demo app built as a React frontend over an ASP.NET Core Web API. Runtime state is PostgreSQL + EF Core, not MongoDB.

```text
React client
  -> ASP.NET Core API controllers
  -> Services layer for OCR, AI, learning, slide generation/export
  -> Core contracts/entities + Infrastructure repositories
  -> PostgreSQL, Ollama, local upload/asset storage
```

## Projects

- `src/ELearnGamePlatform.API`: entrypoint, DI, controllers, JWT auth, in-memory job stores, document ingestion, slide image pipeline, uploads.
- `src/ELearnGamePlatform.Core`: entities, enums, repository/service interfaces, shared domain contracts.
- `src/ELearnGamePlatform.Infrastructure`: `ApplicationDbContext`, EF Core migrations, repositories, `OllamaService`, PostgreSQL/Ollama config.
- `src/ELearnGamePlatform.Services`: OCR processors, content analysis, question generation, slide generation, slide export.
- `client`: React 18 app using React Router, Axios, auth context, and live backend APIs.

## Backend Runtime

- API URL is pinned in `Program.cs` to `http://localhost:5000`.
- Swagger is enabled at `http://localhost:5000/swagger`.
- EF Core migrations run automatically on startup through `dbContext.Database.Migrate()`.
- Startup also validates selected critical columns for questions, documents, and slide items.
- All controllers except `POST /api/auth/register` and `POST /api/auth/login` require JWT. Admin overview requires role `Admin`.

## Core Entities

Main persisted entities verified in `src/ELearnGamePlatform.Core/Entities`:

- `AppUser`
- `Document`
- `ProcessedContent`
- `Question`
- `GameSession`
- `LearningAttempt`
- `LearningProgress`
- `LearningTestResult`
- `FolderProject`
- `SlideDeck`
- `SlideItem`
- `SlideImageMetadata`
- progress/update DTOs for document, question, and slide jobs

## Data Layer

`ApplicationDbContext` maps the runtime schema to PostgreSQL. Repositories currently include:

- `DocumentRepository`
- `QuestionRepository`
- `GameSessionRepository`
- `FolderProjectRepository`
- `SlideDeckRepository`

JSON-shaped data is stored in JSONB columns where configured by EF Core. PostgreSQL does not automatically create useful GIN indexes for JSONB query patterns; add explicit indexes in migrations if JSONB fields become query targets.

## AI / Ollama

Runtime config comes from `src/ELearnGamePlatform.API/appsettings.json`:

- `Model`: `qwen2.5:7b`
- `AnalysisModel`: `qwen2.5:7b`
- `GenerationModel`: `qwen2.5:7b`
- `VerificationModel`: `qwen2.5:7b`
- `BaseUrl`: `http://localhost:11434`

`OllamaService` resolves the profile-specific model first. If a non-generation profile fails and differs from the generation/default model, it falls back to the generation/default model.

The repo also contains `qwen2.5-edu-json.modelfile`; that is a local optional model recipe and is not the default unless `appsettings.json` is changed.

## Main Flows

### Auth

1. `AuthController` registers or logs in a user.
2. `JwtTokenService` creates a bearer token.
3. React `AuthContext` stores the token in `localStorage`.
4. Axios attaches `Authorization: Bearer <token>`.
5. Protected controllers read the current user through claims.

Current limitation: auth is real basic JWT for demo/local use, but it lacks production hardening such as refresh tokens, reset password, email verification, rate limiting, and audit logs.

### Document Processing

1. Frontend uploads a file through `POST /api/documents/upload`.
2. API validates extension/size and user ownership.
3. File metadata and file content are stored.
4. `DocumentIngestionService` starts a `Task.Run` background job.
5. PDF/DOCX/image processors extract text; scanned images/PDFs use Tesseract and Poppler/`pdftoppm`.
6. `ContentAnalyzerService` analyzes chunks through Ollama and local fallback/merge logic.
7. PostgreSQL stores extracted text, summary, topics, key points, structure, and coverage metadata.
8. Frontend polls `GET /api/documents/{id}/progress`.

### Question Generation

1. Async flow starts with `POST /api/questions/generate/start`.
2. API creates an in-memory job state in `QuestionGenerationJobStore`.
3. `Task.Run` invokes `QuestionGeneratorService`.
4. The service uses Ollama generation plus local/AI verification and one repair pass when needed.
5. Questions are persisted to PostgreSQL.
6. Frontend polls `GET /api/questions/generate/progress/{jobId}`.

`POST /api/questions/generate` still exists as a legacy/synchronous endpoint. Prefer the async start/progress pair for UI flows.

### Learning / Games

Quiz, flashcards, streak/session flows, practice tests, attempts, progress summaries, and CSV exports are implemented through `GamesController` and `LearningController`.

### Slide Generation

Slide module is implemented, not just planned:

1. `SlidesController` starts document or folder/workspace deck jobs.
2. `SlideGenerationJobStore` keeps progress in memory.
3. `SlideGeneratorService` builds outline and slide content from processed document chunks.
4. Local and AI verifier metadata is applied; low-confidence/fallback content can be marked for review.
5. `SlideImageService` can refresh/select image candidates when the image pipeline is enabled.
6. `SlideDeck` and `SlideItem` are persisted.
7. HTML preview, HTML export, print HTML, PPTX basic export, and slide item edits are available.

Frontend `SlideStudio` uses live APIs from `client/src/services/api.js`; it is not a mock-only screen.

## API Surface

Controller routes verified from source:

- Auth: `POST /api/auth/register`, `POST /api/auth/login`, `GET /api/auth/me`
- Admin: `GET /api/admin/overview`
- Documents: upload, get, progress, structure, analyze-structure, user list, delete
- Workspaces/Folders: create, user list, default workspace, get, delete, source upload/list, slide selection
- Questions: async generate start/progress, legacy sync generate, document list, get, update, delete
- Games: sessions, quiz, quiz answers, flashcards, user sessions
- Learning: attempts, test start/submit/list/summary, progress, CSV exports
- Slides: document/folder generation, progress, deck fetch, HTML preview, HTML/print/PPTX export, item edit, image refresh/select

## Known Limitations

- Not production-ready.
- Job state/progress is in memory and can be lost when the API restarts.
- Background jobs use `Task.Run`, not a durable queue or worker service.
- Test coverage is not a complete automated safety net.
- AI quality depends on the local model, available hardware, and input document quality.
- Security hardening is still incomplete for production deployment.
- `local-store` contains old/sample data and is not the runtime database source.
