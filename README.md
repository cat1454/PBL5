# ELearn Game Platform

ELearn Game Platform là hệ thống biến tài liệu học tập thành trải nghiệm học tương tác. Người dùng có thể upload tài liệu, trích xuất nội dung bằng OCR/text extraction, phân tích bằng AI local, sinh câu hỏi để học bằng quiz/flashcards/streak và tạo slide deck để preview/chỉnh sửa trên web.

Repo hiện ở mức **MVP+**: đã có đủ luồng chính để demo và phát triển tiếp, nhưng chưa phải bản production-ready.

---

## 1. Tính năng chính

- Upload tài liệu: `PDF`, `DOCX`, `PNG`, `JPG`, `JPEG`.
- Trích xuất text từ PDF text-based.
- OCR cho ảnh và PDF scan.
- Phân tích nội dung bằng AI:
  - tóm tắt nội dung;
  - chủ đề chính;
  - ý chính;
  - ngôn ngữ;
  - metadata/coverage map phục vụ sinh slide.
- Sinh câu hỏi tự động kèm progress polling.
- Học bằng nhiều chế độ:
  - Quiz;
  - Flashcards;
  - Streak Mode.
- Tạo slide deck từ tài liệu hoặc workspace/folder.
- Preview HTML cho slide deck.
- Chỉnh sửa từng slide item trong Slide Studio.
- Tìm/chọn media cho slide theo image pipeline.
- Workspace/Folder Studio để gom nhiều nguồn tài liệu và chọn phạm vi nội dung trước khi sinh slide.

---

## 2. Công nghệ sử dụng

### Backend

- ASP.NET Core 8 Web API
- Entity Framework Core 8
- PostgreSQL + Npgsql
- Tesseract OCR
- ImageSharp
- PdfPig
- OpenXML
- Ollama local AI

### Frontend

- React 18
- React Router DOM 6
- Axios
- React Scripts
- React Icons

### Database

- PostgreSQL 14+
- EF Core migrations tự động chạy khi backend start.

---

## 3. Cấu trúc repo

```text
src/
  ELearnGamePlatform.API/             Web API, controllers, DI, appsettings, tessdata
  ELearnGamePlatform.Core/            Entities, enums, interfaces, extensions
  ELearnGamePlatform.Infrastructure/  EF Core, repositories, migrations, external integrations
  ELearnGamePlatform.Services/        OCR, document processing, AI services

client/                               React frontend

docs/
  guides/                             Tài liệu hướng dẫn/chính thức
  working-notes/                      Ghi chú thiết kế, checklist, research tạm thời

poppler-25.12.0/                      Poppler bundled cho OCR PDF scan
README.md
AGENTS.md
PLANS.md
```

---

## 4. Yêu cầu môi trường

Cần cài trước:

- .NET SDK 8.0+
- Node.js 18+
- PostgreSQL 14+
- Ollama
- Tesseract OCR
- Git

OCR service mặc định tìm tessdata tại:

```text
src/ELearnGamePlatform.API/tessdata
```

Tối thiểu cần:

```text
eng.traineddata
```

Khuyến nghị thêm nếu xử lý tài liệu tiếng Việt:

```text
vie.traineddata
```

Với PDF scan, hệ thống ưu tiên Poppler bundled trong repo. Nếu không có, hệ thống fallback sang `pdftoppm` trong `PATH`.

---

## 5. Cấu hình chính

File cấu hình backend:

```text
src/ELearnGamePlatform.API/appsettings.json
```

