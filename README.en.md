# ELearn Game Platform

ELearn Game Platform is a `.NET + React` MVP that turns learning documents into interactive study experiences. It supports document upload, OCR/text extraction, local AI analysis, question generation, quiz/flashcard/streak/practice-test study flows, progress tracking, workspace/folder management, and slide deck generation with preview, editing, and export.

The repository is currently an **MVP+ for PBL demo use**. The main flows can be demonstrated end to end, but the system is not production-ready yet because job progress is still in memory, background processing is demo-oriented, automated coverage is incomplete, and security hardening is not finished.

## Current Features

- **JWT authentication**: register, login, current user lookup, and basic roles: `ADMIN`, `INSTRUCTOR`, `LEARNER`.
- **Document pipeline**: upload `PDF`, `DOCX`, `PNG`, `JPG`, `JPEG`; validate files; extract text with PdfPig/OpenXML/Tesseract; clean OCR output; analyze summary, topics, key points, language, structure, and coverage metadata through Ollama.
- **Question pipeline**: generate questions from documents, track generation jobs, verify output, auto-repair weak output once, and persist questions to PostgreSQL.
- **Question Studio**: create generation runs, review drafts, edit/accept/reject/quarantine/restore drafts, and import approved drafts into the question bank.
- **Learning flows**: quiz, flashcards, streak mode, game sessions, practice tests, learning attempts, review queue, and document-level progress summaries.
- **Analytics**: personal dashboard with real activity data, heatmap, skill/radar view, checklist, and recent activity.
- **Workspace/Folder flow**: create workspaces/folders, upload multiple sources, select source sections for slides, and manage learning material by workspace.
- **Slide Studio**: generate slides from documents or workspaces/folders, select content scope, preview HTML, edit slide items, refresh/select image candidates, export HTML, open a print-friendly browser flow for Save as PDF, and export basic PPTX.

## Technology

### Backend

- ASP.NET Core Web API, projects target `net8.0`
- .NET SDK pinned by `global.json`: `9.0.306`
- Entity Framework Core 8
- PostgreSQL + Npgsql
- JWT Bearer Authentication
- Tesseract OCR, PdfPig, OpenXML, ImageSharp
- Local AI through Ollama, default model `qwen2.5:7b`

### Frontend

- React 18
- React Router DOM 6
- Axios
- React Scripts
- React Icons

### Runtime

- Backend: `http://localhost:5000`
- Swagger: `http://localhost:5000/swagger`
- Frontend dev server: `http://localhost:3000`
- Frontend proxy: `http://127.0.0.1:5000`
- Database: PostgreSQL through EF Core migrations

## Repository Structure

```text
src/
  ELearnGamePlatform.API/             Web API, controllers, DI, config, auth, runtime uploads
  ELearnGamePlatform.Core/            Entities, enums, interfaces, shared contracts
  ELearnGamePlatform.Infrastructure/  DbContext, repositories, migrations, Ollama integration
  ELearnGamePlatform.Services/        OCR, document processing, AI, question, slide services

client/
  public/                             Static frontend entrypoint
  src/                                React frontend source

tests/                                Automated test projects
docs/                                 Guides, research, working notes, agent context
benchmarks/                           OCR/document benchmark harness
scripts/                              Repo helper scripts
```

Folders/files such as `artifacts/`, `.artifacts/`, `.tmp/`, `commit-history/`, `src/ELearnGamePlatform.API/uploads/`, `src/ELearnGamePlatform.API/logs/`, `src/ELearnGamePlatform.API/tessdata/*.traineddata`, and `poppler-25.12.0/` are local/generated data or runtime assets, not primary source files.

## Environment Requirements

Install:

- .NET SDK `9.0.306`
- Node.js 18+
- PostgreSQL 14+
- Ollama
- Tesseract OCR
- Git

The OCR service looks for tessdata at:

```text
src/ELearnGamePlatform.API/tessdata
```

Recommended minimum files:

```text
eng.traineddata
vie.traineddata
```

For scanned PDFs, the system uses local Poppler when `poppler-25.12.0/Library/bin/pdftoppm.exe` exists; otherwise it falls back to `pdftoppm` from `PATH`.

## Local Configuration

The backend reads its main configuration from:

```text
src/ELearnGamePlatform.API/appsettings.json
```

Local example with placeholders only:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=ELearnGameDB;Username=postgres;Password=YOUR_LOCAL_PASSWORD;SslMode=disable"
  },
  "JwtSettings": {
    "Issuer": "ELearnGamePlatform",
    "Audience": "ELearnGamePlatform.Client",
    "SecretKey": "CHANGE_THIS_TO_A_LONG_LOCAL_DEV_SECRET",
    "ExpirationMinutes": 10080
  },
  "AdminSeed": {
    "Enabled": true,
    "Email": "admin@example.local",
    "Password": "CHANGE_THIS_ADMIN_PASSWORD",
    "FullName": "System Admin"
  },
  "OllamaSettings": {
    "BaseUrl": "http://localhost:11434",
    "Model": "qwen2.5:7b",
    "AnalysisModel": "qwen2.5:7b",
    "GenerationModel": "qwen2.5:7b",
    "VerificationModel": "qwen2.5:7b"
  }
}
```

Do not commit real passwords, API keys, or personal configuration.

## Run Locally

Clone the repository:

```powershell
git clone https://github.com/cat1454/PBL5.git
cd PBL5
```

Create the PostgreSQL database:

```sql
CREATE DATABASE "ELearnGameDB";
```

Prepare the Ollama model:

```powershell
ollama pull qwen2.5:7b
ollama list
```

Run the backend:

```powershell
cd src\ELearnGamePlatform.API
dotnet restore
dotnet run
```

Run the frontend in another terminal:

```powershell
cd client
npm install
npm start
```

Open the app:

```text
http://localhost:3000
```

## Suggested Demo Flow

1. Register or log in.
2. Create a workspace or use the default workspace.
3. Upload a PDF/DOCX/image document.
4. Wait for document processing to complete.
5. Review analysis, structure, and OCR text.
6. Open Question Studio or generate questions directly.
7. Review/import question drafts when using Question Studio.
8. Study through quiz, flashcards, streak, or practice test.
9. Review analytics and learning progress.
10. Open Slide Studio, select scope, generate a deck, preview, and edit.
11. Export the deck as HTML, browser Print/Save as PDF, or basic PPTX.

## Main API Groups

Most endpoints require a JWT Bearer token, except login/register.

```text
Auth
POST   /api/auth/register
POST   /api/auth/login
GET    /api/auth/me

