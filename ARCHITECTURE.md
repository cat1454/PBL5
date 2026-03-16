# Architecture - ELearn Game Platform

## 1. Tong quan

He thong hien tai theo mo hinh 4 layer:

```text
Frontend (React)
        |
        v
API Layer (.NET Web API)
        |
        v
Services Layer (OCR, AI, document processing)
        |
        v
Core + Infrastructure (entities, repositories, EF Core, PostgreSQL, Ollama)
```

## 2. Layer thuc te trong repo

### `src/ELearnGamePlatform.Core`

Chua:

- domain entities
- enums
- interfaces cho repositories va services
- extension methods cho JSON fields

Entities chinh:

- `Document`
- `Question`
- `GameSession`
- `ProcessedContent`
- `QuestionGenerationProgressUpdate`

### `src/ELearnGamePlatform.Infrastructure`

Chua:

- `ApplicationDbContext`
- EF Core migrations
- repository implementations
- `OllamaService`
- config classes

Runtime database hien tai:

- PostgreSQL

Khong phai MongoDB.

### `src/ELearnGamePlatform.Services`

Chua business logic:

- `PdfProcessor`
- `DocxProcessor`
- `ImageProcessor`
- `TesseractOcrService`
- `ContentAnalyzerService`
- `QuestionGeneratorService`

### `src/ELearnGamePlatform.API`

Chua:

- DI wiring
- controllers
- startup
- in-memory progress store cho question generation

Controllers hien tai:

- `DocumentsController`
- `QuestionsController`
- `GamesController`

### `client/`

Frontend React hien tai co:

- upload document
- document list
- analysis modal
- quiz
- flashcards

## 3. Data flow hien tai

### Upload document

1. Frontend goi `POST /api/documents/upload`
2. API validate:
   - file co ton tai
   - `userId`
   - file size
   - allowed extension
3. API luu file vao `uploads/`
4. API tao `Document` record trong PostgreSQL
5. API day background task xu ly document

### Process document

1. Chon processor theo file type
2. Trich xuat text:
   - PDF text -> PdfPig
   - DOCX -> OpenXML
   - image -> Tesseract
   - PDF scan -> `pdftoppm` + Tesseract
3. Goi AI de phan tich noi dung
4. Luu:
   - extracted text
   - main topics
   - key points
   - summary
   - language

### Generate questions

1. Frontend goi `POST /api/questions/generate/start`
2. API tao job state trong memory
3. Background task goi `QuestionGeneratorService`
4. Service dung Ollama tao bo cau hoi
5. Ket qua duoc luu vao PostgreSQL
6. Frontend poll `GET /api/questions/generate/progress/{jobId}`

### Play game

- Quiz lay du lieu tu `GET /api/games/quiz/{documentId}`
- Flashcards lay du lieu tu `GET /api/games/flashcards/{documentId}`

## 4. Dinh nghia du lieu chinh

### Document

Luu:

- metadata file
- extracted text
- topics/key points dang JSON
- summary
- language
- status
- owner (`UploadedBy`)

### Question

Luu:

- document id
- question text
- question type
- options dang JSON
- correct answer
- explanation
- difficulty
- topic tag

### GameSession

Luu:

- document id
- game type
- user id
- danh sach question ids dang JSON
- score
- correct answers
- status

## 5. Ky thuat dang dung

### Backend

- ASP.NET Core 8
- Entity Framework Core 8
- Npgsql
- PdfPig
- DocumentFormat.OpenXml
- Tesseract
- ImageSharp

### Frontend

- React 18
- React Router
- Axios

### AI

- Ollama
- `llama3.2`

## 6. Gioi han kien truc hien tai

- Question generation progress store dang nam trong RAM
- Background jobs dang dung `Task.Run`
- Chua co auth/authorization that su
- Frontend dang hardcode `demo-user`
- Chua co test project that su
- `local-store` khong duoc wiring vao runtime

## 7. Dinh huong mo rong gan nhat

Gan nhat nen uu tien:

1. On dinh MVP va dong bo docs
2. Auth va ownership that su
3. Job ben vung hon
4. Mo rong hoc tap (`QuestionType`, test mode, lich su hoc)
5. Auto slide tu tai lieu theo huong:
   - AI tao slide schema
   - backend render HTML
   - frontend preview
   - export PDF