Mẫu cấu hình local nên dùng:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=ELearnGameDB;Username=postgres;Password=YOUR_PASSWORD;SslMode=disable"
  },
  "OllamaSettings": {
    "BaseUrl": "http://localhost:11434",
    "Model": "qwen2.5:7b",
    "AnalysisModel": "qwen2.5:7b",
    "GenerationModel": "qwen2.5:7b",
    "VerificationModel": "qwen2.5:7b",
    "TimeoutSeconds": 300,
    "KeepAlive": "30m",
    "EnableTimingLogs": true,
    "Temperature": 0.4,
    "AnalysisTemperature": 0.2,
    "GenerationTemperature": 0.5,
    "VerificationTemperature": 0.1
  },
  "FileUpload": {
    "MaxFileSizeInMB": 50,
    "AllowedExtensions": [".pdf", ".docx", ".png", ".jpg", ".jpeg"]
  }
}
```

Frontend proxy hiện trỏ về backend local:

```text
http://127.0.0.1:5000
```

Không commit mật khẩu thật, API key thật hoặc cấu hình cá nhân lên repo.

---

## 6. Chuẩn bị Ollama model

Model mặc định đang dùng trong cấu hình local:

```text
qwen2.5:7b
```

Pull model:

```powershell
ollama pull qwen2.5:7b
ollama list
```

Nếu dùng image review trong slide image pipeline, có thể cần thêm model vision theo cấu hình:

```powershell
ollama pull qwen2.5-vl:3b
```

Nếu máy yếu, có thể đổi model trong:

```text
src/ELearnGamePlatform.API/appsettings.json
```

---

## 7. Chạy nhanh trên local

### Bước 1: clone và checkout main

```powershell
git clone https://github.com/cat1454/PBL5.git
cd PBL5
git checkout main
git pull origin main
```

### Bước 2: chuẩn bị PostgreSQL

Đảm bảo PostgreSQL đang chạy tại:

```text
localhost:5432
```

Tạo database nếu chưa có:

```sql
CREATE DATABASE "ELearnGameDB";
```

Kiểm tra lại connection string trong:

```text
src/ELearnGamePlatform.API/appsettings.json
```

### Bước 3: chuẩn bị Ollama

```powershell
ollama pull qwen2.5:7b
ollama list
```

### Bước 4: kiểm tra OCR assets

Đặt file tessdata vào:

```text
src/ELearnGamePlatform.API/tessdata
```

Nên có:

```text
eng.traineddata
vie.traineddata
```

### Bước 5: chạy backend

```powershell
cd src\ELearnGamePlatform.API
dotnet restore
dotnet run
```

Backend mặc định:

```text
http://localhost:5000
```

Swagger:

```text
http://localhost:5000/swagger
```

Nếu máy bị thiếu dung lượng ổ `C:` khi build/run, có thể dùng script:

```powershell
.\run-h.ps1
```

Clear cache cũ rồi chạy lại:

```powershell
.\run-h.ps1 -ClearOldCaches
```

### Bước 6: chạy frontend

Mở terminal khác:

```powershell
cd client
npm install
npm start
```

Frontend mặc định:

```text
http://localhost:3000
```

---

## 8. Luồng demo đề xuất

1. Mở frontend tại `http://localhost:3000`.
2. Upload một tài liệu PDF/DOCX/ảnh.
3. Đợi tài liệu xử lý xong và chuyển sang trạng thái `Completed`.
4. Xem phần analysis.
5. Sinh câu hỏi.
6. Chơi Quiz, Flashcards hoặc Streak Mode.
7. Mở Slide Studio.
8. Sinh slide deck.
9. Preview/chỉnh sửa slide.
10. Tạo workspace/folder, upload nhiều nguồn và sinh slide theo phạm vi chọn.

---

## 9. Pipeline chính

### Document pipeline

1. Upload tài liệu.
2. Validate file, size, extension và `userId`.
3. Lưu metadata/file vào hệ thống.
4. Trích xuất text:
   - PDF text-based -> PdfPig/direct extraction;
   - DOCX -> OpenXML;
   - image/PDF scan -> Tesseract OCR.
5. Cleanup text sau OCR.
6. AI phân tích nội dung theo chunk.
7. Lưu summary, topics, key points, language và coverage metadata vào PostgreSQL.

### Question pipeline

1. Frontend gọi API start generation.
2. Backend tạo job state trong memory.
3. Background task sinh câu hỏi bằng Ollama.
4. Chạy kiểm tra chất lượng/verifier.
5. Auto-repair 1 vòng nếu output yếu.
6. Lưu câu hỏi xuống PostgreSQL.
7. Frontend poll progress và lấy câu hỏi theo document.

### Slide pipeline

1. Chọn tài liệu hoặc workspace/folder.
2. Chọn số slide, theme, audience, tone và scope nội dung.
3. Sinh outline.
4. Sinh từng slide item.
5. Verifier local + AI verifier.
6. Auto-repair nếu cần.
7. Tìm/chọn media cho slide nếu image pipeline bật.
8. Lưu `SlideDeck` + `SlideItem`.
9. Render HTML để preview.

---

## 10. API chính

### Documents

```text
POST   /api/documents/upload
GET    /api/documents/{id}
GET    /api/documents/user/{userId}
GET    /api/documents/{id}/progress
DELETE /api/documents/{id}
```

### Folders / Workspaces

