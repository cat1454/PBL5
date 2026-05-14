# DESIGN.md - ELearn Game Platform Design Contract

> **Luật bắt buộc:** Any agent modifying this repository must preserve the existing document, question, game, and slide flows unless the user explicitly asks to change that flow.

## 1. Mục đích và thứ tự ưu tiên nguồn sự thật

`DESIGN.md` là design contract và guardrail bắt buộc cho agent trước khi sửa code trong repo này. Mục đích của file này là bảo vệ các luồng đang chạy: upload tài liệu, OCR/text extraction, AI analysis, question generation, quiz/flashcards, Slide Studio, HTML preview, slide editing và PostgreSQL persistence.

Mọi agent phải đọc file này trước khi thay đổi code. Nếu task chỉ yêu cầu một phần nhỏ, agent phải giữ nguyên các flow còn lại và chọn thay đổi nhỏ nhất an toàn.

### 1.1 Source of truth priority

Thứ tự ưu tiên nguồn sự thật:

1. Source code runtime hiện tại.
2. `DESIGN.md` guardrails.
3. `docs/guides/ARCHITECTURE.md`.
4. `README.md`.
5. `docs/guides/ROADMAP.md`.
6. `docs/guides/RUN_GUIDE.md` / `HUONG_DAN_CHAY.md` / `docs/guides/DEVELOPMENT.md`.
7. Docs cũ hoặc working notes.

Nếu docs mâu thuẫn với code, phải kiểm tra code trước và cập nhật docs cẩn thận. Nếu task mâu thuẫn với `DESIGN.md`, phải dừng và báo conflict trước khi sửa code. Nếu yêu cầu chưa rõ, hãy chọn thay đổi nhỏ nhất an toàn và không chạm vào flow không liên quan.

## 2. Nguyên tắc bắt buộc khi sửa code

- Không sửa module ngoài phạm vi task nếu không có lý do rõ ràng.
- Không đổi pipeline đang chạy nếu task chỉ yêu cầu UI.
- Không đổi API contract nếu không cập nhật frontend/backend/tests/docs liên quan.
- Không đổi database schema nếu task không yêu cầu migration.
- Không xóa fallback/error handling hiện có.
- Không đổi model AI mặc định nếu không được yêu cầu.
- Không thay đổi OCR flow nếu task không liên quan OCR.
- Không sửa nhiều flow cùng lúc.
- Không hardcode dữ liệu runtime mới.
- Không hardcode API URL, Ollama URL, database connection string, Tesseract path, Poppler path trong source code.
- Runtime config phải đi qua `appsettings`, environment variables, hoặc config mechanism hiện có.
- Không dùng local-store làm runtime database.
- Không phá progress polling.
- Không đổi demo-user/auth behavior trừ khi task là auth.
- Không tự ý đổi route, endpoint, DTO, entity ID type.
- Không xóa hoặc comment out logging trong document/OCR/AI/question/slide processing pipeline.
- Không nuốt exception âm thầm trong background processing.
- Khi thay đổi pipeline thực sự, phải cập nhật `DESIGN.md` trong cùng task.

## 3. Luồng xử lý chuẩn không được phá

### 3.1 Document pipeline

Luồng chuẩn:

Upload document
-> validate file/userId/size/extension
-> save uploaded file
-> create `Document` in PostgreSQL
-> background processing
-> extract text by file type
-> OCR if image/scanned PDF
-> cleanup OCR/text
-> AI analysis
-> save extracted text, summary, topics, key points, language, status.

Rules:

- UI upload changes must not bypass backend validation.
- Backend processing must update document status clearly.
- OCR/text extraction fallback must not be removed without a replacement.
- Processing errors must be logged and exposed as a clear failure state.
- Document progress polling, currently exposed by the backend, must remain compatible with frontend consumers.

### 3.2 Question pipeline

Luồng chuẩn:

Start generation job
-> create progress state
-> call `QuestionGeneratorService`
-> generate questions by coverage/batch
-> polish
-> local verifier + AI verifier
-> auto-repair once if weak
-> save questions to PostgreSQL
-> frontend polls progress endpoint
-> frontend loads questions by `documentId`.

Rules:

- Any change to question generation must preserve progress polling.
- Any generated question must remain linked to `documentId`.
- Do not create a separate incompatible question schema.
- If Ollama is unavailable, return/log a clear error or use existing fallback behavior. Do not fail silently.

### 3.3 Game pipeline

Luồng chuẩn:

