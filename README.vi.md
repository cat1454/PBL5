# ELearn Game Platform

ELearn Game Platform là MVP `.NET + React` giúp biến tài liệu học tập thành trải nghiệm học tương tác. Hệ thống hỗ trợ upload tài liệu, OCR/text extraction, phân tích bằng AI local, sinh câu hỏi, học qua quiz/flashcard/streak/practice test, theo dõi tiến độ, quản lý workspace/folder và tạo slide deck để preview, chỉnh sửa, export.

Repo hiện ở mức **MVP+ phục vụ demo PBL**. Các luồng chính có thể demo end-to-end, nhưng chưa production-ready vì job progress vẫn in-memory, background processing còn demo-oriented, test coverage chưa đầy đủ và security hardening chưa hoàn chỉnh.

## Tính năng hiện có

- **Authentication JWT**: đăng ký, đăng nhập, lấy thông tin user hiện tại, phân quyền cơ bản `ADMIN`, `INSTRUCTOR`, `LEARNER`.
- **Document pipeline**: upload `PDF`, `DOCX`, `PNG`, `JPG`, `JPEG`; validate file; trích xuất text bằng PdfPig/OpenXML/Tesseract; cleanup OCR; phân tích summary, topics, key points, language, structure và coverage metadata bằng Ollama.
- **Question pipeline**: sinh câu hỏi từ document, chạy job progress, verifier, auto-repair một vòng khi output yếu, lưu câu hỏi vào PostgreSQL.
- **Question Studio**: tạo run sinh draft, review draft, edit/accept/reject/quarantine/restore, import draft đạt yêu cầu vào question bank.
- **Learning flows**: quiz, flashcards, streak mode, game session, practice test, learning attempts, review queue và progress summary theo document.
- **Analytics**: dashboard cá nhân tổng hợp hoạt động học, heatmap, skill/radar, checklist và recent activity từ dữ liệu thật.
- **Workspace/Folder**: tạo workspace/folder, upload nhiều source, chọn source/section cho slide, quản lý tài liệu theo không gian học.
- **Slide Studio**: sinh slide từ document hoặc workspace/folder, chọn scope nội dung, preview HTML, chỉnh sửa slide item, refresh/select image candidate, export HTML, mở bản print-friendly để Save as PDF, và export PPTX basic.

## Công nghệ

### Backend

- ASP.NET Core Web API, project target `net8.0`
- .NET SDK pin bằng `global.json`: `9.0.306`
- Entity Framework Core 8
- PostgreSQL + Npgsql
- JWT Bearer Authentication
- Tesseract OCR, PdfPig, OpenXML, ImageSharp
- Ollama local AI, mặc định `qwen2.5:7b`

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
- Database: PostgreSQL qua EF Core migrations

## Cấu trúc repo

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

Các thư mục/file như `artifacts/`, `.artifacts/`, `.tmp/`, `commit-history/`, `src/ELearnGamePlatform.API/uploads/`, `src/ELearnGamePlatform.API/logs/`, `src/ELearnGamePlatform.API/tessdata/*.traineddata`, `poppler-25.12.0/` là dữ liệu local/generated hoặc runtime asset, không nên xem là source chính.

## Yêu cầu môi trường

Cài trước:

- .NET SDK `9.0.306`
- Node.js 18+
- PostgreSQL 14+
- Ollama
- Tesseract OCR
- Git

OCR service mặc định tìm tessdata tại:

```text
src/ELearnGamePlatform.API/tessdata
```

Tối thiểu nên có:

```text
eng.traineddata
vie.traineddata
```

Với PDF scan, hệ thống dùng Poppler local nếu có `poppler-25.12.0/Library/bin/pdftoppm.exe`; nếu không, fallback sang `pdftoppm` trong `PATH`.

## Cấu hình local

Backend đọc cấu hình chính từ:

```text
src/ELearnGamePlatform.API/appsettings.json
```

Ví dụ cấu hình local, chỉ dùng placeholder:

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

Không commit mật khẩu thật, API key thật hoặc cấu hình cá nhân lên repo.

## Chạy local

Clone repo:

```powershell
git clone https://github.com/cat1454/PBL5.git
cd PBL5
```

Tạo database PostgreSQL:

```sql
CREATE DATABASE "ELearnGameDB";
```

Chuẩn bị model Ollama:

```powershell
ollama pull qwen2.5:7b
ollama list
```

Chạy backend:

```powershell
cd src\ELearnGamePlatform.API
dotnet restore
dotnet run
```

Chạy frontend ở terminal khác:

```powershell
cd client
npm install
npm start
```

Mở app tại:

```text
http://localhost:3000
```

## Luồng demo đề xuất

1. Đăng ký hoặc đăng nhập.
2. Tạo workspace hoặc dùng default workspace.
3. Upload tài liệu PDF/DOCX/ảnh.
4. Đợi document processing hoàn tất.
5. Xem analysis/structure/OCR text.
6. Mở Question Studio hoặc sinh câu hỏi.
7. Review/import question draft nếu dùng Question Studio.
8. Học bằng quiz, flashcards, streak hoặc practice test.
9. Xem analytics/progress.
10. Mở Slide Studio, chọn scope, sinh deck, preview và chỉnh sửa.
11. Export deck bằng HTML, browser Print/Save as PDF, hoặc PPTX basic.

## API chính

Hầu hết endpoint yêu cầu JWT Bearer token, trừ login/register.

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

## Kiểm tra trước khi merge/push

Backend:

```powershell
dotnet build ELearnGamePlatform.sln
```

Frontend:

```powershell
cd client
npm run build
```

Nếu cần test frontend:

```powershell
cd client
npm test -- --watchAll=false
```

## Giới hạn hiện tại

- Chưa production-ready.
- Auth mới ở mức JWT local/demo, chưa có refresh token, reset password, email verification, rate limit hoặc audit log đầy đủ.
- Job progress đang lưu in-memory, restart backend có thể mất trạng thái job đang chạy.
- Background processing vẫn dựa trên task runtime đơn giản, chưa có queue bền vững.
- Chất lượng AI phụ thuộc model Ollama local, tài nguyên máy và chất lượng tài liệu đầu vào.
- PDF export của slide dùng browser Print/Save as PDF, không phải backend render binary PDF.
- PPTX export ở mức basic, chưa pixel-perfect so với HTML preview.

## Tài liệu liên quan

- [Docs Index](./docs/README.md)
- [Architecture](./docs/guides/ARCHITECTURE.md)
- [Run Guide](./docs/guides/RUN_GUIDE.md)
- [Frontend Handoff](./docs/guides/FRONTEND_HANDOFF.md)
- [Roadmap](./docs/guides/ROADMAP.md)
- [Agent Context](./docs/agent/PROJECT_CONTEXT.md)
