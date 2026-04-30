# ELearn Game Platform — Staging

ELearn Game Platform là hệ thống biến tài liệu học tập thành trải nghiệm học tương tác. Người dùng có thể upload tài liệu, trích xuất nội dung bằng OCR/text extraction, phân tích bằng AI, sinh câu hỏi để học bằng quiz/flashcards và tạo slide để preview/chỉnh sửa trên web.

README này dành cho nhánh staging/test trước khi hợp vào nhánh chính. Mục tiêu của nhánh staging là gom các thay đổi đã tương đối ổn định, chạy build/test nhanh, kiểm tra luồng demo chính và phát hiện lỗi trước khi merge lên `main`.

> Lưu ý hiện tại: GitHub repo đang có nhánh dạng `staging/document-ingestion-slide-quality`, nên không thể tạo đồng thời nhánh tên đúng là `staging`. Trong lúc chờ dọn ref cũ, nhánh test staging đang dùng là `staging-test`.

---

## 1. Trạng thái sản phẩm

Repo hiện ở mức `MVP+`:

- Đã có luồng chính để demo.
- Chưa phải bản production-ready.
- Một số thành phần vẫn dùng dữ liệu/dev user tạm thời.
- Chất lượng AI phụ thuộc model local, RAM/CPU/GPU và chất lượng tài liệu đầu vào.

Các điểm chính đã có:

- Upload tài liệu: `PDF`, `DOCX`, `PNG`, `JPG`, `JPEG`.
- Trích xuất text từ PDF text-based.
- OCR cho ảnh và PDF scan.
- AI phân tích nội dung: summary, main topics, key points, language.
- Sinh câu hỏi tự động kèm progress polling.
- Quiz game.
- Flashcard game.
- Sinh slide từ tài liệu.
- Preview HTML cho slide deck.
- Chỉnh sửa slide item trong Slide Studio.
- Workspace/Folder Studio để gom nhiều nguồn tài liệu và sinh deck theo phạm vi chọn.

---

## 2. Nhánh staging dùng để test gì?

Trước khi merge staging lên `main`, cần kiểm tra tối thiểu các luồng sau:

1. Frontend build được.
2. Backend build được.
3. App chạy được trên local.
4. Upload tài liệu không lỗi.
5. Tài liệu chuyển được sang trạng thái `Completed`.
6. Xem analysis được.
7. Sinh câu hỏi được.
8. Chơi quiz/flashcards được.
9. Mở Slide Studio được.
10. Sinh slide deck được.
11. Mở Workspace/Folder Studio được.
12. Chọn scope nội dung trong Workspace/Folder Studio không vỡ giao diện.
13. Frontend không bị trắng trang ở các route chính.

---

## 3. Công nghệ đang dùng

### Backend

- ASP.NET Core 8 Web API
- Entity Framework Core 8
- PostgreSQL
- Npgsql
- Tesseract OCR
- ImageSharp
- PdfPig
- OpenXML
- Ollama local AI

### Frontend

- React 18
- React Router
- Axios
- React Scripts

---

## 4. Cấu trúc repo

```text
src/
  ELearnGamePlatform.API/             Web API, controllers, startup, appsettings, tessdata
  ELearnGamePlatform.Core/            Entities, enums, interfaces, shared utilities
  ELearnGamePlatform.Infrastructure/  EF Core, repositories, migrations, external integrations
  ELearnGamePlatform.Services/        OCR, document processing, AI services

client/                               React frontend

docs/
  guides/                             Tài liệu hướng dẫn và tham chiếu chính
  working-notes/                      Ghi chú thiết kế, checklist, research tạm thời

poppler-25.12.0/                      Poppler bundled cho OCR PDF scan
README.md
AGENTS.md
PLANS.md
```

---

## 5. Yêu cầu môi trường

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

Nếu thiếu `vie.traineddata`, OCR có thể fallback sang tiếng Anh và chất lượng đọc tiếng Việt sẽ giảm.

---

## 6. Model Ollama khuyến nghị

Cấu hình hiện tại dùng hướng multi-model:

```text
AnalysisModel     = qwen2.5-edu-json:latest
GenerationModel   = qwen3:14b
VerificationModel = qwen2.5-edu-json:latest
```

Chuẩn bị model:

```powershell
ollama pull qwen3:14b
ollama create qwen2.5-edu-json:latest -f qwen2.5-edu-json.modelfile
ollama list
```

Nếu máy yếu, có thể đổi model trong:

