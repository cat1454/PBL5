# ELearn Game Platform

ELearn Game Platform la mot he thong bien tai lieu hoc tap thanh trai nghiem hoc tuong tac. Nguoi dung co the upload tai lieu, trich xuat noi dung bang OCR/text extraction, phan tich bang AI, sinh cau hoi de hoc bang quiz/flashcards, va tao slide de preview tren web.

Repo hien tai dang o muc `MVP+`: da co du luong chinh de demo va phat trien tiep, nhung chua phai ban production-ready.

## 1. He thong dang lam duoc gi

- Upload tai lieu: `PDF`, `DOCX`, `PNG`, `JPG`, `JPEG`
- Trich xuat text tu PDF text-based
- OCR cho image va PDF scan
- AI phan tich noi dung de lay:
  - `summary`
  - `main topics`
  - `key points`
  - `language`
- Sinh cau hoi tu dong voi progress polling
- Quiz game
- Flashcard game
- Sinh slide tu tai lieu
- Preview HTML cho slide deck
- Chinh sua slide item tren Slide Studio

## 2. Pipeline hien tai

### Document pipeline

1. Upload tai lieu
2. Luu metadata va file
3. Trich xuat text:
   - PDF text -> direct extraction
   - DOCX -> OpenXML
   - image / PDF scan -> Tesseract OCR
4. Cleanup text sau OCR
5. AI phan tich noi dung theo chunk
6. Hop nhat ket qua thanh `ProcessedContent`

### Question pipeline

1. Lap plan cau hoi theo coverage
2. Sinh question theo batch
3. Polish output
4. Verifier local + verifier AI
5. Auto-repair 1 vong neu chat luong chua dat
6. Luu xuong PostgreSQL

### Slide pipeline

1. Tao outline tu tai lieu
2. Sinh noi dung tung slide
3. Verifier local + verifier AI
4. Auto-repair 1 vong neu can
5. Luu `SlideDeck` + `SlideItem`
6. Render HTML de preview

## 3. Diem noi bat ky thuat

- OCR da duoc nang cap theo huong "uu tien text sach va on dinh":
  - multi-pass OCR
  - nhieu bien the tien xu ly anh
  - fallback cho PDF scan
  - cleanup text hau OCR
- He thong AI da tach profile model:
  - `AnalysisModel`
  - `GenerationModel`
  - `VerificationModel`
- Question va slide khong con la one-shot generation don gian:
  - co verifier
  - co score
  - co auto-repair neu output bi yeu

## 4. Cong nghe dang dung

- Backend: ASP.NET Core 8 Web API
- Data access: Entity Framework Core 8
- Database: PostgreSQL
- OCR: Tesseract + ImageSharp
- PDF text extraction: PdfPig
- DOCX processing: OpenXML
- AI local: Ollama
- Frontend: React 18 + React Router + Axios

## 5. Cau truc repo

```text
src/
  ELearnGamePlatform.API/             Web API, controllers, startup, appsettings, tessdata
  ELearnGamePlatform.Core/            Entities, enums, interfaces, shared utilities
  ELearnGamePlatform.Infrastructure/  EF Core, repositories, external integrations
  ELearnGamePlatform.Services/        OCR, document processing, AI services
client/                               React frontend
poppler-25.12.0/                      Poppler bundled cho OCR PDF scan
README.md
ARCHITECTURE.md
RUN_GUIDE.md
ROADMAP.md
```

## 6. Yeu cau moi truong

- .NET SDK 8.0+
- Node.js 18+
- PostgreSQL 14+
- Ollama
- Tesseract language data

OCR service mac dinh tim `tessdata` trong:

- `src/ELearnGamePlatform.API/tessdata`

Can toi thieu:

- `eng.traineddata`

Khuyen nghi rat cao:

- `vie.traineddata`

Neu thieu `vie.traineddata`, OCR se fallback sang tieng Anh va log canh bao.

## 7. Model Ollama mac dinh hien tai

Repo hien tai dang cau hinh theo huong multi-model:

- `AnalysisModel = qwen2.5:7b`
- `GenerationModel = qwen3:8b`
- `VerificationModel = qwen2.5:7b`

Pull nhanh:

```powershell
ollama pull qwen2.5:7b
ollama pull qwen3:8b
ollama list
```

Neu muon doi model, sua trong:

- `src/ELearnGamePlatform.API/appsettings.json`

## 8. Chay nhanh

### Buoc 1: chay PostgreSQL

- Dam bao PostgreSQL dang chay tren `localhost:5432`
- Tao database `ELearnGameDB`

### Buoc 2: chuan bi Ollama