Quiz and Flashcards reuse saved `Question` data. Game modes must not create a separate incompatible question schema. New game modes should reuse existing `Question` and `GameSession` where possible.

Rules:

- A game UI change must not modify document processing or slide generation.
- New game modes should use existing question data unless the task explicitly requires schema expansion.
- Learning/session/progress behavior must remain compatible with existing `GamesController` and `LearningController` consumers.

### 3.4 Slide pipeline

Luồng chuẩn:

Generate slide outline
-> generate slide content
-> local verifier + AI verifier
-> auto-repair once if needed
-> save `SlideDeck` and `SlideItem`
-> render HTML preview
-> allow editing slide items in Slide Studio.

Rules:

- Slide Studio UI changes must not rewrite document/question generation logic.
- Slide editing must persist through the existing slide API/data model.
- Slide preview changes must not break deck generation or item editing.
- If Ollama is unavailable, show a clear generation error or use existing fallback behavior. Do not fake successful slide generation.
- Slide export/image/folder routes must remain compatible unless the task explicitly changes them.

## 4. Module ownership / phạm vi sửa

| Module | Real files/folders in this repo | Owns | Must not touch accidentally |
|---|---|---|---|
| Document Upload | `src/ELearnGamePlatform.API/Controllers/DocumentsController.cs`, `src/ELearnGamePlatform.API/Services/DocumentIngestionService.cs`, `client/src/components/DocumentUpload.js`, `client/src/components/DocumentList.js`, `client/src/services/api.js` | Upload API, validation, user/document ownership, upload UI | Question generation, games, slide generation, EF schema unless required |
| OCR/Text Extraction | `src/ELearnGamePlatform.Services/DocumentProcessing/`, `src/ELearnGamePlatform.Services/OCR/`, `src/ELearnGamePlatform.Core/Utilities/TextCleanupUtility.cs`, `src/ELearnGamePlatform.API/tessdata`, `poppler-25.12.0/` | PDF/DOCX/image extraction, scanned PDF OCR, OCR cleanup | Question schema, game UI, slide editor, auth behavior |
| AI Analysis | `src/ELearnGamePlatform.Services/AI/ContentAnalyzerService.cs`, `src/ELearnGamePlatform.Services/AI/DocumentStructureChunker.cs`, `src/ELearnGamePlatform.Services/AI/DocumentCoverageMapBuilder.cs`, `src/ELearnGamePlatform.Infrastructure/Services/OllamaService.cs` | Summary, topics, key points, language, coverage/structure metadata | OCR fallback, question persistence, slide item editing unless explicitly coupled |
| Question Generation | `src/ELearnGamePlatform.API/Controllers/QuestionsController.cs`, `src/ELearnGamePlatform.API/Services/QuestionGenerationJobStore.cs`, `src/ELearnGamePlatform.Services/AI/QuestionGeneratorService.cs`, `src/ELearnGamePlatform.Infrastructure/Repositories/QuestionRepository.cs`, `src/ELearnGamePlatform.Core/Entities/Question.cs` | Generate/polish/verify/repair questions, progress jobs, question CRUD | Document ingestion, slide generation, game rendering except consumers |
| Games: Quiz/Flashcards | `src/ELearnGamePlatform.API/Controllers/GamesController.cs`, `src/ELearnGamePlatform.API/Controllers/LearningController.cs`, `src/ELearnGamePlatform.Infrastructure/Repositories/GameSessionRepository.cs`, `client/src/components/QuizGame.js`, `client/src/components/FlashcardGame.js`, `client/src/components/StreakGame.js`, `client/src/components/StudyHub.js` | Quiz, flashcards, streak/session flows, answers, learning progress | OCR/AI document processing, slide generation, DB schema unless game task requires it |
| Slide Studio | `src/ELearnGamePlatform.API/Controllers/SlidesController.cs`, `src/ELearnGamePlatform.API/Services/SlideGenerationJobStore.cs`, `src/ELearnGamePlatform.API/Services/SlideImageService.cs`, `src/ELearnGamePlatform.API/Services/SlideImagePlannerService.cs`, `src/ELearnGamePlatform.Services/AI/SlideGeneratorService.cs`, `src/ELearnGamePlatform.Services/Slides/SlideExportService.cs`, `client/src/components/SlideStudio.js`, `client/src/components/SlideStudioScreen.js`, `client/src/services/slideImages.js` | Slide generation, progress, HTML preview, item editing, image candidates, export | Document OCR, question generation, game schema |
| API Controllers | `src/ELearnGamePlatform.API/Controllers/` | HTTP routes, auth/ownership checks, request/response contracts | Entity/schema shape or frontend consumers without full contract review |
| EF Core/PostgreSQL | `src/ELearnGamePlatform.Infrastructure/Data/ApplicationDbContext.cs`, `src/ELearnGamePlatform.Infrastructure/Migrations/`, `src/ELearnGamePlatform.Infrastructure/Repositories/`, `src/ELearnGamePlatform.Core/Entities/` | Runtime schema, migrations, repositories, JSONB mapping | Frontend-only behavior, local-store sample data, MongoDB guidance |
| Frontend services | `client/src/services/api.js`, `client/src/services/progress.js`, `client/src/services/slideImages.js`, `client/package.json` | Axios calls, API URL/proxy coupling, progress helpers | Backend route/schema changes without backend update |
| Styling/UI components | `client/src/components/`, `client/src/components/common/`, `client/src/App.css`, `client/src/index.css`, `client/src/i18n/` | User experience, layout, shared UI, bilingual UI text | Backend pipeline, DB schema, AI prompts |
| Runtime config | `src/ELearnGamePlatform.API/appsettings.json`, `src/ELearnGamePlatform.API/appsettings.Development.json`, `src/ELearnGamePlatform.API/Program.cs`, `src/ELearnGamePlatform.API/Configuration/`, `src/ELearnGamePlatform.Infrastructure/Configuration/`, `client/package.json` | Ports, connection strings, JWT config, file upload config, Ollama config, image pipeline config | Hardcoded local secrets or machine-specific paths in source |
| Tests | `tests/ELearnGamePlatform.Services.Tests/`, `benchmarks/OcrBenchmark/` | Focused service tests, slide image planner tests, local-first pipeline tests, OCR benchmark | Application source behavior unless the task is test-driven and scoped |