```text
src/ELearnGamePlatform.API/appsettings.json
```

---

## 7. Cấu hình chính

File cấu hình backend:

```text
src/ELearnGamePlatform.API/appsettings.json
```

Mẫu cấu hình local:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=ELearnGameDB;Username=postgres;Password=YOUR_PASSWORD;SslMode=disable"
  },
  "OllamaSettings": {
    "BaseUrl": "http://localhost:11434",
    "Model": "qwen3:14b",
    "AnalysisModel": "qwen2.5-edu-json:latest",
    "GenerationModel": "qwen3:14b",
    "VerificationModel": "qwen2.5-edu-json:latest",
    "TimeoutSeconds": 120,
    "KeepAlive": "15m",
    "EnableTimingLogs": true,
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

Frontend proxy hiện trỏ backend local:

```text
http://127.0.0.1:5000
```

---

## 8. Chạy nhanh trên local

### Bước 1: checkout nhánh staging/test

Nếu đã có nhánh `staging-test`:

```powershell
git fetch origin
git checkout staging-test
git pull
```

Nếu sau này đã dọn được ref và có nhánh `staging` đúng tên:

```powershell
git fetch origin
git checkout staging
git pull
```

### Bước 2: chạy PostgreSQL

Đảm bảo PostgreSQL đang chạy tại:

```text
localhost:5432
```

Database local:

```text
ELearnGameDB
```

Nếu chưa có database:

```sql
CREATE DATABASE "ELearnGameDB";
```

### Bước 3: chạy Ollama

```powershell
ollama list
```

Nếu chưa có model:

```powershell
ollama pull qwen3:14b
ollama create qwen2.5-edu-json:latest -f qwen2.5-edu-json.modelfile
```

### Bước 4: chạy backend

```powershell
cd H:\pbl5\src\ELearnGamePlatform.API
dotnet restore
dotnet run
```

Backend mặc định:

```text
http://localhost:5000
```

Swagger nếu bật development:

```text
http://localhost:5000/swagger
```

Nếu máy bị thiếu dung lượng ổ `C:` khi build/run, có thể dùng script:

```powershell
cd H:\pbl5\src\ELearnGamePlatform.API
.\run-h.ps1
```

Clear cache cũ rồi chạy lại:

```powershell
cd H:\pbl5\src\ELearnGamePlatform.API
.\run-h.ps1 -ClearOldCaches
```

### Bước 5: chạy frontend

Mở terminal khác:

```powershell
cd H:\pbl5\client
npm install
npm start
```

Frontend mặc định:

```text
http://localhost:3000
```

---

## 9. Lệnh test staging bắt buộc

Chạy từ root repo:

```powershell
git status
```

Backend build:

```powershell
dotnet build ELearnGamePlatform.sln
```

Frontend build:

```powershell
cd client
npm install
npm run build
```

Nếu muốn chạy test frontend:

```powershell
npm test
```

Nếu build fail, không merge staging lên `main`.

---

## 10. Checklist test thủ công trước khi merge

### 10.1 Backend

- [ ] `dotnet build ELearnGamePlatform.sln` pass.
- [ ] Backend chạy được tại `http://localhost:5000`.
- [ ] Swagger mở được nếu đang ở Development.
- [ ] Không lỗi connection string PostgreSQL.
- [ ] Không lỗi migration khi start app.
- [ ] Không lỗi Ollama connection nếu dùng chức năng AI.

### 10.2 Frontend

- [ ] `npm run build` pass.
- [ ] App mở được tại `http://localhost:3000`.
- [ ] Không trắng trang.
- [ ] Không lỗi console nghiêm trọng ở dashboard.
- [ ] Route `/workspaces` mở được.
- [ ] Route Workspace Studio mở được.
- [ ] Route Slide Studio mở được.

### Luồng demo

- [ ] Upload được PDF/DOCX/ảnh.
- [ ] Tài liệu xử lý đến `Completed`.
- [ ] Xem analysis được.
- [ ] Generate questions chạy được.
- [ ] Quiz chạy được.
- [ ] Flashcards chạy được.
- [ ] Generate slide deck chạy được.
- [ ] Preview slide deck không vỡ layout.
- [ ] Chọn scope trong Workspace/Folder Studio không vỡ layout.

---

## 11. Pipeline chính

### Document pipeline

1. Upload tài liệu.
2. Lưu metadata và file.
3. Trích xuất text:
   - PDF text-based -> direct extraction.
   - DOCX -> OpenXML.
   - Image/PDF scan -> Tesseract OCR.
4. Cleanup text sau OCR.
5. AI phân tích nội dung theo chunk.
6. Hợp nhất kết quả thành `ProcessedContent`.

### Question pipeline

1. Lập plan câu hỏi theo coverage.
2. Sinh question theo batch.
3. Polish output.
4. Verifier local + verifier AI.
5. Auto-repair 1 vòng nếu chất lượng chưa đạt.
6. Lưu xuống PostgreSQL.

### Slide pipeline

1. Tạo outline từ tài liệu/workspace.
2. Sinh nội dung từng slide.
3. Verifier local + verifier AI.
4. Auto-repair 1 vòng nếu cần.
5. Lưu `SlideDeck` + `SlideItem`.
6. Render HTML để preview.

---

## 12. API chính

### Documents

```text
POST   /api/documents/upload
GET    /api/documents/{id}
GET    /api/documents/user/{userId}
DELETE /api/documents/{id}
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
POST /api/games/sessions
GET  /api/games/sessions/{sessionId}
POST /api/games/sessions/{sessionId}/start
POST /api/games/sessions/{sessionId}/submit
GET  /api/games/quiz/{documentId}
GET  /api/games/flashcards/{documentId}
GET  /api/games/user/{userId}
```

### Slides

```text
POST /api/slides/generate/start
GET  /api/slides/generate/progress/{jobId}
GET  /api/slides/document/{documentId}
GET  /api/slides/document/{documentId}/html
PUT  /api/slides/{deckId}/items/{itemId}
```

---

## 13. Lỗi thường gặp

### Không tạo được nhánh `staging`

Lỗi:

```text
fatal: cannot lock ref 'refs/heads/staging': 'refs/heads/staging/document-ingestion-slide-quality' exists; cannot create 'refs/heads/staging'
```

Nguyên nhân: đang tồn tại nhánh dạng `staging/...`, nên Git không cho tạo nhánh cha tên `staging`.

Cách xử lý local:

```powershell
git branch -D staging/document-ingestion-slide-quality
```

Nếu remote cũng còn nhánh đó và đã thống nhất xóa:

```powershell
git push origin --delete staging/document-ingestion-slide-quality
```

Sau đó tạo lại:

```powershell
git checkout staging-test
git checkout -b staging
git push -u origin staging
```

### Đang có cherry-pick dở

Kiểm tra:

```powershell
git status
```

Hủy cherry-pick dở nếu cần:

```powershell
git cherry-pick --abort
```

### Frontend không gọi được backend

Kiểm tra backend đang chạy:

```powershell
curl http://localhost:5000
```

Kiểm tra proxy trong:

```text
client/package.json
```

### OCR tiếng Việt xấu

- Kiểm tra `vie.traineddata` đã có trong `src/ELearnGamePlatform.API/tessdata`.
- Dùng file scan rõ nét hơn.
- Với PDF scan, kiểm tra Poppler hoặc `pdftoppm`.

### Generate question/slide lỗi hoặc ra fallback

- Kiểm tra Ollama đang chạy.
- Kiểm tra model đã pull/create đúng.
- Kiểm tra document đã ở trạng thái `Completed`.
- Kiểm tra log backend.

---

## 14. Quy tắc merge staging

Chỉ merge staging lên `main` khi đạt tối thiểu:

- [ ] Không còn cherry-pick/rebase dở.
- [ ] Working tree sạch.
- [ ] Backend build pass.
- [ ] Frontend build pass.
- [ ] Luồng demo chính chạy được.
- [ ] Không có file cấu hình chứa secret thật.
- [ ] Không commit `node_modules`, `bin`, `obj`, file upload runtime hoặc cache cá nhân.

Lệnh kiểm tra nhanh:

```powershell
git status
git log --oneline --decorate -5
```

Merge sau khi test xong:

```powershell
git checkout main
git pull origin main
git merge staging
```

Nếu đang dùng `staging-test` thay cho `staging`:

```powershell
git checkout main
git pull origin main
git merge staging-test
```

---

## 15. Tài liệu liên quan

- [Docs Index](./docs/README.md)
- [Architecture](./docs/guides/ARCHITECTURE.md)
- [Run Guide](./docs/guides/RUN_GUIDE.md)
- [Frontend Handoff](./docs/guides/FRONTEND_HANDOFF.md)
- [Roadmap](./docs/guides/ROADMAP.md)
