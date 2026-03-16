# Migration Checklist - MongoDB to PostgreSQL

## ✅ Completed Items

### 1. Package Updates
- [x] Removed MongoDB.Bson from Core project
- [x] Removed MongoDB.Driver from Infrastructure project
- [x] Added Microsoft.EntityFrameworkCore to Core (8.0.0)
- [x] Added Npgsql.EntityFrameworkCore.PostgreSQL to Infrastructure (8.0.0)
- [x] Added Microsoft.EntityFrameworkCore.Design to Infrastructure (8.0.0)
- [x] Installed dotnet-ef global tool (10.0.4)

### 2. Entity Models Migration
- [x] Document.cs - Changed ID from string to int, ProcessedContent to JSON columns
- [x] Question.cs - Changed IDs to int, Options to JSON column
- [x] GameSession.cs - Changed IDs to int, QuestionIds to JSON column
- [x] Created ProcessedContent.cs as DTO class
- [x] Added navigation properties and foreign keys

### 3. DbContext and Configuration
- [x] Created ApplicationDbContext.cs with entity configurations
- [x] Configured JSONB columns for PostgreSQL
- [x] Set up indexes (UploadedBy, CreatedAt, Status)
- [x] Configured cascade delete relationships
- [x] Renamed MongoDbSettings to PostgreSqlSettings

### 4. Repository Layer
- [x] Updated IDocumentRepository interface (string id → int id)
- [x] Updated IQuestionRepository interface (string id → int id)
- [x] Updated IGameSessionRepository interface (string id → int id)
- [x] Rewrote DocumentRepository for EF Core
- [x] Rewrote QuestionRepository for EF Core
- [x] Rewrote GameSessionRepository for EF Core

### 5. Extension Methods
- [x] Created EntityExtensions.cs in Core/Extensions
- [x] Implemented GetMainTopics/SetMainTopics for Document
- [x] Implemented GetKeyPoints/SetKeyPoints for Document
- [x] Implemented GetOptions/SetOptions for Question
- [x] Implemented GetQuestionIds/SetQuestionIds for GameSession

### 6. Service Layer
- [x] Updated IQuestionGenerator interface to use int documentId
- [x] Updated QuestionGeneratorService to use int documentId
- [x] Updated QuestionGeneratorService to use EntityExtensions
- [x] Updated ContentAnalyzerService (no changes needed)

### 7. API Controllers
- [x] Updated DocumentsController methods to use int id
- [x] Updated QuestionsController methods to use int id/documentId
- [x] Updated GamesController methods to use int id/documentId/sessionId
- [x] Updated request DTOs (GenerateQuestionsRequest, CreateGameSessionRequest, UserAnswer, AnswerResult)

### 8. Configuration Files
- [x] Updated Program.cs - Removed MongoDB, added PostgreSQL + DbContext
- [x] Updated appsettings.json - Changed MongoDbSettings to ConnectionStrings
- [x] Added DbContext registration with AddDbContext
- [x] Changed repository lifetime from Singleton to Scoped
- [x] Added automatic migration on startup

### 9. Database Migrations
- [x] Generated InitialCreate migration (20260311064712)
- [x] Verified migration creates documents, questions, game_sessions tables
- [x] Verified JSONB columns are created
- [x] Verified indexes are created
- [x] Verified foreign keys with cascade delete

### 10. Documentation
- [x] Updated README.md with PostgreSQL setup instructions
- [x] Created POSTGRESQL_MIGRATION.md with detailed migration guide
- [x] Created PACKAGES.md with package dependency summary
- [x] Created this MIGRATION_CHECKLIST.md

## 📋 Testing Checklist (To Verify)

### Prerequisites
- [ ] PostgreSQL 14+ installed and running
- [ ] ELearnGameDB database created
- [ ] Connection string updated in appsettings.json
- [ ] Ollama running with llama2 model

### Backend Tests
- [ ] Build solution successfully
- [ ] Run migrations successfully (automatic on startup)
- [ ] Start API without errors
- [ ] Swagger UI loads at https://localhost:5001/swagger

