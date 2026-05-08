# ELearn Game Platform

ELearn Game Platform là hệ thống biến tài liệu học tập thành trải nghiệm học tương tác. Người dùng có thể đăng ký/đăng nhập, upload tài liệu, trích xuất nội dung bằng OCR/text extraction, phân tích bằng AI local, sinh câu hỏi, học bằng quiz/flashcard/streak/test mode, theo dõi tiến độ học và tạo slide deck để preview, chỉnh sửa và export.

Sau PR #17, các nút export slide không còn là placeholder: hệ thống đã có export HTML, mở bản print-friendly để dùng Print / Save as PDF của trình duyệt, và export PPTX cơ bản.

Repo hiện ở mức **MVP+ phục vụ demo PBL**. Các flow chính đã có thể trình diễn end-to-end, nhưng hệ thống **chưa production-ready** vì vẫn còn nợ kỹ thuật ở persistent background jobs, test tự động, security hardening và polish UI/UX.

---

## 1. Trạng thái hiện tại

### Đã có thể demo

- **Authentication cơ bản bằng JWT**
  - Đăng ký, đăng nhập, lấy thông tin user hiện tại.
  - Frontend lưu token và gửi `Authorization: Bearer <token>` qua axios interceptor.
  - Các API chính yêu cầu xác thực.

- **Role cơ bản**
  - `ADMIN`
  - `INSTRUCTOR`
  - `LEARNER`

- **Admin overview**
  - Xem tổng quan users.
  - Xem danh sách tài liệu gần đây.

- **Document pipeline**
  - Upload `PDF`, `DOCX`, `PNG`, `JPG`, `JPEG`.
  - Validate file, size, extension và user hiện tại.
  - Lưu metadata/file.
  - OCR hoặc text extraction.
  - Cleanup text.
  - AI phân tích summary, topics, key points, language, structure và coverage metadata.
  - Progress polling cho document processing.

- **Question pipeline**
  - Sinh câu hỏi từ document.
  - Start job và poll progress.
  - Verifier local/AI.
  - Auto-repair một vòng khi output yếu.
  - Lưu câu hỏi xuống PostgreSQL.
  - CRUD cơ bản cho question.

- **Learning/game flows**
  - Quiz.
  - Flashcards.
  - Streak Mode.
  - Game session.
  - Learning attempt.
  - Practice test start/submit.
  - Learning progress/summary theo document.

- **Workspace/Folder flow**
  - Tạo workspace/folder.
  - Upload nhiều source vào workspace.
  - Chọn source/section dùng cho slide.
  - Sinh slide deck từ workspace/folder.

- **Slide Studio**
  - Sinh slide deck từ document.
  - Sinh slide deck từ workspace/folder nhiều nguồn.
  - Chọn phạm vi section/source trước khi sinh slide.
  - Poll progress khi generate slide.
  - Preview HTML.
  - Chỉnh sửa slide item.
  - Refresh/select image candidate cho slide.
  - Export HTML file.
  - Print / Save as PDF qua browser print flow.
  - Export PPTX basic.

- **PostgreSQL/EF Core**
  - PostgreSQL là runtime database chính.
  - EF Core Code First migrations.
  - JSONB dùng cho các trường dữ liệu phức tạp.

### Chưa production-ready

- Auth mới ở mức ứng dụng local/demo, chưa có refresh token, reset password, email verification, rate limit hoặc audit log đầy đủ.
- Job progress vẫn lưu trong memory. Restart backend có thể làm mất trạng thái job đang chạy.
- Background processing vẫn dựa trên `Task.Run`, chưa có queue bền vững như Hangfire/Quartz/Worker Service + persistent store.
- Chưa có test tự động đầy đủ cho toàn bộ core flow.
- Chưa có CI/CD verify chính thức trong repo.
- Chất lượng AI phụ thuộc model Ollama local, tài nguyên máy và chất lượng tài liệu đầu vào.
- UI/UX đã cải thiện nhưng vẫn cần polish thêm để demo mượt và đồng bộ hơn.

---

## 2. Công nghệ sử dụng

### Backend

- ASP.NET Core 8 Web API
- Entity Framework Core 8
- PostgreSQL + Npgsql
- JWT Bearer Authentication
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
- EF Core Code First migrations
- JSONB cho các trường như options, topics, key points, slide body, image candidates

---

## 3. Cấu trúc repo

