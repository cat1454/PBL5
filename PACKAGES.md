# Package Dependencies Summary

## Các packages đã được cài đặt/cập nhật trong quá trình migration

### 1. ELearnGamePlatform.Core
**Đã xóa:**
- ❌ MongoDB.Bson

**Đã thêm:**
- ✅ Microsoft.EntityFrameworkCore (8.0.0)

### 2. ELearnGamePlatform.Infrastructure
**Đã xóa:**
- ❌ MongoDB.Driver (2.25.0)

**Đã thêm:**
- ✅ Npgsql.EntityFrameworkCore.PostgreSQL (8.0.0)
- ✅ Microsoft.EntityFrameworkCore.Design (8.0.0)

**Packages còn lại:**
- Microsoft.Extensions.Configuration.Abstractions (8.0.0)
- Microsoft.Extensions.DependencyInjection.Abstractions (8.0.0)
- Microsoft.Extensions.Options (8.0.0)

### 3. ELearnGamePlatform.Services
**Không thay đổi packages** - Chỉ cập nhật code để dùng int IDs

**Packages hiện có:**
- itext7 (8.0.3)
- PdfPig (0.1.9) 
- DocumentFormat.OpenXml (3.0.2)
- Tesseract (5.2.0)
- SixLabors.ImageSharp (3.1.3) ⚠️ *Có known vulnerabilities, nên update*
- Microsoft.Extensions.Logging.Abstractions (8.0.0)

### 4. ELearnGamePlatform.API
**Packages Project References** - Không thay đổi, chỉ cập nhật code

**Packages hiện có:**
- Swashbuckle.AspNetCore (6.5.0) - Swagger
- Microsoft.AspNetCore.OpenApi (8.0.0)

### 5. Global Tools
**Đã cài đặt:**
- ✅ dotnet-ef (10.0.4) - Entity Framework Core CLI tools

## Packages cần update (Khuyến nghị)

### Security Vulnerabilities
```bash
# SixLabors.ImageSharp có vulnerabilities, cập nhật lên version mới nhất
cd src/ELearnGamePlatform.Services
dotnet add package SixLabors.ImageSharp --version 3.1.5
```

## Kiểm tra packages đã cài

### Kiểm tra từng project
```powershell
# Core
dotnet list src/ELearnGamePlatform.Core/ELearnGamePlatform.Core.csproj package

# Infrastructure  
dotnet list src/ELearnGamePlatform.Infrastructure/ELearnGamePlatform.Infrastructure.csproj package

# Services
dotnet list src/ELearnGamePlatform.Services/ELearnGamePlatform.Services.csproj package

# API
dotnet list src/ELearnGamePlatform.API/ELearnGamePlatform.API.csproj package
```

### Restore tất cả packages
```powershell
dotnet restore H:\PBL5\src\ELearnGamePlatform.API\ELearnGamePlatform.API.csproj
```

## Package Installation Commands

### Nếu cần cài lại từ đầu:
```powershell
# Core - EF Core basics
cd src/ELearnGamePlatform.Core
dotnet add package Microsoft.EntityFrameworkCore --version 8.0.0

# Infrastructure - PostgreSQL + EF Core Design
cd ../ELearnGamePlatform.Infrastructure
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 8.0.0
dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.0.0

# Global Tools - EF Core CLI
dotnet tool install --global dotnet-ef --version 10.0.4
# Or update existing:
dotnet tool update --global dotnet-ef
```

## Verify Installation

### 1. Build solution
```powershell
cd H:\PBL5
dotnet build src/ELearnGamePlatform.API/ELearnGamePlatform.API.csproj
```

### 2. Check EF Core tools
```powershell
dotnet ef --version
# Expected: Entity Framework Core .NET Command-line Tools 10.0.4
```

### 3. Verify migrations
```powershell
cd src/ELearnGamePlatform.API
dotnet ef migrations list --project ../ELearnGamePlatform.Infrastructure
# Expected: 20260311064712_InitialCreate
```

## Package Versions Matrix

| Package | Version | Project |
|---------|---------|---------|
| Microsoft.EntityFrameworkCore | 8.0.0 | Core |
| Npgsql.EntityFrameworkCore.PostgreSQL | 8.0.0 | Infrastructure |
| Microsoft.EntityFrameworkCore.Design | 8.0.0 | Infrastructure |
| itext7 | 8.0.3 | Services |
| PdfPig | 0.1.9 | Services |
| DocumentFormat.OpenXml | 3.0.2 | Services |
| Tesseract | 5.2.0 | Services |
| SixLabors.ImageSharp | 3.1.3 → 3.1.5 | Services |
| Swashbuckle.AspNetCore | 6.5.0 | API |

## Common Issues

### Issue: Package conflicts
```powershell
# Clear NuGet cache
dotnet nuget locals all --clear

# Restore
dotnet restore --force
```

### Issue: EF Core tools not found
```powershell
# Make sure global tools path is in PATH
# Windows: %USERPROFILE%\.dotnet\tools
# Linux/Mac: ~/.dotnet/tools

# Reinstall
dotnet tool uninstall --global dotnet-ef
dotnet tool install --global dotnet-ef
```

### Issue: Migration build errors
```powershell
# Clean and rebuild
dotnet clean
dotnet build

# Then try migration again
cd src/ELearnGamePlatform.API
dotnet ef migrations add MigrationName --project ../ELearnGamePlatform.Infrastructure
```
