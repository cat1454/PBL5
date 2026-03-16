# ⚡ Command Cheatsheet - Lệnh Thường Dùng

## 🎯 Chạy Ứng Dụng

### Backend
```powershell
cd H:\PBL5\src\ELearnGamePlatform.API
dotnet run
```

### Frontend  
```powershell
cd H:\PBL5\client
npm start
```

---

## 🔧 Development Commands

### Backend (.NET)
```powershell
# Restore packages
dotnet restore

# Build
dotnet build

# Clean
dotnet clean

# Run with specific environment
dotnet run --environment Development

# Watch mode (auto-reload)
dotnet watch run

# Create migration
dotnet ef migrations add MigrationName

# Update database
dotnet ef database update
```

### Frontend (React)
```powershell
# Install dependencies
npm install

# Start dev server
npm start

# Build for production
npm run build

# Run tests
npm test
```

---

## 🤖 Ollama Commands

```powershell
# List installed models
ollama list

# Pull a model
ollama pull llama3.2

# Run model interactively
ollama run llama3.2

# Check if Ollama is running
curl http://localhost:11434/api/tags

# Show model info
ollama show llama3.2
```

---

## 🗄️ Database Commands

### PostgreSQL
```powershell
# Connect to database
psql -U postgres -d ELearnGameDB

# List databases
\l

# List tables
\dt

# Describe table
\d documents

# Exit
\q
```

---

## 🐛 Debug Commands

```powershell
# Check .NET version
dotnet --version

# Check Node version
node --version
npm --version

# Check Ollama status
ollama list

# Check ports in use
netstat -ano | findstr :5000
netstat -ano | findstr :3000
netstat -ano | findstr :11434

# Kill process on port (if needed)
# Find PID first, then:
taskkill /PID <PID> /F
```

---

## 📁 Useful Paths

```
Backend: H:\PBL5\src\ELearnGamePlatform.API
Frontend: H:\PBL5\client
Uploads: H:\PBL5\src\ELearnGamePlatform.API\uploads
Models: H:\.ollama\models
Logs: H:\PBL5\src\ELearnGamePlatform.API\logs
```

---

## 🔥 Quick Restart Everything

```powershell
# Stop all (Ctrl+C in each terminal)
# Then restart in order:

# Terminal 1: Backend
cd H:\PBL5\src\ELearnGamePlatform.API ; dotnet run

# Terminal 2: Frontend
cd H:\PBL5\client ; npm start
```

---

## 📊 Check Status

```powershell
# Backend health
curl http://localhost:5000/api/documents/user/demo-user

# Frontend
curl http://localhost:3000

# Ollama
curl http://localhost:11434/api/tags
```
