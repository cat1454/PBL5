# Setup Guide - ELearn Game Platform

Verified from source: 2026-05-07.

## System Requirements

1. .NET SDK `9.0.306` as pinned by `global.json`. The projects target `net8.0`.
2. PostgreSQL `14+`.
3. Ollama.
4. Node.js `18+`.
5. Tesseract OCR.

## 1. Setup PostgreSQL

Make sure PostgreSQL is running on `localhost:5432`.

Create the database if needed:

```powershell
psql -U postgres
CREATE DATABASE "ELearnGameDB";
\q
```

Check the runtime connection string in:

```text
src/ELearnGamePlatform.API/appsettings.json
```

Current runtime uses PostgreSQL + EF Core migrations. MongoDB is not part of the active runtime.

## 2. Setup Ollama

The current default model story is:

- `Model`: `qwen2.5:7b`
- `AnalysisModel`: `qwen2.5:7b`
- `GenerationModel`: `qwen2.5:7b`
- `VerificationModel`: `qwen2.5:7b`

Pull and test the model:

```powershell
ollama pull qwen2.5:7b
ollama run qwen2.5:7b "Hello"
```

Optional for slide image review if enabled:

```powershell
ollama pull qwen2.5-vl:3b
```

`qwen2.5-edu-json.modelfile` is optional/local. It is not the default model unless `appsettings.json` is changed.

## 3. Setup Tesseract OCR

Create or verify:

```powershell
cd H:\pbl5
mkdir src\ELearnGamePlatform.API\tessdata
```

Add:

- `eng.traineddata`
- `vie.traineddata` for Vietnamese OCR

For scanned PDFs, the app first tries bundled Poppler under `poppler-25.12.0`, then falls back to `pdftoppm` from `PATH`.

## 4. Restore Tools and Build Backend

Use the repo local tool manifest. Do not install a global EF tool unless you intentionally want one outside this repo.

```powershell
cd H:\pbl5
dotnet tool restore
dotnet ef --version
dotnet restore
dotnet build ELearnGamePlatform.sln
```

Expected local `dotnet-ef` version: `8.0.0`.

## 5. Run Database Migrations

The API runs EF Core migrations automatically on startup.

Manual update is useful when you want to prepare or repair the database without starting the API:

```powershell
cd H:\pbl5\src\ELearnGamePlatform.API
dotnet ef database update --project ..\ELearnGamePlatform.Infrastructure
dotnet ef migrations list --project ..\ELearnGamePlatform.Infrastructure
```

## 6. Run API

```powershell
cd H:\pbl5\src\ELearnGamePlatform.API
dotnet run
```

Backend:

- `http://localhost:5000`

Swagger:

- `http://localhost:5000/swagger`

There is no active HTTPS `5001` URL in `Program.cs`.

## 7. Run Frontend

```powershell
cd H:\pbl5\client
npm install
npm start
```

Frontend:

- `http://localhost:3000`

Frontend proxy in `client/package.json`:

- `http://127.0.0.1:5000`