## 5. API contract guardrails

Current API groups include:

Documents:

- `POST /api/documents/upload`
- `GET /api/documents/{id}`
- `GET /api/documents/{id}/progress`
- `GET /api/documents/{id}/structure`
- `POST /api/documents/{id}/analyze-structure`
- `GET /api/documents/user/{userId}`
- `DELETE /api/documents/{id}`

Questions:

- `POST /api/questions/generate/start`
- `GET /api/questions/generate/progress/{jobId}`
- `POST /api/questions/generate`
- `GET /api/questions/document/{documentId}`
- `GET /api/questions/document/{documentId}/metrics`
- `GET /api/questions/{id}`
- `PUT /api/questions/{id}`
- `DELETE /api/questions/{id}`

Games:

- `POST /api/games/sessions`
- `GET /api/games/sessions/{sessionId}`
- `POST /api/games/sessions/{sessionId}/start`
- `POST /api/games/sessions/{sessionId}/submit`
- `GET /api/games/quiz/{documentId}`
- `POST /api/games/quiz/{documentId}/answers`
- `GET /api/games/flashcards/{documentId}`
- `GET /api/games/user/{userId}`

Slides:

- `POST /api/slides/generate/start`
- `GET /api/slides/generate/progress/{jobId}`
- `GET /api/slides/document/{documentId}`
- `GET /api/slides/document/{documentId}/html`
- `GET /api/slides/{deckId:int}/export/html`
- `GET /api/slides/{deckId:int}/export/print`
- `GET /api/slides/{deckId:int}/export/pptx`
- `PUT /api/slides/{deckId}/items/{itemId}`
- `POST /api/slides/{deckId}/items/{itemId}/images/refresh`
- `POST /api/slides/{deckId}/items/{itemId}/images/select`
- `GET /api/slides/folders/{folderId}`
- `GET /api/slides/folders/{folderId}/html`
- `POST /api/slides/folders/{folderId}/generate/start`

Rules:

- If an endpoint is changed, update both backend and frontend service calls.
- If request/response shape changes, update all consumers.
- If ID type changes, update entities, DTOs, frontend parsing, tests, and docs.
- Do not introduce duplicate endpoints unless there is a migration plan.
- Do not delete an endpoint without a replacement and migration note.
- Prefer existing async start/progress endpoints for long-running AI/OCR/slide work.

## 6. Data model guardrails

Current important entities include:

- `Document`
- `Question`
- `GameSession`
- `ProcessedContent`
- `SlideDeck`
- `SlideItem`
- `QuestionGenerationProgressUpdate`
- `DocumentProcessingProgressUpdate`
- `SlideGenerationProgressUpdate`
- `AppUser`
- `FolderProject`
- `LearningAttempt`
- `LearningProgress`
- `LearningTestResult`
- `SlideImageMetadata`