### API Endpoint Tests
- [ ] POST /api/documents/upload - Upload PDF file
- [ ] GET /api/documents/{id} - Get document by ID (int)
- [ ] GET /api/documents/user/{userId} - Get user documents
- [ ] POST /api/questions/generate - Generate questions from document
- [ ] GET /api/questions/document/{documentId} - Get questions by document
- [ ] POST /api/games/sessions - Create game session
- [ ] GET /api/games/sessions/{sessionId} - Get game session
- [ ] POST /api/games/sessions/{sessionId}/submit - Submit answers

### Database Verification
- [ ] Connect to PostgreSQL: `psql -U postgres -d ELearnGameDB`
- [ ] Check tables exist: `\dt`
- [ ] Verify documents table structure: `\d documents`
- [ ] Verify questions table structure: `\d questions`
- [ ] Verify game_sessions table structure: `\d game_sessions`
- [ ] Check JSONB columns: main_topics, key_points, options, question_ids
- [ ] Verify foreign keys: `\d+ questions` and `\d+ game_sessions`

### Frontend Tests (When Ready)
- [ ] Update API calls to use integer IDs
- [ ] Test document upload flow
- [ ] Test question generation flow
- [ ] Test game creation and play flow

## 🔍 Known Issues

### Warnings (Non-blocking)
- ⚠️ SixLabors.ImageSharp 3.1.3 has known vulnerabilities
  - **Solution**: Update to 3.1.5 or latest

### Potential Issues
- Frontend may need updates to handle integer IDs instead of strings
- Existing MongoDB data (if any) needs manual migration script

## 🚀 Next Steps

### Immediate
1. [ ] Test the application with PostgreSQL
2. [ ] Verify all CRUD operations work correctly
3. [ ] Test document upload and processing pipeline
4. [ ] Test AI question generation
5. [ ] Test game session workflows

### Optional Improvements
1. [ ] Update SixLabors.ImageSharp to patch vulnerabilities
2. [ ] Add database seeding for test data
3. [ ] Add integration tests with PostgreSQL
4. [ ] Create data migration script from MongoDB (if needed)
5. [ ] Add database connection pooling configuration
6. [ ] Configure PostgreSQL performance settings
7. [ ] Set up database backup strategy

### Frontend Updates Needed
1. [ ] Update API client to use integer IDs
2. [ ] Update type definitions (documentId: number)
3. [ ] Test all pages after backend changes
4. [ ] Update any hardcoded ID formats

## 📊 Migration Summary

**Total Files Changed:** ~25 files
**Packages Added:** 3 (EF Core, Npgsql, EF Core Design)
**Packages Removed:** 2 (MongoDB.Bson, MongoDB.Driver)
**New Files Created:** 4 (ApplicationDbContext, EntityExtensions in Core, ProcessedContent, migration files)
**Documentation Files:** 3 (POSTGRESQL_MIGRATION.md, PACKAGES.md, MIGRATION_CHECKLIST.md)

**Time Estimate to Complete:** ~2-3 hours
**Risk Level:** Medium (major database change)
**Rollback Complexity:** Medium (need MongoDB backup if data exists)

## 📝 Notes

### ID Type Change Impact
- All entities now use `int` primary keys with auto-increment
- Entity Framework will manage ID generation automatically
- Foreign key relationships properly configured with cascade delete

### JSON Serialization Approach
- Complex objects stored as JSONB in PostgreSQL
- EntityExtensions provide clean API for accessing JSON properties
- Transparent serialization/deserialization with System.Text.Json

### Architecture Benefits
- Cleaner architecture with EF Core Code First
- Better type safety with strong foreign key relationships
- Improved query performance with SQL Server indexing
- ACID compliance for transactions
- Better tooling support (EF Core migrations, LINQ)

## ✅ Migration Complete!

Tất cả các thay đổi về code đã hoàn thành. 
Chỉ cần PostgreSQL đang chạy và connection string đúng là có thể test được.
