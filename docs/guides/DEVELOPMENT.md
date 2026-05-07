# Development Guide and Best Practices

Verified from source: 2026-05-07.

## Code Structure

### C# / .NET

- Classes and interfaces: `PascalCase`
- Methods: `PascalCase`
- Variables and parameters: `camelCase`
- Private fields: `_camelCase`
- Prefer async/await for I/O.
- Register runtime services in `src/ELearnGamePlatform.API/Program.cs`.

### React

- Components: `PascalCase`
- Functions and variables: `camelCase`
- Shared API calls live in `client/src/services/api.js`.
- UI text changes must update both `vi` and `en` translations in the same task.

## Runtime Stack

- ASP.NET Core Web API targeting `net8.0`
- .NET SDK pinned by `global.json` to `9.0.306`
- PostgreSQL + EF Core + Npgsql
- React 18 + React Router + Axios
- Ollama local AI
- Tesseract OCR

MongoDB is historical migration context only. Do not add MongoDB runtime guidance, `mongosh`, MongoDB Compass steps, or MongoDB health checks for the current app.

## Database Practices

### EF Core Queries

Use repository/query patterns already present in `ELearnGamePlatform.Infrastructure`.

For read-only queries:

```csharp
var documents = await _dbContext.Documents
    .AsNoTracking()
    .Where(document => document.UploadedBy == userId)
    .OrderByDescending(document => document.CreatedAt)
    .Take(50)
    .ToListAsync();
```

For large lists, use pagination:

```csharp
var page = Math.Max(1, request.Page);
var pageSize = Math.Clamp(request.PageSize, 1, 100);

var items = await query
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync();
```

### Migrations

Use the repo-local EF tool:

```powershell
cd H:\pbl5
dotnet tool restore
dotnet ef --version
```

List or apply migrations from the API project:

```powershell
cd H:\pbl5\src\ELearnGamePlatform.API
dotnet ef migrations list --project ..\ELearnGamePlatform.Infrastructure
dotnet ef database update --project ..\ELearnGamePlatform.Infrastructure
```

The API also applies pending migrations automatically on startup.

### PostgreSQL Inspection

```powershell
psql -U postgres -d ELearnGameDB
```

Useful commands:

```sql
\dt
\d+ documents
\d+ questions
\d+ game_sessions
\d+ slide_decks
\d+ slide_items
SELECT id, file_name, status, uploaded_by, created_at FROM documents ORDER BY created_at DESC LIMIT 20;
```

### JSONB

JSON-shaped fields are stored as JSONB where configured by EF Core. PostgreSQL does not automatically create query-optimized GIN indexes for JSONB columns. If a feature needs to filter/search inside JSONB, add an explicit migration with the right index.

Example:

```sql
CREATE INDEX idx_documents_processed_metadata_gin
ON documents USING GIN (processed_metadata);
```

## API Design Notes

- Use controller routes already present in `src/ELearnGamePlatform.API/Controllers`.
- Do not invent new endpoints in docs before adding source.
- Most controllers are protected by JWT; `register` and `login` are anonymous.
- Use structured error responses from `AuthenticatedControllerBase` where possible.

## Background Jobs and Progress

Current job stores are in-memory singletons:

- `DocumentProcessingJobStore`
- `QuestionGenerationJobStore`
- `SlideGenerationJobStore`

Current background execution uses `Task.Run` for document ingestion, question generation, and slide generation. This is acceptable for MVP/demo, but not durable across API restarts.

## AI Development Notes

Current Ollama defaults in `appsettings.json`:

- `qwen2.5:7b` for analysis
- `qwen2.5:7b` for generation
- `qwen2.5:7b` for verification

`OllamaService` can fall back to the generation/default model if a different profile model fails. Keep model names in docs aligned with `appsettings.json`.

## Testing

There is no complete automated test suite covering all core flows. When adding tests, prefer focused coverage around:

- auth and ownership checks
- document upload/processing contracts
- question generation persistence
- learning progress/session behavior
- slide generation/export contracts

Before merge/push, run at minimum:

```powershell
cd H:\pbl5
dotnet tool restore
dotnet build ELearnGamePlatform.sln

cd H:\pbl5\client
npm run build
```

## Debugging

Backend:

```powershell
cd H:\pbl5\src\ELearnGamePlatform.API
dotnet run --verbosity detailed
```

Frontend:

```powershell
cd H:\pbl5\client
npm start
```

PostgreSQL:

```powershell
psql -U postgres -d ELearnGameDB
```

Ollama:

```powershell
ollama list
curl http://localhost:11434/api/tags
```

Swagger:

```text
http://localhost:5000/swagger
```

## Security Checklist

- [x] Basic JWT login/register/me flow exists.
- [x] Protected controllers require bearer token.
- [x] Admin overview requires `Admin` role.
- [ ] Add refresh tokens.
- [ ] Add password reset/account recovery.
- [ ] Add email verification if needed.
- [ ] Add rate limiting.
- [ ] Add audit logging.
- [ ] Move secrets to environment/user secrets for non-local use.
- [ ] Add production HTTPS/deployment hardening.

## Useful Commands

```powershell
# Backend
dotnet clean
dotnet restore
dotnet build ELearnGamePlatform.sln
dotnet run --project src\ELearnGamePlatform.API
dotnet watch run --project src\ELearnGamePlatform.API

# EF Core local tool
dotnet tool restore
dotnet ef --version
dotnet ef migrations list --project src\ELearnGamePlatform.Infrastructure --startup-project src\ELearnGamePlatform.API

# Frontend
cd client
npm install
npm start
npm run build

# Git
git status
git diff
```
