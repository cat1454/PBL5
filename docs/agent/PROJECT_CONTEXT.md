# PROJECT_CONTEXT

## Mục tiêu tài liệu

Tài liệu này tóm tắt trạng thái thực tế hiện tại của PBL5 để Codex đọc nhanh trước khi sửa. Khi có khác biệt giữa tài liệu và source code, ưu tiên source code runtime.

## Kiến trúc hiện tại

- `src/ELearnGamePlatform.API`
  - ASP.NET Core Web API entrypoint
  - DI, auth, config binding, controller, runtime uploads
- `src/ELearnGamePlatform.Core`
  - entity domain, enum, interface, shared helpers
- `src/ELearnGamePlatform.Infrastructure`
  - `ApplicationDbContext`, migrations, repository, `OllamaService`, config classes
- `src/ELearnGamePlatform.Services`
  - OCR, document processing, AI analysis, question generation, slide generation
- `client`
  - React 18 app với React Router, Axios, i18n song ngữ

## Runtime truth

- Backend URL: `http://localhost:5000`
  - nguồn: `src/ELearnGamePlatform.API/Program.cs`
- Frontend proxy: `http://127.0.0.1:5000`
  - nguồn: `client/package.json`
- Database: PostgreSQL qua EF Core
  - nguồn: `Program.cs`, `ApplicationDbContext`, `appsettings.json`
- API startup tự chạy EF migrations và seed admin nếu config bật
  - nguồn: `src/ELearnGamePlatform.API/Program.cs`
- OCR dùng Tesseract và trông chờ `tessdata` dưới `src/ELearnGamePlatform.API/tessdata`
- Ollama settings và image pipeline settings nằm trong `src/ELearnGamePlatform.API/appsettings.json`

## Thành phần cốt lõi

### Backend API

- Controllers chính:
  - `DocumentsController`
  - `QuestionsController`
  - `GamesController`
  - `FoldersController`
  - `WorkspacesController`
  - `SlidesController`
  - `AuthController`
  - `AdminController`
- Services API:
  - `DocumentIngestionService`
  - `WorkspaceService`
  - `SlideImageService`
  - job stores cho document/question/slide generation

### Services domain

- OCR:
  - `TesseractOcrService`
- Document processors:
  - `PdfProcessor`
  - `DocxProcessor`
  - `ImageProcessor`
- AI:
  - `ContentAnalyzerService`
  - `QuestionGeneratorService`
  - `SlideGeneratorService`
  - `DocumentStructureChunker`
  - `DocumentCoverageMapBuilder`

### Persistence

- `ApplicationDbContext` quản lý:
  - `AppUsers`
  - `Documents`
  - `FolderProjects`
  - `Questions`
  - `GameSessions`
  - `SlideDecks`
  - `SlideItems`
- Nhiều field quan trọng lưu dưới dạng `jsonb` hoặc text JSON.

### Frontend

- App root và routing: `client/src/App.js`
- API client: `client/src/services/api.js`
- i18n: `client/src/i18n/translations.js`, `client/src/i18n/index.js`
- Màn hình chính:
  - workspace dashboard / studio
  - document list legacy
  - study hub, quiz, flashcards, streak
  - slide studio

## Flow chính cần nhớ

### Upload -> OCR -> Analysis

1. Frontend upload file qua `documentService`, `folderService`, hoặc `workspaceService`.
2. API lưu file vào `uploads`.
3. API chọn processor theo loại file.
4. `PdfProcessor`, `DocxProcessor`, `ImageProcessor` lấy text; PDF scan có thể đi qua `pdftoppm` + Tesseract.
5. `ContentAnalyzerService` phân tích nội dung và lưu kết quả vào document.

Nguồn tra cứu nhanh:

- `src/ELearnGamePlatform.API/Controllers/DocumentsController.cs`
- `src/ELearnGamePlatform.API/Services/DocumentIngestionService.cs`
- `src/ELearnGamePlatform.Services/OCR/TesseractOcrService.cs`
- `src/ELearnGamePlatform.Services/DocumentProcessing/*`

### Generate questions -> Study flows

1. Frontend gọi `questionService`.
2. API tạo trạng thái job trong RAM.
3. `QuestionGeneratorService` dùng Ollama để tạo câu hỏi.
4. Kết quả lưu vào PostgreSQL.
5. Frontend dùng quiz/flashcards/streak trên dữ liệu câu hỏi đã lưu.

Nguồn tra cứu nhanh:

- `src/ELearnGamePlatform.API/Controllers/QuestionsController.cs`
- `src/ELearnGamePlatform.Services/AI/QuestionGeneratorService.cs`
- `src/ELearnGamePlatform.API/Controllers/GamesController.cs`
- `client/src/components/StudyHub.js`
- `client/src/components/QuizGame.js`
- `client/src/components/FlashcardGame.js`
- `client/src/components/StreakGame.js`

### Workspace / Folder

1. Workspace là flow chính trên frontend hiện tại.
2. Folder/project và workspace đều có source upload, source selection, và slide deck liên quan.
3. Frontend route chính đang ưu tiên `/workspaces`.

Nguồn tra cứu nhanh:

- `src/ELearnGamePlatform.API/Controllers/FoldersController.cs`
- `src/ELearnGamePlatform.API/Controllers/WorkspacesController.cs`
- `client/src/components/FolderProjects.js`
- `client/src/components/FolderStudio.js`

### Slide generation -> Preview -> Editor

1. Frontend gọi `slideService.startGenerateSlides*`.
2. `SlidesController` điều phối generation và lưu deck/items.
3. `SlideGeneratorService` tạo outline, slide content, HTML preview.
4. `SlideImageService` xử lý image candidates, select/refresh, local asset storage.
5. Frontend preview/editor chạy qua `SlideStudio` và `SlideStudioScreen`.

Nguồn tra cứu nhanh:

- `src/ELearnGamePlatform.API/Controllers/SlidesController.cs`
- `src/ELearnGamePlatform.API/Services/SlideImageService.cs`
- `src/ELearnGamePlatform.Infrastructure/Repositories/SlideDeckRepository.cs`
- `src/ELearnGamePlatform.Services/AI/SlideGeneratorService.cs`
- `client/src/components/SlideStudio.js`
- `client/src/components/SlideStudioScreen.js`

## Điều không nên giả định

- Đừng giả định repo có test suite chuẩn; hiện chưa có test project ổn định mặc định.
- Đừng tin docs cũ nói MongoDB hoặc .NET 8 nếu code hiện tại nói khác.
- Đừng giả định auth còn dùng `demo-user`; runtime hiện đã có auth flow và bearer token.
- Đừng sửa một phía của contract rồi cho rằng phía còn lại tự khớp.
