# Quick Start - ELearn Game Platform

## 1. Environment

- .NET SDK is pinned by `global.json` to `9.0.203`
- Node.js `18+`
- PostgreSQL `14+`
- Ollama
- Tesseract tessdata:
  - `eng.traineddata` required
  - `vie.traineddata` recommended for Vietnamese OCR

## 2. Prepare Ollama models

Current backend config expects:

- `AnalysisModel = qwen2.5-edu-json:latest`
- `GenerationModel = qwen3:14b`
- `VerificationModel = qwen2.5-edu-json:latest`

Build/pull them:

```powershell
cd H:\pbl5
ollama pull qwen3:14b
ollama create qwen2.5-edu-json:latest -f qwen2.5-edu-json.modelfile
ollama list
```

If you change model names in `src/ELearnGamePlatform.API/appsettings.json`, update these commands to match.

## 3. Start dependencies

### PostgreSQL

- Make sure PostgreSQL is running on `localhost:5432`
- Create database `ELearnGameDB`

### OCR assets

- Put `eng.traineddata` and `vie.traineddata` in `src/ELearnGamePlatform.API/tessdata`
- For scanned PDFs the app tries:
  - bundled Poppler in `poppler-25.12.0`
  - then `pdftoppm` from `PATH`

## 4. Start backend

```powershell
cd H:\pbl5\src\ELearnGamePlatform.API
dotnet restore
dotnet build
dotnet run
```

Backend default URL:

- `http://localhost:5000`

Notes:

- EF Core migrations run automatically on startup
- Upload validation is enforced from `appsettings.json`
- If build fails with `MSB3021/MSB3027`, stop the currently running `ELearnGamePlatform.API` process first because the DLL is locked

## 5. Start frontend

```powershell
cd H:\pbl5\client
npm install
npm start
```

Frontend default URL:

- `http://localhost:3000`

## 6. Smoke test flow

1. Upload a document
2. Check document progress via `GET /api/documents/{id}/progress`
3. Wait until the document is `Completed`
4. Generate questions and poll `GET /api/questions/generate/progress/{jobId}`
5. Generate slides and poll `GET /api/slides/generate/progress/{jobId}`

## 7. Current notes

- `demo-user` is still being used by the frontend
- Job state is still in memory
- Progress payload is now standardized for document, question, and slide flows
- The active frontend handoff is documented in `FRONTEND_HANDOFF.md`
