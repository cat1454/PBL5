# ELearn Game Platform

ELearn Game Platform là MVP `.NET + React` cho PBL5, tập trung vào một luồng học tập đầy đủ: upload tài liệu, OCR/text extraction, phân tích bằng AI local, sinh câu hỏi, học bằng quiz/flashcard/streak/practice test, theo dõi tiến độ, quản lý workspace/folder và tạo slide deck để preview/chỉnh sửa/export.

Trạng thái hiện tại: **MVP+ phục vụ demo và public test nhẹ**. Các luồng chính đã có thể chạy end-to-end, nhưng hệ thống chưa production-ready vì job progress còn lưu trong RAM, background jobs còn demo-oriented, chất lượng AI phụ thuộc Ollama/local machine, test coverage chưa bao phủ hết và security hardening vẫn cần làm thêm.

Tài liệu ngôn ngữ cũ vẫn được giữ để tham khảo:

- [English](./README.en.md)
- [Tiếng Việt](./README.vi.md)
- [日本語](./README.ja.md)

## Tính Năng Hiện Có

- **Auth JWT**: đăng ký, đăng nhập, lấy current user, phân quyền cơ bản `ADMIN`, `INSTRUCTOR`, `LEARNER`.
- **Workspace-first dashboard**: dashboard, workspace list, workspace studio, upload nhiều nguồn học tập và chọn nguồn/section cho slide.
- **Document pipeline**: upload `PDF`, `DOCX`, `PNG`, `JPG`, `JPEG`; validate file; extract text bằng PdfPig/OpenXML/Tesseract; OCR fallback cho PDF scan; cleanup text; phân tích summary, topic, key point, language, structure và coverage metadata.
- **Question generation**: sinh câu hỏi từ tài liệu, job progress, verifier, auto-repair JSON/output yếu, lưu question bank vào PostgreSQL.
- **Question Studio V2**: tạo run sinh draft, review/edit/accept/reject/quarantine/restore, import draft đạt chuẩn vào question bank.
- **Study flows**: Study Hub, quiz, flashcards, streak mode, game sessions, practice tests, review queue, learning attempts và progress summary.
- **Personal analytics**: dashboard hoạt động học thật, heatmap 12 tháng, skill/radar, checklist, recent activity và action context.
- **Slide Studio**: sinh slide từ document hoặc workspace/folder, chọn scope, preview HTML, edit slide item, refresh/select image candidates, export HTML, mở bản print-friendly để Save as PDF, export PPTX basic.
- **Public test deployment**: có Dockerfile backend/frontend, `docker-compose.yml`, PostgreSQL container, backend gọi Ollama trên host và hướng dẫn Cloudflare Tunnel trong `DEPLOY_UBUNTU.md`.

## Tech Stack

### Backend

- ASP.NET Core Web API, target `net8.0`
- .NET SDK pinned trong `global.json`: `9.0.306`
- Entity Framework Core 8 + PostgreSQL/Npgsql
- JWT Bearer Authentication
- Tesseract OCR, PdfPig, OpenXML, ImageSharp
- Ollama local AI; config hiện tại dùng model nhẹ `qwen3:4b`

### Frontend

- React 18
- React Router DOM 6
- Axios
- React Scripts
- React Icons

### Runtime Mặc Định

- Backend local: `http://localhost:5000`
- Swagger local/dev: `http://localhost:5000/swagger`
- Frontend dev: `http://localhost:3000`
- Frontend dev proxy: `http://127.0.0.1:5000`
- Frontend production fallback: `https://pbl5-api.danangtoiiu.live/api`
- Database: PostgreSQL qua EF Core migrations

## Cấu Trúc Repo

```text
src/
  ELearnGamePlatform.API/             Web API, controllers, DI, config, auth, runtime uploads
  ELearnGamePlatform.Core/            Entities, enums, interfaces, shared contracts
  ELearnGamePlatform.Infrastructure/  DbContext, migrations, repositories, Ollama integration
  ELearnGamePlatform.Services/        OCR, document processing, AI, question, slide services

client/
  public/                             Static frontend entrypoint
  src/                                React app, routes, components, services, i18n, styles

tests/                                .NET service/regression tests
docs/                                 Guides, research, working notes, agent context
benchmarks/                           OCR/document benchmark harness
scripts/                              Repo helper scripts
tools/                                Local repair/report tooling
```

Các thư mục như `uploads/`, `logs/`, `tessdata/*.traineddata`, `.tmp/`, `artifacts/`, `publish/`, ảnh snapshot và build output là runtime/generated/local assets, không phải source chính.

## Yêu Cầu Môi Trường

Cài trước:

- .NET SDK `9.0.306`
- Node.js 18+
- PostgreSQL 14+ hoặc Docker
- Ollama
- Tesseract OCR
- Git

OCR service tìm tessdata tại:

```text
src/ELearnGamePlatform.API/tessdata
```

Nên có tối thiểu:

```text
eng.traineddata
vie.traineddata
```

Với PDF scan, app sẽ ưu tiên Poppler local nếu có `poppler-25.12.0/Library/bin/pdftoppm.exe`, sau đó fallback sang `pdftoppm` trong `PATH`.

