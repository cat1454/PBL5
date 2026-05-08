# Package and Tooling Summary

Verified from source: 2026-05-07.

## .NET SDK and Tools

- `global.json` pins .NET SDK `9.0.306`.
- Projects target `net8.0`.
- Local tool manifests: `.config/dotnet-tools.json` and root `dotnet-tools.json`.
- Local `dotnet-ef` version: `8.0.0`.

Use local tools:

```powershell
cd H:\pbl5
dotnet tool restore
dotnet ef --version
```

Do not require a global `dotnet-ef` install for this repo.

## Backend Packages

### `src/ELearnGamePlatform.Core`

| Package | Version |
| --- | --- |
| Microsoft.EntityFrameworkCore | 8.0.0 |

### `src/ELearnGamePlatform.Infrastructure`

| Package | Version |
| --- | --- |
| Microsoft.EntityFrameworkCore | 8.0.0 |
| Microsoft.EntityFrameworkCore.Design | 8.0.0 |
| Npgsql.EntityFrameworkCore.PostgreSQL | 8.0.0 |
| Microsoft.Extensions.Options | 8.0.0 |

### `src/ELearnGamePlatform.Services`

| Package | Version |
| --- | --- |
| itext7 | 8.0.3 |
| PdfPig | 0.1.8 |
| DocumentFormat.OpenXml | 3.0.2 |
| SixLabors.ImageSharp | 3.1.12 |
| Tesseract | 5.2.0 |
| Microsoft.Extensions.Logging.Abstractions | 8.0.0 |

### `src/ELearnGamePlatform.API`

| Package | Version |
| --- | --- |
| Microsoft.AspNetCore.Authentication.JwtBearer | 8.0.0 |
| Microsoft.AspNetCore.OpenApi | 8.0.0 |
| Swashbuckle.AspNetCore | 6.5.0 |
| System.IdentityModel.Tokens.Jwt | 8.0.0 |

## Frontend Packages

Verified from `client/package.json`:

| Package | Version |
| --- | --- |
| axios | ^1.6.7 |
| react | ^18.2.0 |
| react-dom | ^18.2.0 |
| react-icons | ^5.6.0 |
| react-router-dom | ^6.22.0 |
| react-scripts | 5.0.1 |

Frontend proxy:

```text
http://127.0.0.1:5000
```

## Package Inspection Commands

```powershell
dotnet list src\ELearnGamePlatform.Core\ELearnGamePlatform.Core.csproj package
dotnet list src\ELearnGamePlatform.Infrastructure\ELearnGamePlatform.Infrastructure.csproj package
dotnet list src\ELearnGamePlatform.Services\ELearnGamePlatform.Services.csproj package
dotnet list src\ELearnGamePlatform.API\ELearnGamePlatform.API.csproj package
```

Frontend:

```powershell
cd H:\pbl5\client
npm install
npm run build
```

## Verification Commands

```powershell
cd H:\pbl5
dotnet tool restore
dotnet ef --version
dotnet restore
dotnet build ELearnGamePlatform.sln
```

Expected EF tool output should report `8.0.0`.
