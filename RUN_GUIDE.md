# Quick Start - ELearn Game Platform

## 1. Start dependencies

### PostgreSQL

- Make sure PostgreSQL is running on `localhost:5432`
- Create database `ELearnGameDB`

### Ollama

```powershell
ollama pull llama3.2
ollama list
```

## 2. Start backend

```powershell
cd H:\pbl5\src\ELearnGamePlatform.API
dotnet run
```

Backend default URL:

- `http://localhost:5000`

Notes:

- EF Core migrations run automatically on startup
- Upload validation is enforced from `appsettings.json`

## 3. Start frontend

```powershell
cd H:\pbl5\client
npm install
npm start
```

Frontend default URL:

- `http://localhost:3000`

## 4. OCR notes

- `eng.traineddata` is required
- `vie.traineddata` is recommended for Vietnamese OCR
- For scanned PDFs, the app will try:
  - bundled Poppler in `poppler-25.12.0`
  - then `pdftoppm` from PATH

## 5. Recommended usage flow

1. Upload a document
2. Wait until status becomes `Completed`
3. Open `View Analysis`
4. Generate questions
5. Play Quiz or Flashcards

## 6. Current product limitations

- No real authentication yet
- Frontend still uses `demo-user`
- `local-store` JSON files are not the active runtime database
- AI generation quality depends on the local Ollama model and available machine resources