Rules:

- PostgreSQL is the runtime database.
- IDs are integer IDs unless existing code says otherwise.
- JSON fields should be accessed through existing extension/helper patterns.
- Do not reintroduce MongoDB.
- Do not treat local-store JSON as live runtime data.
- Do not create parallel data models for the same concept.
- Any schema change requires migration, repository update, API review, frontend review, and docs update.
- JSONB query changes must be reviewed against `ApplicationDbContext` and migrations.

## 7. Frontend visual/UX principles

- One visual language across dashboard, quiz, flashcards, Slide Studio.
- Clear primary actions: Upload document, Learn now, Generate slides.
- AI jobs must show loading/progress/error/retry states.
- Empty states must explain what the user should do next.
- Long generated content should be previewed progressively, not dumped into dense UI.
- Slide Studio should separate selection, preview, edit, and generate actions.
- Quiz and Flashcards should feel like learning activities, not raw data lists.
- Desktop and mobile layouts must not break core actions.
- UI text changes must keep Vietnamese and English translations aligned where the app uses i18n.

## 8. Runtime config / environment guardrails

Runtime dependencies include PostgreSQL, Ollama, Tesseract tessdata, Poppler/`pdftoppm`, upload storage, and frontend/backend ports.

Current verified runtime facts:

- API runs at `http://localhost:5000` from `src/ELearnGamePlatform.API/Program.cs`.
- Frontend proxy points to `http://127.0.0.1:5000` in `client/package.json`.
- `global.json` pins .NET SDK `9.0.306`; backend projects target `net8.0`.
- PostgreSQL + EF Core + Npgsql is the runtime persistence layer.

Rules:

- Ollama `BaseUrl`/model settings must come from `appsettings`/env/config.
- PostgreSQL connection string must come from `appsettings`/env/config.
- Tesseract tessdata path must follow existing config/discovery behavior.
- Poppler/`pdftoppm` path must follow existing bundled/PATH fallback behavior.
- Do not hardcode machine-specific paths except in documentation examples.
- Do not commit secrets, passwords, API keys, or local-only credentials.
- If config shape changes, update setup/run docs.
- If a dependency is offline, show/log a clear error instead of silently succeeding.

## 9. AI/OCR prompt and model guardrails

- Do not change AI model config casually.
- Keep separate model/profile purposes: analysis, generation, verification.
- Preserve verifier and auto-repair behavior.
- OCR cleanup should not remove meaningful Vietnamese content.
- If improving OCR, add before/after validation or logs.
- Any prompt change must preserve JSON/structured output expected by parser.
- Ollama offline, timeout, malformed response, and low-quality response must be handled with clear logging and user-visible error/fallback behavior.
- Do not fake AI output as successful generated content.
- The optional `qwen2.5-edu-json.modelfile` is not the runtime default unless config is explicitly changed.

## 10. Logging and observability guardrails

- Do not remove logs from upload, OCR, AI analysis, question generation, slide generation, or job progress.
- Logs should help identify which document/job/stage failed.
- Errors in background tasks must be logged.
- Long-running AI/OCR stages should keep enough progress/status information for debugging.
- Do not replace specific error messages with generic "failed" if useful diagnostic context already exists.
- Never log secrets, full connection strings, or sensitive credentials.
- Prefer structured logs with stage/job/document identifiers where existing style allows.

## 11. Progress/job guardrails

Current progress jobs may be in-memory. Frontend depends on polling endpoints.

Known job stores:

- `src/ELearnGamePlatform.API/Services/DocumentProcessingJobStore.cs`
- `src/ELearnGamePlatform.API/Services/QuestionGenerationJobStore.cs`
- `src/ELearnGamePlatform.API/Services/SlideGenerationJobStore.cs`

Rules:

- Do not remove polling without replacing it everywhere.
- Do not mark job completed before data is actually persisted.
- Errors must be visible to frontend.
- Restart behavior limitations must be documented, not hidden.
- If moving progress state from memory to persistent storage, update API, frontend, docs, and regression checklist.

## 12. Pre-change impact checklist

Before editing code, every agent must answer:

- What user flow is this task changing?
- Which module owns this change?
- Which files are allowed to change?
- Which files must not be touched?
- Which flows must remain untouched?
- Does this change affect API contract?
- Does this change affect database schema?
- Does this change affect runtime config?
- Does this change affect AI/OCR output format?
- Does this change affect progress polling?
- Does this change conflict with a pending/unmerged change?
- What is the smallest safe implementation?