```powershell
ollama pull qwen2.5:7b
ollama pull qwen3:8b
ollama list
```

### Buoc 3: kiem tra OCR assets

- Dat `eng.traineddata` va `vie.traineddata` vao:
  - `src/ELearnGamePlatform.API/tessdata`
- OCR PDF scan se uu tien Poppler bundled trong repo.
- Neu khong tim thay Poppler bundled, he thong se fallback sang `pdftoppm` trong `PATH`.

### Buoc 4: chay backend

```powershell
cd H:\pbl5\src\ELearnGamePlatform.API
dotnet run
```

Backend mac dinh:

- `http://localhost:5000`

Ghi chu:

- EF Core migrations se duoc apply tu dong khi start app.
- Upload validation duoc doc tu `appsettings.json`.

Neu may gap van de dung luong o `C:` khi build/run, co the dung:

```powershell
cd H:\pbl5\src\ELearnGamePlatform.API
.\run-h.ps1
```

Neu muon clear cache cu roi chay lai:

```powershell
cd H:\pbl5\src\ELearnGamePlatform.API
.\run-h.ps1 -ClearOldCaches
```

### Buoc 5: chay frontend

```powershell
cd H:\pbl5\client
npm install
npm start
```

Frontend mac dinh:

- `http://localhost:3000`

## 9. Luong su dung de demo

1. Upload mot tai lieu
2. Doi trang thai xu ly xong
3. Xem `View Analysis`
4. Sinh cau hoi
5. Choi `Quiz` hoac `Flashcards`
6. Mo `Slide Studio`
7. Sinh va preview slide deck

## 10. Cau hinh chinh

File:

- `src/ELearnGamePlatform.API/appsettings.json`

Mau cau hinh:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=ELearnGameDB;Username=postgres;Password=YOUR_PASSWORD;SslMode=disable"
  },
  "OllamaSettings": {
    "BaseUrl": "http://localhost:11434",
    "Model": "qwen3:8b",
    "AnalysisModel": "qwen2.5:7b",
    "GenerationModel": "qwen3:8b",
    "VerificationModel": "qwen2.5:7b",
    "TimeoutSeconds": 120,
    "Temperature": 0.3,
    "AnalysisTemperature": 0.15,
    "GenerationTemperature": 0.35,
    "VerificationTemperature": 0.05
  },
  "FileUpload": {
    "MaxFileSizeInMB": 50,
    "AllowedExtensions": [".pdf", ".docx", ".png", ".jpg", ".jpeg"]
  }
}
```

## 11. API chinh

### Documents

- `POST /api/documents/upload`
- `GET /api/documents/{id}`
- `GET /api/documents/user/{userId}`
- `DELETE /api/documents/{id}`

### Questions

- `POST /api/questions/generate/start`
- `GET /api/questions/generate/progress/{jobId}`
- `POST /api/questions/generate`
- `GET /api/questions/document/{documentId}`
- `GET /api/questions/{id}`
- `PUT /api/questions/{id}`
- `DELETE /api/questions/{id}`

### Games

- `POST /api/games/sessions`
- `GET /api/games/sessions/{sessionId}`
- `POST /api/games/sessions/{sessionId}/start`
- `POST /api/games/sessions/{sessionId}/submit`
- `GET /api/games/quiz/{documentId}`
- `GET /api/games/flashcards/{documentId}`
- `GET /api/games/user/{userId}`

### Slides

- `POST /api/slides/generate/start`
- `GET /api/slides/generate/progress/{jobId}`
- `GET /api/slides/document/{documentId}`
- `GET /api/slides/document/{documentId}/html`
- `PUT /api/slides/{deckId}/items/{itemId}`

## 12. Gioi han hien tai

- Chua co auth that su
- Frontend van dang dung `demo-user`
- Job progress store van la in-memory
- Background jobs van dua tren `Task.Run`
- Chua co test tu dong day du
- Chat luong AI phu thuoc vao model local va tai nguyen may
- Slide templates va game mode mo rong van dang trong roadmap

## 13. Trang thai san pham

Nen xem repo hien tai nhu:

- mot ban `MVP+` de demo
- mot nen tang tot de phat trien tiep UI/UX, game mode moi, slide templates, benchmark AI/OCR, va hardening he thong

Thu tu uu tien gan nhat da duoc cap nhat trong:

- [ROADMAP.md](./ROADMAP.md)

## 14. Tai lieu lien quan

- [ARCHITECTURE.md](./ARCHITECTURE.md)
- [RUN_GUIDE.md](./RUN_GUIDE.md)
- [HUONG_DAN_CHAY.md](./HUONG_DAN_CHAY.md)
- [ROADMAP.md](./ROADMAP.md)