```text
src/
  ELearnGamePlatform.API/             Web API, controllers, DI, appsettings, auth, job stores, tessdata
  ELearnGamePlatform.Core/            Entities, enums, interfaces, extensions, domain contracts
  ELearnGamePlatform.Infrastructure/  EF Core DbContext, repositories, migrations, Ollama integration
  ELearnGamePlatform.Services/        OCR, document processing, AI analysis/generation/verification, slide export

client/                               React frontend

docs/
  guides/                             Tài liệu hướng dẫn/chính thức
  working-notes/                      Ghi chú thiết kế, checklist, research tạm thời
  agent/                              Ngữ cảnh/rule cho agent hỗ trợ phát triển

poppler-25.12.0/                      Poppler bundled cho OCR PDF scan
README.md
AGENTS.md
PLANS.md
```

---

## 4. Yêu cầu môi trường

Cần cài trước:

- .NET SDK `9.0.306` theo `global.json` (projects target `net8.0`)
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

Với PDF scan, hệ thống ưu tiên Poppler bundled trong repo. Nếu không có Poppler bundled, hệ thống fallback sang `pdftoppm` trong `PATH`.

---

## 5. Cấu hình chính

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
  "JwtSettings": {
    "SecretKey": "CHANGE_THIS_TO_A_LONG_LOCAL_DEV_SECRET_KEY",
    "Issuer": "ELearnGamePlatform",
    "Audience": "ELearnGamePlatform.Client",
    "ExpirationMinutes": 10080
  },
  "AdminSeed": {
    "Enabled": true,
    "Email": "admin@example.com",
    "Password": "CHANGE_THIS_ADMIN_PASSWORD",
    "FullName": "System Admin"
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

Model mặc định nên dùng cho local/dev:

```text
qwen2.5:7b
```

Pull model:

```powershell
ollama pull qwen2.5:7b
ollama list
```

Nếu dùng image review trong slide image pipeline, có thể cần thêm model vision theo cấu hình hiện tại:

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

Kiểm tra connection string trong:

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
2. Đăng ký hoặc đăng nhập.
3. Upload một tài liệu PDF/DOCX/ảnh.
4. Đợi document xử lý xong và chuyển sang trạng thái completed.
5. Xem analysis/structure của tài liệu.
6. Sinh câu hỏi.
7. Chơi Quiz, Flashcards hoặc Streak Mode.
8. Làm Practice Test và xem learning progress/summary.
9. Mở Slide Studio.
10. Sinh slide deck từ một document.
11. Preview và chỉnh sửa slide item.
12. Export slide:
    - Download HTML.
    - Print / Save as PDF.
    - Download PPTX.
13. Tạo workspace/folder, upload nhiều nguồn, chọn phạm vi nội dung và sinh slide deck từ workspace.
14. Export slide deck sinh từ workspace/folder nếu có.

---

## 9. Pipeline chính

### Auth pipeline

1. User đăng ký hoặc đăng nhập.
2. Backend xác thực tài khoản và sinh JWT.
3. Frontend lưu token vào localStorage.
4. Axios interceptor gắn `Authorization: Bearer <token>` cho request sau đó.
5. Backend kiểm tra JWT qua middleware authentication/authorization.

### Document pipeline

1. Upload tài liệu.
2. Validate file, size, extension và user hiện tại.
3. Tạo hoặc lấy default workspace của user.
4. Lưu metadata/file vào hệ thống.
5. Trích xuất text:
   - PDF text-based -> PdfPig/direct extraction.
   - DOCX -> OpenXML.
   - Image/PDF scan -> Tesseract OCR.
6. Cleanup text sau OCR.
7. AI phân tích nội dung theo chunk.
8. Lưu summary, topics, key points, language, structure và coverage metadata vào PostgreSQL.
9. Frontend poll progress để cập nhật trạng thái.

### Question pipeline

1. Frontend gọi API start generation.
2. Backend tạo job state trong memory.
3. Background task sinh câu hỏi bằng Ollama.
4. Chạy kiểm tra chất lượng/verifier.
5. Auto-repair một vòng nếu output yếu.
6. Lưu câu hỏi xuống PostgreSQL.
7. Frontend poll progress và lấy câu hỏi theo document.

### Learning/game pipeline

1. User chọn mode học hoặc test.
2. Backend lấy question theo document và mode.
3. User trả lời.
4. Frontend submit answer/session/test result.
5. Backend ghi learning attempt/progress/test result.
6. Frontend hiển thị kết quả, điểm và summary.

### Slide pipeline

1. Chọn document hoặc workspace/folder.
2. Chọn số slide, theme, audience, tone, narrative goal và scope nội dung.
3. Sinh outline.
4. Sinh từng slide item.
5. Verifier local + AI verifier.
6. Auto-repair nếu cần.
7. Tìm/chọn media cho slide nếu image pipeline bật.
8. Lưu `SlideDeck` + `SlideItem`.
9. Render HTML để preview.
10. Cho phép chỉnh sửa slide item và chọn lại image candidate.
11. Export theo deck: HTML, Print / Save as PDF, PPTX basic.

---

## 10. Slide export sau PR #17

Các export action trong Slide Studio hiện đã là flow thật thay vì nút giả/placeholder.

### Format hỗ trợ

- **HTML file download**
  - Tải deck hiện tại thành file `.html` độc lập.

- **Print / Save as PDF**
  - Backend trả về bản HTML print-friendly.
  - Frontend mở bản này ở tab mới.
  - Người dùng dùng hộp thoại in của browser để `Save as PDF`.
  - Đây không phải binary `.pdf` do backend render.

- **PPTX basic**
  - Backend xuất file `.pptx` bằng OpenXML.
  - Đủ dùng cho demo và mở bằng PowerPoint/LibreOffice.
  - Không đảm bảo pixel-perfect so với HTML preview.

### Endpoint export

```text
GET /api/slides/{deckId}/export/html
GET /api/slides/{deckId}/export/print
GET /api/slides/{deckId}/export/pptx
```

### Auth/ownership

- Export endpoint yêu cầu JWT Bearer token.
- Backend kiểm tra deck thuộc về user hiện tại thông qua document hoặc workspace/folder owner.
- User khác không được export deck không thuộc quyền sở hữu của mình.

### Giới hạn của export

- PDF dùng browser Print / Save as PDF, không phải file PDF do backend render trực tiếp.
- PPTX basic chưa pixel-perfect so với HTML preview.
- PPTX chưa embed image candidate/local image đầy đủ.
- Speaker notes trong PPTX hiện render như text box nhỏ, chưa phải PowerPoint presenter notes pane.
- HTML/print export dùng CSS inline tối thiểu, không thay thế hoàn toàn preview/editor trên web.

---

## 11. API chính

Tất cả endpoint chính, trừ auth login/register, yêu cầu JWT Bearer token.

### Auth

```text
POST   /api/auth/register
POST   /api/auth/login
GET    /api/auth/me
```

### Admin

```text
GET    /api/admin/overview
```

### Documents

```text
POST   /api/documents/upload
GET    /api/documents/{id}
GET    /api/documents/{id}/progress
GET    /api/documents/{id}/structure
POST   /api/documents/{id}/analyze-structure
GET    /api/documents/user/{userId}
DELETE /api/documents/{id}
```

### Workspaces / Folders

```text
POST   /api/workspaces
GET    /api/workspaces/user/{userId}
GET    /api/workspaces/default/user/{userId}
GET    /api/workspaces/{id}
DELETE /api/workspaces/{id}
POST   /api/workspaces/{id}/sources/upload
GET    /api/workspaces/{id}/sources
PUT    /api/workspaces/{id}/sources/{sourceId}/slide-selection
```

Folder aliases vẫn tồn tại cho một số flow cũ:

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
POST   /api/games/quiz/{documentId}/answers
GET    /api/games/flashcards/{documentId}
GET    /api/games/user/{userId}
```

### Learning

```text
POST   /api/learning/attempts
POST   /api/learning/tests/start
POST   /api/learning/tests/submit
GET    /api/learning/tests/document/{documentId}
GET    /api/learning/tests/summary/{documentId}
GET    /api/learning/progress/document/{documentId}
GET    /api/learning/progress/summary/{documentId}
GET    /api/learning/export/attempts.csv
GET    /api/learning/export/progress.csv
GET    /api/learning/export/test-results.csv
```

### Slides

```text
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

---

## 12. Lệnh kiểm tra trước khi merge/push

Chạy từ root repo:

```powershell
git status
dotnet restore
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

## 13. Lỗi thường gặp

### Backend không kết nối được PostgreSQL

- Kiểm tra PostgreSQL đang chạy.
- Kiểm tra database `ELearnGameDB` đã tồn tại.
- Kiểm tra username/password trong connection string.

```powershell
psql -U postgres -d ELearnGameDB
```

### Backend không start vì JWT config

- Kiểm tra `JwtSettings.SecretKey` trong `appsettings.json`.
- Secret key không được rỗng.
- Nên dùng chuỗi đủ dài cho môi trường local/dev.

### Migration lỗi khi start backend

Backend tự chạy EF Core migrations khi start. Nếu schema lệch, kiểm tra migration và database hiện tại:

```powershell
cd src\ELearnGamePlatform.API
dotnet ef migrations list --project ..\ELearnGamePlatform.Infrastructure
dotnet ef database update --project ..\ELearnGamePlatform.Infrastructure
```

### Frontend bị đá về login hoặc API trả 401

- Kiểm tra đã đăng nhập chưa.
- Kiểm tra token trong localStorage còn hợp lệ không.
- Logout/login lại nếu token cũ bị lệch secret hoặc hết hạn.
- Kiểm tra backend đang dùng đúng `JwtSettings`.

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

### Export slide lỗi 401/403

- Kiểm tra đã đăng nhập chưa.
- Kiểm tra deck có thuộc user hiện tại không.
- Export endpoint cần JWT token; frontend đã dùng blob request để gắn Bearer token.
- Nếu mở export URL trực tiếp trên tab mới, browser có thể không tự gắn Authorization header.

### File PPTX mở lỗi hoặc thiếu style

- Kiểm tra deck có slide item hợp lệ không.
- PPTX hiện là export cơ bản, chưa pixel-perfect.
- Thử mở bằng PowerPoint hoặc LibreOffice.
- Nếu vẫn lỗi, kiểm tra log backend trong `SlideExportService`.

### Progress job biến mất sau khi restart backend

Đây là giới hạn hiện tại. Job store vẫn nằm trong memory, nên restart backend có thể làm mất progress job đang chạy. Kết quả đã persist xuống database thì vẫn còn, nhưng trạng thái job runtime có thể mất.

---

## 14. Giới hạn hiện tại

- Chưa production-ready.
- Auth đã có ở mức JWT cơ bản, nhưng chưa hardening đầy đủ cho production.
- Chưa có refresh token, reset password, email verification, rate limit và audit log.
- Job progress store vẫn là in-memory.
- Background job hiện dùng `Task.Run`, chưa phải queue bền vững.
- Chưa có test tự động đầy đủ cho toàn bộ core flows.
- Chưa có CI/CD verify chính thức.
- Chất lượng AI phụ thuộc model local, tài nguyên máy và chất lượng tài liệu đầu vào.
- Không nên xem `local-store` hoặc dữ liệu mẫu là runtime source chính.
- Slide export đã có thật, nhưng PPTX/PDF vẫn ở mức demo-friendly, chưa phải export production-grade.

---

## 15. Ưu tiên tiếp theo

Thứ tự nên xử lý tiếp:

1. Polish UI/UX cho dashboard, document detail, question review, learning modes và Slide Studio.
2. Chuyển job state/progress sang persistent store.
3. Thay `Task.Run` bằng background worker/queue rõ ràng hơn.
4. Bổ sung test tự động cho auth, upload, question generation, game/learning và slide generation/export.
5. Hoàn thiện security hardening cho auth.
6. Thêm benchmark/timing log có hệ thống cho OCR, analysis, question và slide pipeline.
7. Hoàn thiện slide templates và game mode nâng cao.

---

## 16. Tài liệu liên quan

- [Docs Index](./docs/README.md)
- [Architecture](./docs/guides/ARCHITECTURE.md)
- [Run Guide](./docs/guides/RUN_GUIDE.md)
- [Frontend Handoff](./docs/guides/FRONTEND_HANDOFF.md)
- [Slide Export](./docs/guides/SLIDE_EXPORT.md)
- [Roadmap](./docs/guides/ROADMAP.md)
- [Agent Context](./docs/agent/PROJECT_CONTEXT.md)

---

## 17. Kết luận trạng thái project

Nên xem repo hiện tại như:

- một bản **MVP+ có thể demo end-to-end**;
- đã vượt qua giai đoạn chỉ có upload/quiz/flashcard đơn giản;
- đã có auth, workspace, learning progress, Slide Studio và slide export thật;
- chưa phải hệ thống production-ready;
- cần ưu tiên ổn định job, test, security hardening và polish UI/UX trước khi mở rộng thêm nhiều tính năng mới.
