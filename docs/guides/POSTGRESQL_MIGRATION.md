# PostgreSQL Migration Guide

## Tổng quan
Dự án đã được migrate từ MongoDB sang PostgreSQL với Entity Framework Core 8.0.

## Thay đổi chính

### Database
- **Trước**: MongoDB (NoSQL)
- **Sau**: PostgreSQL 14+ (SQL) với EF Core

### Schema Changes
- **ID Fields**: Từ `string` (ObjectId) → `int` (auto-increment)
- **JSON Storage**: Complex properties được lưu dưới dạng JSONB columns
- **Relationships**: Foreign keys và navigation properties được thiết lập đúng
- **Naming**: Sử dụng snake_case cho database columns

### Entity Models

#### Document Entity
```csharp
- Id: string → int (Primary Key, auto-increment)
- ProcessedContent object → MainTopicsJson (jsonb) + KeyPointsJson (jsonb)
- Navigation Properties: Questions, GameSessions
```

#### Question Entity
```csharp
- Id: string → int
- DocumentId: string → int (Foreign Key)
- Options: List<QuestionOption> → OptionsJson (jsonb)
- Navigation Property: Document
```

#### GameSession Entity
```csharp
- Id: string → int
- DocumentId: string → int (Foreign Key)
- QuestionIds: List<string> → QuestionIdsJson (jsonb as List<int>)
- Navigation Property: Document
```

## Extension Methods
Để truy cập các JSON properties một cách dễ dàng, sử dụng EntityExtensions:

```csharp
using ELearnGamePlatform.Core.Extensions;

// Document
var mainTopics = document.GetMainTopics();
document.SetMainTopics(new List<string> { "Topic 1", "Topic 2" });

var keyPoints = document.GetKeyPoints();
document.SetKeyPoints(new List<string> { "Point 1", "Point 2" });

// Question
var options = question.GetOptions();
question.SetOptions(new List<QuestionOption> { ... });

// GameSession
var questionIds = session.GetQuestionIds();
session.SetQuestionIds(new List<int> { 1, 2, 3 });
```

## Database Setup

### 1. Install PostgreSQL
**Windows:**
```bash
# Sử dụng PostgreSQL installer hoặc Chocolatey
choco install postgresql14

# Hoặc download từ: https://www.postgresql.org/download/windows/
```

**Linux:**
```bash
sudo apt-get update
sudo apt-get install postgresql-14 postgresql-contrib
```

**Mac:**
```bash
brew install postgresql@14
brew services start postgresql@14
```

### 2. Create Database
```bash
# Connect to PostgreSQL
psql -U postgres

# Create database
CREATE DATABASE ELearnGameDB;

# Create user (optional)
CREATE USER elearnapp WITH PASSWORD 'yourpassword';
GRANT ALL PRIVILEGES ON DATABASE ELearnGameDB TO elearnapp;

# Exit
\q
```

### 3. Update Connection String
Cập nhật `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=ELearnGameDB;Username=postgres;Password=postgres"
  }
}
```

### 4. Run Migrations
```bash
cd src/ELearnGamePlatform.API

# Migrations sẽ tự động chạy khi start app
dotnet run

# Hoặc chạy thủ công:
dotnet ef database update --project ../ELearnGamePlatform.Infrastructure
```

## Verify Migration
```sql
-- Connect to database
psql -U postgres -d ELearnGameDB

-- List tables
\dt

-- Expected tables:
-- documents
-- questions
-- game_sessions
-- __EFMigrationsHistory

-- Check table structure
\d documents
\d questions
\d game_sessions

-- Exit
\q
```

## Rollback (nếu cần)
```bash
# Remove last migration
cd src/ELearnGamePlatform.API
dotnet ef migrations remove --project ../ELearnGamePlatform.Infrastructure

# Revert database to specific migration
dotnet ef database update PreviousMigrationName --project ../ELearnGamePlatform.Infrastructure
```

## Troubleshooting

### Issue: Connection refused
```bash
# Check PostgreSQL service
# Windows:
sc query postgresql-x64-14

# Linux:
sudo systemctl status postgresql

# Mac:
brew services list
```

### Issue: Authentication failed
- Kiểm tra username/password trong connection string
- Kiểm tra pg_hba.conf file cho authentication method

### Issue: Permission denied
```sql
-- Grant permissions
psql -U postgres
GRANT ALL PRIVILEGES ON DATABASE ELearnGameDB TO your_user;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO your_user;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO your_user;
```

## Performance Tips

### Enable Query Logging (Development)
```json
// appsettings.Development.json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  }
}
```

### JSON Performance
JSONB columns được index tự động bởi PostgreSQL, nhưng có thể tạo thêm GIN indexes:
```sql
CREATE INDEX idx_documents_main_topics ON documents USING GIN (main_topics);
CREATE INDEX idx_documents_key_points ON documents USING GIN (key_points);
CREATE INDEX idx_questions_options ON questions USING GIN (options);
```

## API Changes
- **ID Parameters**: Tất cả endpoints dùng `int` thay vì `string`
- **Compatibility**: Frontend cần cập nhật để gửi integer IDs

Example:
```javascript
// Before (MongoDB)
axios.get(`/api/documents/${documentId}`)  // documentId là string

// After (PostgreSQL)
axios.get(`/api/documents/${documentId}`)  // documentId là number
```

## Testing
```bash
# Run tests with PostgreSQL
cd tests
dotnet test
```

## Data Migration (từ MongoDB sang PostgreSQL)
Nếu đã có data trong MongoDB, cần migrate:

1. Export data từ MongoDB
```bash
mongoexport --db=ELearnGameDB --collection=documents --out=documents.json
mongoexport --db=ELearnGameDB --collection=questions --out=questions.json
mongoexport --db=ELearnGameDB --collection=gameSessions --out=gameSessions.json
```

2. Tạo migration script để import vào PostgreSQL
3. Cập nhật IDs từ ObjectId strings sang integers
4. Update relationships

## Benefits of PostgreSQL
- ✅ ACID compliance
- ✅ Better query performance với complex joins
- ✅ Built-in JSONB support
- ✅ Strong typing và schema validation
- ✅ Rich ecosystem của PostgreSQL tools
- ✅ Better integration với .NET EF Core