Conflict rule:

- If the new task conflicts with a pending change, unmerged branch, or unclear previous modification, stop and report the conflict.
- Do not self-resolve conflicting product decisions without user instruction.
- Do not overwrite unrelated local changes.

## 13. Definition of Done

A task is only done when:

- The requested behavior is implemented.
- The smallest safe set of files was changed.
- Existing document/question/game/slide flows are not intentionally broken.
- Backend builds if backend code changed.
- Frontend builds if frontend code changed.
- API endpoints used by existing frontend are not removed.
- Runtime config is not hardcoded.
- Errors/fallbacks/logging are preserved.
- Docs are updated if architecture, pipeline, config, API, or schema changed.
- `DESIGN.md` is updated if the real design contract changed.
- Any untested area is reported honestly.

Minimum acceptance criteria:

- `dotnet build ELearnGamePlatform.sln` passes when backend was changed.
- `npm run build` passes when frontend was changed.
- Main touched flow works manually.
- At least one unrelated core flow is checked for regression when practical.

## 14. Post-change regression checklist

Document flow:

- Upload PDF/DOCX/image.
- Status becomes Completed or clear failure state.
- Extracted text/analysis appears.
- OCR fallback still works for image/scanned PDF where applicable.

Question flow:

- Start generation.
- Progress polling works.
- Questions are saved.
- Questions load by `documentId`.
- Ollama failure produces a clear error/fallback.

Game flow:

- Quiz loads existing questions.
- Flashcards load existing questions.
- Game session can submit answers.
- New game mode, if any, does not require incompatible question schema.

Slide flow:

- Generate slide deck.
- Progress polling works.
- HTML preview opens.
- Editing slide item persists.
- Slide failure shows clear error/fallback.

System flow:

- Backend builds.
- Frontend builds.
- Existing routes still load.
- No unrelated API endpoints removed.
- No runtime config was hardcoded.

## 15. Allowed vs Forbidden change patterns

| Scenario | Allowed | Forbidden |
|---|---|---|
| UI-only change | Modify component/CSS/service call only if needed. | Rewrite backend pipeline, change database schema, change AI prompts. |
| API behavior change | Update controller + service + frontend service + docs. | Change response shape without updating consumers. |
| Database change | Migration + entity + repository + API/frontend review + docs. | Edit entity only and skip migration. |
| New game mode | Reuse `Question` data and `GameSession` where possible. | Create incompatible parallel question schema. |
| Slide template change | Modify slide template/config/rendering. | Touch OCR/question generation unless required. |
| OCR improvement | Improve preprocessing/fallback/logging with validation. | Remove existing PDF text extraction or OCR fallback. |
| Progress UI fix | Improve UI display using existing job API. | Remove backend job state or polling contract. |
| Code cleanup | Small local cleanup inside touched module. | Rename DTO/API/entity fields without full migration. |

## 16. Agent workflow

Future agents must follow:

1. Read `DESIGN.md`.
2. Identify exact requested change.
3. List affected flow/module.
4. Inspect current implementation.
5. Make smallest safe change.
6. Run build/test when possible.
7. Report changed files.
8. Report regression risks.
9. Never claim success if build/test was not run.

## 17. Scope lock

- Prioritize UI/UX core journey.
- Do not open large infrastructure scope unless needed.
- Do not implement full auth unless the task is specifically auth.
- Do not add too many game modes at once.
- Do not add more slide templates than requested.
- Do not mix benchmark/reliability/auth/template/game changes into one task.
- Do not mix design cleanup with feature implementation unless explicitly requested.
- Keep roadmap-aligned work focused on demo value unless the user asks for production hardening.

## 18. Lịch sử cập nhật DESIGN.md

| Ngày | Thay đổi | Người cập nhật |
|---|---|---|
| 2026-05-14 | Tạo DESIGN.md lần đầu để khóa design contract và bảo vệ core flows | Initial agent |

Rules:

- Khi thay đổi pipeline thực sự, phải cập nhật `DESIGN.md` cùng lúc.
- Khi thêm endpoint/module/runtime dependency mới, phải cập nhật `DESIGN.md`.
- Khi rule trong `DESIGN.md` không còn đúng với source code, phải cập nhật hoặc báo cáo conflict.
- Không để `DESIGN.md` trở thành tài liệu cũ không còn khớp hệ thống.

> **FINAL RULE:** Any agent modifying this repository must preserve the existing document, question, game, and slide flows unless the user explicitly asks to change that flow.