## Cấu Hình Local

Backend đọc cấu hình chính từ:

```text
src/ELearnGamePlatform.API/appsettings.json
```

Không commit mật khẩu/API key thật. Khi chạy local hoặc deploy, dùng environment variables/user secrets nếu cần override config.

Ví dụ placeholder:

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
    "Model": "qwen3:4b",
    "AnalysisModel": "qwen3:4b",
    "GenerationModel": "qwen3:4b",
    "VerificationModel": "qwen3:4b"
  }
}
```

## Chạy Local

Tạo database PostgreSQL:

```sql
CREATE DATABASE "ELearnGameDB";
```

Chuẩn bị Ollama:

```powershell
ollama pull qwen3:4b
ollama list
```

Chạy backend:

```powershell
cd src\ELearnGamePlatform.API
dotnet restore
dotnet run
```

Backend sẽ tự chạy EF Core migrations khi startup và seed admin nếu `AdminSeed` được bật.

Chạy frontend ở terminal khác:

```powershell
cd client
npm install
npm start
```

Mở app:

```text
http://localhost:3000
```

## Chạy Public Test Nhẹ Bằng Docker

Public test hiện được thiết kế theo mô hình:

- `pbl5.danangtoiiu.live` -> frontend React container
- `pbl5-api.danangtoiiu.live` -> ASP.NET Core API container
- PostgreSQL chạy bằng Docker
- Ollama chạy trực tiếp trên Ubuntu host
- Backend container gọi Ollama qua `http://host.docker.internal:11434`

Tóm tắt:

```bash
cp .env.example .env
# sửa POSTGRES_PASSWORD, JWT_SECRET, ADMIN_PASSWORD
ollama pull qwen3:4b
docker compose up -d --build
```

Chi tiết xem [DEPLOY_UBUNTU.md](./DEPLOY_UBUNTU.md).

## Luồng Demo Đề Xuất

1. Đăng ký hoặc đăng nhập.
2. Mở dashboard/workspace mặc định.
3. Upload tài liệu PDF/DOCX/ảnh.
4. Đợi OCR + analysis hoàn tất.
5. Xem OCR text, analysis và document structure.
6. Mở Question Studio hoặc sinh câu hỏi trực tiếp.
7. Review/import draft đạt chuẩn vào question bank.
8. Học bằng quiz, flashcards, streak hoặc practice test.
9. Xem analytics/progress cá nhân.
10. Mở Slide Studio, chọn scope, sinh deck, preview và chỉnh sửa.
11. Export HTML, dùng browser Print/Save as PDF hoặc export PPTX basic.

## API Chính

Hầu hết endpoint yêu cầu JWT Bearer token, trừ login/register.

```text
Auth:             /api/auth/*
Admin:            /api/admin/*
Documents:        /api/documents/*
Workspaces:       /api/workspaces/*
Folders:          /api/folders/*
Questions:        /api/questions/*
Question Studio:  /api/question-studio/*
Games:            /api/games/*
Learning:         /api/learning/*
Analytics:        /api/analytics/*
Slides:           /api/slides/*
```

Các endpoint được frontend dùng tập trung trong:

```text
client/src/services/api.js
```

## Kiểm Tra Trước Khi Push/Merge

Backend:

```powershell
dotnet build ELearnGamePlatform.sln
```

Frontend:

```powershell
cd client
npm run build
```

Tests đáng chú ý:

```powershell
dotnet test tests\ELearnGamePlatform.Services.Tests\ELearnGamePlatform.Services.Tests.csproj
cd client
npm test -- --watchAll=false
```

Với docs-only change như README, có thể bỏ qua build/test nhưng cần inspect Markdown sau khi sửa.

## Giới Hạn Hiện Tại

- Chưa production-ready.
- JWT auth còn ở mức local/demo; chưa có refresh token, password reset, email verification, rate limiting đầy đủ.
- Job progress đang lưu in-memory, restart backend có thể mất trạng thái job đang chạy.
- Background processing vẫn dựa trên runtime task đơn giản, chưa có durable queue.
- AI quality phụ thuộc model Ollama, tài nguyên máy và chất lượng tài liệu đầu vào.
- Document Understanding nâng cao/vision đang tắt mặc định trong config runtime.
- Slide PDF export dùng browser Print/Save as PDF, không phải backend binary PDF renderer.
- PPTX export ở mức basic, chưa pixel-perfect so với HTML preview.
- Public test chỉ phù hợp demo nhẹ, không nên dùng cho tải lớn hoặc nhiều job AI song song.

## Tài Liệu Liên Quan

- [Docs Index](./docs/README.md)
- [Architecture](./docs/guides/ARCHITECTURE.md)
- [Run Guide](./docs/guides/RUN_GUIDE.md)
- [Frontend Handoff](./docs/guides/FRONTEND_HANDOFF.md)
- [Deploy Ubuntu](./DEPLOY_UBUNTU.md)
- [Project Context](./docs/agent/PROJECT_CONTEXT.md)