Admin
GET    /api/admin/overview

Documents
POST   /api/documents/upload
GET    /api/documents/{id}
GET    /api/documents/{id}/progress
GET    /api/documents/{id}/structure
GET    /api/documents/{id}/understanding/latest
POST   /api/documents/{id}/analyze-structure
GET    /api/documents/user/{userId}
DELETE /api/documents/{id}

Workspaces / Folders
POST   /api/workspaces
GET    /api/workspaces/user/{userId}
GET    /api/workspaces/default/user/{userId}
GET    /api/workspaces/{id}
DELETE /api/workspaces/{id}
POST   /api/workspaces/{id}/sources/upload
GET    /api/workspaces/{id}/sources
PUT    /api/workspaces/{id}/sources/{sourceId}/slide-selection
POST   /api/folders
GET    /api/folders/user/{userId}
GET    /api/folders/{id}
DELETE /api/folders/{id}
POST   /api/folders/{id}/sources/upload
GET    /api/folders/{id}/sources
PUT    /api/folders/{id}/sources/{sourceId}/slide-selection

Questions
POST   /api/questions/generate/start
GET    /api/questions/generate/progress/{jobId}
POST   /api/questions/generate
GET    /api/questions/document/{documentId}
GET    /api/questions/document/{documentId}/metrics
GET    /api/questions/{id}
PUT    /api/questions/{id}
DELETE /api/questions/{id}

Question Studio
POST   /api/question-studio/runs/start
GET    /api/question-studio/runs/{runId}
GET    /api/question-studio/drafts
PUT    /api/question-studio/drafts/{draftId}
POST   /api/question-studio/drafts/{draftId}/accept
POST   /api/question-studio/drafts/{draftId}/reject
POST   /api/question-studio/drafts/{draftId}/quarantine
POST   /api/question-studio/drafts/{draftId}/restore
POST   /api/question-studio/import

Games / Learning
POST   /api/games/sessions
GET    /api/games/sessions/{sessionId}
POST   /api/games/sessions/{sessionId}/start
POST   /api/games/sessions/{sessionId}/submit
GET    /api/games/quiz/{documentId}
POST   /api/games/quiz/{documentId}/answers
GET    /api/games/flashcards/{documentId}
GET    /api/games/user/{userId}
POST   /api/learning/attempts
POST   /api/learning/tests/start
POST   /api/learning/tests/submit
GET    /api/learning/tests/document/{documentId}
GET    /api/learning/tests/summary/{documentId}
GET    /api/learning/progress/document/{documentId}
GET    /api/learning/review-queue/{documentId}
GET    /api/learning/progress/summary/{documentId}
GET    /api/learning/export/attempts.csv
GET    /api/learning/export/progress.csv
GET    /api/learning/export/test-results.csv

Analytics
GET    /api/analytics/personal
POST   /api/analytics/events

Slides
POST   /api/slides/generate/start
POST   /api/slides/folders/{folderId}/generate/start
GET    /api/slides/generate/progress/{jobId}
GET    /api/slides/document/{documentId}
GET    /api/slides/document/{documentId}/html
GET    /api/slides/folders/{folderId}
GET    /api/slides/folders/{folderId}/html
GET    /api/slides/{deckId}/export/html
GET    /api/slides/{deckId}/export/print
GET    /api/slides/{deckId}/export/pptx
PUT    /api/slides/{deckId}/items/{itemId}
POST   /api/slides/{deckId}/items/{itemId}/images/refresh
POST   /api/slides/{deckId}/items/{itemId}/images/select
```

## Verification Before Merge/Push

Backend:

```powershell
dotnet build ELearnGamePlatform.sln
```

Frontend:

```powershell
cd client
npm run build
```

Optional frontend tests:

```powershell
cd client
npm test -- --watchAll=false
```

## Current Limitations

- Not production-ready.
- Authentication is local/demo JWT only; refresh tokens, password reset, email verification, rate limiting, and complete audit logs are not implemented.
- Job progress is stored in memory, so restarting the backend can lose active job state.
- Background processing still uses simple runtime tasks, not a durable queue.
- AI quality depends on the local Ollama model, machine resources, and input document quality.
- Slide PDF export uses the browser Print/Save as PDF flow, not backend binary PDF rendering.
- PPTX export is basic and not pixel-perfect compared with HTML preview.

## Related Documentation

- [Docs Index](./docs/README.md)
- [Architecture](./docs/guides/ARCHITECTURE.md)
- [Run Guide](./docs/guides/RUN_GUIDE.md)
- [Frontend Handoff](./docs/guides/FRONTEND_HANDOFF.md)
- [Roadmap](./docs/guides/ROADMAP.md)
- [Agent Context](./docs/agent/PROJECT_CONTEXT.md)
