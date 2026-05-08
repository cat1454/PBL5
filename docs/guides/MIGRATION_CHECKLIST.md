# Migration Checklist - Historical MongoDB to PostgreSQL

Verified from source: 2026-05-07.

This file is historical migration context. The active runtime is PostgreSQL + EF Core + Npgsql.

## Completed Runtime State

- [x] Runtime database is PostgreSQL.
- [x] EF Core `ApplicationDbContext` is registered in `Program.cs`.
- [x] Npgsql provider is referenced by `ELearnGamePlatform.Infrastructure`.
- [x] EF Core migrations exist under `src/ELearnGamePlatform.Infrastructure/Migrations`.
- [x] API applies pending migrations automatically on startup.
- [x] Repository lifetime is scoped.
- [x] Primary runtime repositories use EF Core, not MongoDB.
- [x] `.config/dotnet-tools.json` and root `dotnet-tools.json` provide repo-local `dotnet-ef` version `8.0.0`.

## Current Tooling

```powershell
cd H:\pbl5
dotnet tool restore
dotnet ef --version
```

Do not document `dotnet-ef 10.x` as required for this repo. EF Core packages are `8.0.0`, and the local tool manifest uses `8.0.0`.

## Current Verification Checklist

- [ ] PostgreSQL `14+` is installed and running.
- [ ] Database `ELearnGameDB` exists.
- [ ] Connection string in `src/ELearnGamePlatform.API/appsettings.json` is correct for the local machine.
- [ ] Ollama is running with `qwen2.5:7b`.
- [ ] `dotnet tool restore` succeeds.
- [ ] `dotnet build ELearnGamePlatform.sln` succeeds.
- [ ] API starts at `http://localhost:5000`.
- [ ] Swagger loads at `http://localhost:5000/swagger`.
- [ ] Frontend build succeeds with `npm run build`.

## API Smoke Checklist

- [ ] `POST /api/auth/register`
- [ ] `POST /api/auth/login`
- [ ] `GET /api/auth/me`
- [ ] `POST /api/documents/upload`
- [ ] `GET /api/documents/{id}/progress`
- [ ] `POST /api/questions/generate/start`
- [ ] `GET /api/questions/generate/progress/{jobId}`
- [ ] `POST /api/slides/generate/start`
- [ ] `POST /api/slides/folders/{folderId}/generate/start`
- [ ] `GET /api/slides/generate/progress/{jobId}`

## Database Inspection

```powershell
psql -U postgres -d ELearnGameDB
```

```sql
\dt
\d+ app_users
\d+ documents
\d+ questions
\d+ game_sessions
\d+ folder_projects
\d+ slide_decks
\d+ slide_items
\d+ learning_attempts
\d+ learning_progresses
\d+ learning_test_results
```

## Remaining Improvements

- [ ] Add persistent job/progress storage.
- [ ] Replace `Task.Run` jobs with a durable queue or worker service.
- [ ] Add broader automated tests.
- [ ] Add production auth hardening.
- [ ] Add database backup/restore guidance for deployment.

## Historical Note

Old MongoDB instructions such as `mongosh`, MongoDB Compass, `db.documents.find()`, and MongoDB health checks should not be used for the active app. Keep them only in historical migration notes if a real legacy data migration is being performed.
