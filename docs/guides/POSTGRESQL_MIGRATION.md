# PostgreSQL Migration Guide

Verified from source: 2026-05-07.

This guide records the historical move from MongoDB to PostgreSQL. The current runtime is PostgreSQL + EF Core + Npgsql.

## Current Runtime

- Database: PostgreSQL `14+`
- ORM: Entity Framework Core `8.0.0`
- Provider: `Npgsql.EntityFrameworkCore.PostgreSQL 8.0.0`
- Local EF tool: `dotnet-ef 8.0.0` from `.config/dotnet-tools.json` and root `dotnet-tools.json`
- API startup: automatically applies pending migrations

## Setup

Create the database:

```powershell
psql -U postgres
CREATE DATABASE "ELearnGameDB";
\q
```

Check the connection string:

```text
src/ELearnGamePlatform.API/appsettings.json
```

Restore local tools:

```powershell
cd H:\pbl5
dotnet tool restore
dotnet ef --version
```

Apply migrations manually if needed:

```powershell
cd H:\pbl5\src\ELearnGamePlatform.API
dotnet ef database update --project ..\ELearnGamePlatform.Infrastructure
```

Normally, `dotnet run` also applies migrations automatically.

## Schema Notes

The active schema is defined by:

- `src/ELearnGamePlatform.Infrastructure/Data/ApplicationDbContext.cs`
- migrations under `src/ELearnGamePlatform.Infrastructure/Migrations`

Main tables include:

- `app_users`
- `documents`
- `questions`
- `game_sessions`
- `folder_projects`
- `slide_decks`
- `slide_items`
- `learning_attempts`
- `learning_progresses`
- `learning_test_results`

## JSONB Notes

Several complex fields are stored as JSONB. JSONB is flexible and can be indexed, but PostgreSQL does not automatically create GIN indexes for your query patterns.

If a feature needs filtering/searching inside JSONB, add an explicit migration with a suitable index.

Example:

```sql
CREATE INDEX idx_slide_items_editor_state_gin
ON slide_items USING GIN (editor_state);
```

Only create JSONB indexes after confirming the exact query pattern; unnecessary GIN indexes slow writes and increase storage.

## Verify Database

```powershell
psql -U postgres -d ELearnGameDB
```

```sql
\dt
\d+ documents
\d+ questions
\d+ slide_decks
\d+ slide_items
SELECT migration_id FROM "__EFMigrationsHistory" ORDER BY migration_id;
```

## Troubleshooting

### Connection refused

```powershell
Get-Service postgresql*
psql -U postgres -d ELearnGameDB -c "SELECT version();"
```

### Authentication failed

- Check username/password in `ConnectionStrings:DefaultConnection`.
- Check local PostgreSQL authentication rules.

### Schema mismatch on startup

The API validates selected critical columns. If startup fails with a schema mismatch:

```powershell
cd H:\pbl5\src\ELearnGamePlatform.API
dotnet ef migrations list --project ..\ELearnGamePlatform.Infrastructure
dotnet ef database update --project ..\ELearnGamePlatform.Infrastructure
```

## Historical Legacy Data

If someone has old MongoDB data from before the migration, migrate it with a dedicated one-off script. Do not use MongoDB commands as normal development or run instructions for the current app.
