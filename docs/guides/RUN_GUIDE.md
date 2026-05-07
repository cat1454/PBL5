# Quick Start - ELearn Game Platform

Verified from source: 2026-05-07.

## 1. Environment

- .NET SDK pinned by `global.json`: `9.0.306`
- Backend target framework: `net8.0`
- Node.js `18+`
- PostgreSQL `14+`
- Ollama
- Tesseract tessdata:
  - `eng.traineddata` required
  - `vie.traineddata` recommended for Vietnamese OCR

## 2. Prepare Ollama Models

Current backend config in `src/ELearnGamePlatform.API/appsettings.json` expects:

- `Model = qwen2.5:7b`
- `AnalysisModel = qwen2.5:7b`
- `GenerationModel = qwen2.5:7b`
- `VerificationModel = qwen2.5:7b`

Pull the default model:

```powershell
cd H:\pbl5
ollama pull qwen2.5:7b
ollama list
```

If slide image review is enabled, the current image pipeline config may also use:

```powershell
ollama pull qwen2.5-vl:3b
```

`qwen2.5-edu-json.modelfile` is an optional local model recipe. It is not the default unless you change `appsettings.json`.

## 3. Start Dependencies

### PostgreSQL

- Make sure PostgreSQL is running on `localhost:5432`.
- Create database `ELearnGameDB` if it does not exist.
- Check the connection string in `src/ELearnGamePlatform.API/appsettings.json`.

### OCR Assets

- Put `eng.traineddata` and optionally `vie.traineddata` in `src/ELearnGamePlatform.API/tessdata`.
- For scanned PDFs the app tries bundled Poppler in `poppler-25.12.0`, then `pdftoppm` from `PATH`.

## 4. Restore Local Tools

The repo has a local tool manifest at `dotnet-tools.json`.

```powershell
cd H:\pbl5
dotnet tool restore
dotnet ef --version
```

Expected EF tool version: `8.0.0`.

## 5. Start Backend

```powershell
cd H:\pbl5\src\ELearnGamePlatform.API
dotnet restore
dotnet build
dotnet run
```

Backend default URL:

- `http://localhost:5000`

Swagger:

- `http://localhost:5000/swagger`

Notes:

- EF Core migrations run automatically on startup.
- Upload validation is enforced from `appsettings.json`.
- If build fails with `MSB3021` or `MSB3027`, stop the currently running API process because the DLL is locked.

## 6. Start Frontend

```powershell
cd H:\pbl5\client
npm install
npm start
```

Frontend default URL:

- `http://localhost:3000`

Frontend proxy:

- `http://127.0.0.1:5000`

## 7. Smoke Test Flow

1. Register or log in.
2. Upload a document.
3. Check document progress through `GET /api/documents/{id}/progress`.
4. Wait until the document is completed.
5. Generate questions with `POST /api/questions/generate/start`.
6. Poll `GET /api/questions/generate/progress/{jobId}`.
7. Generate slides with `POST /api/slides/generate/start` or `POST /api/slides/folders/{folderId}/generate/start`.
8. Poll `GET /api/slides/generate/progress/{jobId}`.

## 8. Current Notes

- Basic JWT auth is implemented; the frontend sends bearer tokens through Axios.
- Job state is still in memory.
- Background work still uses `Task.Run`.
- Progress payload is standardized for document, question, and slide flows.
- The active frontend handoff is documented in `FRONTEND_HANDOFF.md`.