## Runtime Config Example

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=ELearnGameDB;Username=postgres;Password=YOUR_PASSWORD;SslMode=disable"
  },
  "JwtSettings": {
    "Issuer": "ELearnGamePlatform",
    "Audience": "ELearnGamePlatform.Client",
    "SecretKey": "CHANGE_THIS_LOCAL_DEV_SECRET",
    "ExpirationMinutes": 10080
  },
  "OllamaSettings": {
    "BaseUrl": "http://localhost:11434",
    "Model": "qwen2.5:7b",
    "AnalysisModel": "qwen2.5:7b",
    "GenerationModel": "qwen2.5:7b",
    "VerificationModel": "qwen2.5:7b",
    "TimeoutSeconds": 300
  }
}
```

## API Endpoints

### Auth

- `POST /api/auth/register`
- `POST /api/auth/login`
- `GET /api/auth/me`

### Documents

- `POST /api/documents/upload`
- `GET /api/documents/{id}`
- `GET /api/documents/{id}/progress`
- `GET /api/documents/{id}/structure`
- `POST /api/documents/{id}/analyze-structure`
- `GET /api/documents/user/{userId}`
- `DELETE /api/documents/{id}`

### Questions

- `POST /api/questions/generate/start`
- `GET /api/questions/generate/progress/{jobId}`
- `POST /api/questions/generate` legacy/synchronous
- `GET /api/questions/document/{documentId}`
- `GET /api/questions/{id}`
- `PUT /api/questions/{id}`
- `DELETE /api/questions/{id}`

### Games and Learning

- `POST /api/games/sessions`
- `GET /api/games/sessions/{sessionId}`
- `POST /api/games/sessions/{sessionId}/start`
- `POST /api/games/sessions/{sessionId}/submit`
- `GET /api/games/quiz/{documentId}`
- `POST /api/games/quiz/{documentId}/answers`
- `GET /api/games/flashcards/{documentId}`
- `GET /api/games/user/{userId}`
- `POST /api/learning/attempts`
- `POST /api/learning/tests/start`
- `POST /api/learning/tests/submit`
- `GET /api/learning/tests/document/{documentId}`
- `GET /api/learning/tests/summary/{documentId}`
- `GET /api/learning/progress/document/{documentId}`
- `GET /api/learning/progress/summary/{documentId}`
- `GET /api/learning/export/attempts.csv`
- `GET /api/learning/export/progress.csv`
- `GET /api/learning/export/test-results.csv`

### Workspaces, Folders, Slides

- `POST /api/workspaces`
- `GET /api/workspaces/user/{userId}`
- `GET /api/workspaces/default/user/{userId}`
- `GET /api/workspaces/{id}`
- `DELETE /api/workspaces/{id}`
- `POST /api/workspaces/{id}/sources/upload`
- `GET /api/workspaces/{id}/sources`
- `PUT /api/workspaces/{id}/sources/{sourceId}/slide-selection`
- `POST /api/folders`
- `GET /api/folders/user/{userId}`
- `GET /api/folders/{id}`
- `DELETE /api/folders/{id}`
- `POST /api/folders/{id}/sources/upload`
- `GET /api/folders/{id}/sources`
- `PUT /api/folders/{id}/sources/{sourceId}/slide-selection`
- `POST /api/slides/generate/start`
- `POST /api/slides/folders/{folderId}/generate/start`
- `GET /api/slides/generate/progress/{jobId}`
- `GET /api/slides/document/{documentId}`
- `GET /api/slides/document/{documentId}/html`
- `GET /api/slides/folders/{folderId}`
- `GET /api/slides/folders/{folderId}/html`
- `GET /api/slides/{deckId}/export/html`
- `GET /api/slides/{deckId}/export/print`
- `GET /api/slides/{deckId}/export/pptx`
- `PUT /api/slides/{deckId}/items/{itemId}`
- `POST /api/slides/{deckId}/items/{itemId}/images/refresh`
- `POST /api/slides/{deckId}/items/{itemId}/images/select`

## Troubleshooting

### PostgreSQL connection failed

```powershell
Get-Service postgresql*
psql -U postgres -d ELearnGameDB -c "SELECT version();"
```

### Migration errors

```powershell
cd H:\pbl5\src\ELearnGamePlatform.API
dotnet ef migrations list --project ..\ELearnGamePlatform.Infrastructure
dotnet ef database update --project ..\ELearnGamePlatform.Infrastructure
```

### Ollama errors

```powershell
ollama list
curl http://localhost:11434/api/tags
```

### Backend build errors from locked DLL

Stop the running `ELearnGamePlatform.API` process, then rebuild:

```powershell
dotnet build ELearnGamePlatform.sln
```

## Next Steps

- [ ] Move job progress/state from memory to persistent storage.
- [ ] Replace `Task.Run` jobs with a durable queue or worker service.
- [ ] Add production auth hardening: refresh tokens, password reset, email verification, rate limiting, audit logs.
- [ ] Add broader automated tests for auth, upload, question generation, learning, and slide generation.
- [ ] Add production deployment hardening and health checks.
- [ ] Continue UI/UX polish for dashboard, learning modes, and Slide Studio.
