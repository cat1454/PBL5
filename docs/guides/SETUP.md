# 🚀 Hướng dẫn Setup và Chạy Project

## Yêu cầu hệ thống

### Phần mềm cần cài đặt:
1. **.NET 8.0 SDK** - [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
2. **PostgreSQL 14+** - [Download](https://www.postgresql.org/download/)
3. **Ollama** - [Download](https://ollama.ai/)
4. **Node.js 18+** - [Download](https://nodejs.org/)
5. **Tesseract OCR** - [Download](https://github.com/UB-Mannheim/tesseract/wiki)

---

## Bước 1: Setup PostgreSQL

### Windows:
```powershell
# Tải và cài đặt PostgreSQL từ website
# https://www.postgresql.org/download/windows/

# Hoặc dùng Chocolatey
choco install postgresql14

# Khởi động PostgreSQL service (thường tự động chạy)
net start postgresql-x64-14
```

### Hoặc chạy PostgreSQL trong Docker:
```powershell
docker run -d -p 5432:5432 --name postgres -e POSTGRES_PASSWORD=postgres postgres:14
```

### Tạo Database:
```powershell
# Kết nối PostgreSQL (mật khẩu: postgres)
psql -U postgres

# Tạo database
CREATE DATABASE "ELearnGameDB";

# Thoát
\q
```

Kiểm tra PostgreSQL đang chạy:
```powershell
# Kiểm tra service
Get-Service postgresql-x64-14

# Hoặc kết nối
psql -U postgres -d ELearnGameDB
```

---

## Bước 2: Setup Ollama và LLaMA

### Cài đặt Ollama:
```powershell
# Download và cài đặt Ollama từ https://ollama.ai/

# Kiểm tra Ollama đã cài đặt
ollama --version

# Pull LLaMA model (hoặc model khác bạn muốn dùng)
ollama pull llama2

# Hoặc dùng model nhỏ hơn nếu RAM hạn chế
ollama pull llama2:7b
```

### Chạy Ollama server:
```powershell
# Ollama sẽ tự động chạy ở background sau khi cài
# Mặc định chạy ở http://localhost:11434

# Test Ollama
ollama run llama2 "Hello, how are you?"
```

---

## Bước 3: Setup Tesseract OCR

### Windows:
```powershell
# Download installer từ: https://github.com/UB-Mannheim/tesseract/wiki
# Cài đặt vào thư mục mặc định: C:\Program Files\Tesseract-OCR

# Thêm vào PATH environment variable
$env:PATH += ";C:\Program Files\Tesseract-OCR"

# Tải tessdata (training data)
# Download từ: https://github.com/tesseract-ocr/tessdata
# Đặt vào: C:\Program Files\Tesseract-OCR\tessdata
# Cần file: eng.traineddata và vie.traineddata (nếu xử lý tiếng Việt)
```

### Tạo thư mục tessdata trong project:
```powershell
cd H:\PBL5
mkdir src\ELearnGamePlatform.API\tessdata
# Copy các file .traineddata vào thư mục này
```

---

## Bước 4: Setup Backend (.NET)

### Restore và Build:
```powershell
cd H:\PBL5

# Restore dependencies
dotnet restore

# Build solution
dotnet build
```

### Chạy Entity Framework Migrations:
```powershell
# Cài đặt EF Core tools (nếu chưa có)
dotnet tool install --global dotnet-ef

# Chạy migrations để tạo database schema
cd src\ELearnGamePlatform.API
dotnet ef database update

# Kiểm tra migrations đã apply
dotnet ef migrations list
```

### Chạy API:
```powershell
# Từ thư mục API
cd src\ELearnGamePlatform.API
dotnet run
```

Backend sẽ chạy tại: **http://localhost:5000**

Swagger UI: **http://localhost:5000/swagger**

---

## Bước 5: Setup Frontend (React)

### Cài đặt dependencies:
```powershell
cd H:\PBL5\client

# Install packages
npm install

# Chạy development server
npm start
```

Frontend sẽ chạy tại: **http://localhost:3000**

---

## Bước 6: Kiểm tra cấu hình

### Kiểm tra appsettings.json:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=ELearnGameDB;Username=postgres;Password=postgres"
  },
  "OllamaSettings": {
    "BaseUrl": "http://localhost:11434",
    "Model": "llama2",
    "TimeoutSeconds": 120,
    "Temperature": 0.7
  },
  "FileUpload": {
    "MaxFileSizeInMB": 50,
    "AllowedExtensions": [".pdf", ".docx", ".png", ".jpg", ".jpeg"]
  }
}
```

**Lưu ý**: Thay đổi `Username` và `Password` nếu bạn dùng credentials khác.

---

## 🎮 Hướng dẫn sử dụng hệ thống

### 1. Upload Document
- Truy cập http://localhost:3000
- Click "Choose File" và chọn file PDF/DOCX/Image
- Click "Upload & Process"
- Hệ thống sẽ tự động:
  - Trích xuất text (OCR nếu là scan/image)
  - Phân tích nội dung bằng AI
  - Lưu vào database

### 2. Generate Questions
- Vào "My Documents"
- Đợi document status = "Completed"
- Click "Generate Questions"
- AI sẽ tạo 10 câu hỏi trắc nghiệm

### 3. Play Games
- **Quiz Game**: Click "Play Quiz" để chơi game trắc nghiệm
- **Flashcards**: Click "Flashcards" để học với flashcard

---

## ⚠️ Troubleshooting

### PostgreSQL không kết nối được:
```powershell
# Kiểm tra PostgreSQL service
Get-Service postgresql-x64-14

# Start service
Start-Service postgresql-x64-14

# Test connection
psql -U postgres -d ELearnGameDB -c "SELECT version();"
```

### Migration errors:
```powershell
# Xóa database và tạo lại
cd src\ELearnGamePlatform.API
dotnet ef database drop
dotnet ef database update

# Hoặc tạo migration mới
dotnet ef migrations add NewMigrationName
dotnet ef database update
```

### Ollama không chạy:
```powershell
# Kiểm tra Ollama
ollama list

# Restart Ollama
# Windows: Tắt Ollama trong System Tray và mở lại
```

### OCR không hoạt động:
```powershell
# Kiểm tra Tesseract
tesseract --version

# Kiểm tra tessdata
ls "C:\Program Files\Tesseract-OCR\tessdata"
# Phải có: eng.traineddata
```

### Backend lỗi khi build:
```powershell
# Clean và rebuild
dotnet clean
dotnet restore
dotnet build
```

### Frontend lỗi:
```powershell
cd client
rm -r node_modules
rm package-lock.json
npm install
```

---

## 📊 API Endpoints

### Documents:
- `POST /api/documents/upload` - Upload file
- `GET /api/documents/{id}` - Get document info
- `GET /api/documents/user/{userId}` - Get user's documents
- `DELETE /api/documents/{id}` - Delete document

### Questions:
- `POST /api/questions/generate` - Generate questions
- `GET /api/questions/document/{documentId}` - Get questions

### Games:
- `POST /api/games/sessions` - Create game session
- `GET /api/games/quiz/{documentId}` - Get quiz
- `GET /api/games/flashcards/{documentId}` - Get flashcards

---

## 🔧 Development Tips

### Xem logs:
```powershell
# Backend logs
cd src\ELearnGamePlatform.API
dotnet run --verbosity detailed

# PostgreSQL queries
psql -U postgres -d ELearnGameDB

# Xem data trong tables
SELECT * FROM documents;
SELECT * FROM questions;
SELECT * FROM game_sessions;

# Xem schema
\dt
\d+ documents
```

### Test API với Postman hoặc Swagger:
- Swagger UI: http://localhost:5000/swagger

### Hot reload:
- Backend: `dotnet watch run` (trong API folder)
- Frontend: `npm start` đã có hot reload sẵn

---

## 📦 Production Deployment

### Build production:
```powershell
# Backend
dotnet publish -c Release -o ./publish

# Frontend
cd client
npm run build
```

### Docker (Optional):
```dockerfile
# Tạo Dockerfile cho backend và frontend
# Deploy lên Azure, AWS, hoặc server riêng
```

---

## 🎯 Next Steps

1. ✅ Setup authentication (JWT, Identity)
2. ✅ Implement real-time progress tracking (SignalR)
3. ✅ Add more game types (Matching, Word Search)
4. ✅ Improve UI/UX
5. ✅ Add analytics dashboard
6. ✅ Multi-language support
7. ✅ Mobile app (React Native)

---

## 📞 Support

Nếu gặp vấn đề, hãy kiểm tra:
1. Tất cả services đang chạy (PostgreSQL, Ollama)
2. Port không bị conflict (5000, 3000, 5432, 11434)
3. Dependencies đã được cài đầy đủ
4. Cấu hình trong appsettings.json đúng
5. Database migrations đã được chạy (`dotnet ef database update`)
6. PostgreSQL user có quyền truy cập database

**Xem thêm**: [POSTGRESQL_MIGRATION.md](POSTGRESQL_MIGRATION.md) để hiểu về database schema và extension methods.

Good luck! 🚀