```text
POST   /api/folders
GET    /api/folders/user/{userId}
GET    /api/folders/{id}
DELETE /api/folders/{id}
POST   /api/folders/{id}/sources/upload
GET    /api/folders/{id}/sources
PUT    /api/folders/{id}/sources/{sourceId}/slide-selection
```

### Questions

```text
POST   /api/questions/generate/start
GET    /api/questions/generate/progress/{jobId}
POST   /api/questions/generate
GET    /api/questions/document/{documentId}
GET    /api/questions/{id}
PUT    /api/questions/{id}
DELETE /api/questions/{id}
```

### Games

```text
POST   /api/games/sessions
GET    /api/games/sessions/{sessionId}
POST   /api/games/sessions/{sessionId}/start
POST   /api/games/sessions/{sessionId}/submit
GET    /api/games/quiz/{documentId}
GET    /api/games/flashcards/{documentId}
GET    /api/games/user/{userId}
```

### Slides

```text
POST   /api/slides/generate/start
GET    /api/slides/generate/progress/{jobId}
GET    /api/slides/document/{documentId}
GET    /api/slides/document/{documentId}/html
GET    /api/slides/folders/{folderId}
GET    /api/slides/folders/{folderId}/html
PUT    /api/slides/{deckId}/items/{itemId}
POST   /api/slides/{deckId}/items/{itemId}/images/refresh
POST   /api/slides/{deckId}/items/{itemId}/images/select
```

---

## 11. Lệnh kiểm tra trước khi merge/push

Chạy từ root repo:

```powershell
git status
dotnet build ELearnGamePlatform.sln
```

Frontend:

```powershell
cd client
npm install
npm run build
```

Nếu cần chạy test frontend:

```powershell
npm test
```

Không merge/push nếu backend hoặc frontend build fail.

---

## 12. Lỗi thường gặp

### Backend không kết nối được PostgreSQL

- Kiểm tra PostgreSQL đang chạy.
- Kiểm tra database `ELearnGameDB` đã tồn tại.
- Kiểm tra username/password trong connection string.

```powershell
psql -U postgres -d ELearnGameDB
```

### Migration lỗi khi start backend

Backend tự chạy EF Core migrations khi start. Nếu schema lệch, kiểm tra migration và database hiện tại:

```powershell
cd src\ELearnGamePlatform.API
dotnet ef migrations list --project ..\ELearnGamePlatform.Infrastructure
dotnet ef database update --project ..\ELearnGamePlatform.Infrastructure
```

### Frontend không gọi được backend

- Kiểm tra backend chạy tại `http://localhost:5000`.
- Kiểm tra proxy trong `client/package.json`.
- Kiểm tra CORS trong `Program.cs`.

```powershell
curl http://localhost:5000/swagger
```

### OCR tiếng Việt xấu

- Thêm `vie.traineddata` vào `src/ELearnGamePlatform.API/tessdata`.
- Dùng file scan rõ hơn.
- Với PDF scan, kiểm tra Poppler hoặc `pdftoppm`.

### Generate question/slide lỗi hoặc ra fallback

- Kiểm tra Ollama đang chạy.
- Kiểm tra model đã pull đúng.
- Kiểm tra document đã xử lý xong.
- Xem log backend để biết lỗi ở OCR, analysis, generation hay verifier.

```powershell
ollama list
curl http://localhost:11434/api/tags
```

---

## 13. Giới hạn hiện tại

- Chưa có authentication/authorization thật sự.
- Frontend vẫn đang dùng user demo/hardcoded ở một số luồng.
- Job progress store vẫn là in-memory, restart backend có thể mất progress job đang chạy.
- Background job hiện dùng `Task.Run`, chưa phải hàng đợi bền vững.
- Chưa có test tự động đầy đủ cho toàn bộ core flows.
- Chất lượng AI phụ thuộc model local, tài nguyên máy và chất lượng tài liệu đầu vào.
- Không nên xem `local-store` hoặc dữ liệu mẫu là runtime source chính.

---

## 14. Tài liệu liên quan

- [Docs Index](./docs/README.md)
- [Architecture](./docs/guides/ARCHITECTURE.md)
- [Run Guide](./docs/guides/RUN_GUIDE.md)
- [Frontend Handoff](./docs/guides/FRONTEND_HANDOFF.md)
- [Roadmap](./docs/guides/ROADMAP.md)

---

## 15. Trạng thái project

Nên xem repo hiện tại như:

- bản MVP+ để demo PBL;
- nền tảng để tiếp tục polish UI/UX;
- nền tảng để mở rộng game mode, slide templates, benchmark AI/OCR, auth và persistent jobs.
