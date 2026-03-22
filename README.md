# ELearn Game Platform

## Tong quan

ELearn Game Platform la mot MVP bien tai lieu hoc tap thanh trai nghiem hoc tuong tac:

- Upload tai lieu
- OCR / trich xuat text
- AI phan tich noi dung
- Sinh cau hoi
- Hoc bang quiz va flashcards

Repo hien tai dung:

- Backend: .NET 8 Web API
- Database: PostgreSQL + EF Core
- AI: Ollama (`llama3.2`)
- OCR: Tesseract
- Frontend: React 18

## Hien trang dung voi code runtime

- Runtime hien tai dung PostgreSQL, khong dung MongoDB.
- `FileUpload` da duoc enforce tu `appsettings.json` o server-side.
- Repo co thu muc `local-store/`, nhung day chi la du lieu mau tham khao; runtime hien tai khong dung no lam data source.
- OCR tieng Viet can `vie.traineddata`. Neu file nay chua co, he thong se fallback sang OCR tieng Anh va log canh bao.
- OCR PDF scan can `pdftoppm`. Service se uu tien Poppler bundled neu tim thay trong repo; neu khong se fallback sang `pdftoppm` trong PATH.
- Frontend hien tai van dang o muc MVP va dang hardcode `demo-user`.

## Tinh nang da co

- Upload file `PDF`, `DOCX`, `PNG`, `JPG`, `JPEG`
- Trich xuat text tu PDF text-based
- OCR cho image va PDF scan
- AI phan tich:
  - main topics
  - key points
  - summary
  - language
- Sinh cau hoi voi progress polling
- Quiz game
- Flashcards

## Cau truc repo

```text
src/
  ELearnGamePlatform.API/             Web API, controllers, startup
  ELearnGamePlatform.Core/            Entities, interfaces
  ELearnGamePlatform.Infrastructure/  EF Core, repositories, external services
  ELearnGamePlatform.Services/        OCR, document processing, AI services
client/                               React frontend
poppler-25.12.0/                      Poppler binaries bundled cho OCR PDF scan
ROADMAP.md                            Roadmap va backlog san pham
```

## Yeu cau moi truong

- .NET SDK 8.0+
- Node.js 18+
- PostgreSQL 14+
- Ollama
- Model `llama3.2`
- Tesseract tessdata:
  - bat buoc: `eng.traineddata`
  - khuyen nghi: `vie.traineddata`

## Chay nhanh

1. Chay PostgreSQL va tao database `ELearnGameDB`
2. Chay Ollama va pull model:

```powershell
ollama pull llama3.2
ollama list
```

3. Chay backend:

```powershell
cd src/ELearnGamePlatform.API
dotnet run
```

Neu `C:` bi day do `NuGet cache`, `Temp`, hoac `MSBuild temp`, dung script sau de day toan bo du lieu build tam sang `H:\pbl5\.runtime-h`:

```powershell
cd src/ELearnGamePlatform.API
.\run-h.ps1
```

Neu muon clear cache cu roi restore lai tren `H:`:

```powershell
cd src/ELearnGamePlatform.API
.\run-h.ps1 -ClearOldCaches
```

4. Chay frontend:

```powershell
cd client
npm install
npm start
```

Mac dinh:

- Backend: `http://localhost:5000`
- Frontend: `http://localhost:3000`

Backend se tu chay EF Core migrations khi start.

## Cau hinh chinh

File: `src/ELearnGamePlatform.API/appsettings.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=ELearnGameDB;Username=postgres;Password=..."
  },
  "OllamaSettings": {
    "BaseUrl": "http://localhost:11434",
    "Model": "llama3.2",
    "TimeoutSeconds": 60,
    "Temperature": 0.3
  },
  "FileUpload": {
    "MaxFileSizeInMB": 50,
    "AllowedExtensions": [".pdf", ".docx", ".png", ".jpg", ".jpeg"]
  }
}
```

## API chinh

- `POST /api/documents/upload`
- `GET /api/documents/{id}`
- `GET /api/documents/user/{userId}`
- `DELETE /api/documents/{id}`
- `POST /api/questions/generate/start`
- `GET /api/questions/generate/progress/{jobId}`
- `POST /api/questions/generate`
- `GET /api/questions/document/{documentId}`
- `GET /api/games/quiz/{documentId}`
- `GET /api/games/flashcards/{documentId}`

## Tai lieu lien quan

- [RUN_GUIDE.md](./RUN_GUIDE.md)
- [HUONG_DAN_CHAY.md](./HUONG_DAN_CHAY.md)
- [ARCHITECTURE.md](./ARCHITECTURE.md)
- [ROADMAP.md](./ROADMAP.md)
