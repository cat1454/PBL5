# dotnet-backend

## Khi nào dùng

- Task bắt đầu ở controller, service, DI, auth, config bind, hoặc repository flow nhìn từ backend.
- Cần lần theo call path ASP.NET Core hiện tại của PBL5.
- Cần xác nhận source of truth cho URL, startup, auth, hoặc registration.

## File/thư mục liên quan

- `src/ELearnGamePlatform.API/Program.cs`
- `src/ELearnGamePlatform.API/Controllers/`
- `src/ELearnGamePlatform.API/Services/`
- `src/ELearnGamePlatform.Core/Interfaces/`
- `src/ELearnGamePlatform.Infrastructure/Repositories/`
- `src/ELearnGamePlatform.Infrastructure/Configuration/`
- `src/ELearnGamePlatform.API/Configuration/`

## Điều cấm

- Không áp đặt rule external như mandatory immutability, TDD bắt buộc, coverage target bắt buộc.
- Không đổi contract chỉ để frontend dễ xử lý hơn nếu chưa kiểm tra consumer thật.
- Không sửa appsettings, secret, port, model name, migration, package version nếu task không yêu cầu.
- Không refactor rộng khi mục tiêu chỉ là fix cục bộ hoặc cập nhật docs.

## Checklist trước khi sửa

- Đọc `Program.cs` nếu task chạm DI, auth, config, URL, startup behavior.
- Xác định controller entrypoint của flow.
- Đọc service được controller gọi tới.
- Đọc interface và repository/config nằm dưới service đó.
- Kiểm tra frontend consumer nếu payload hoặc route có thể bị ảnh hưởng.

## Checklist sau khi sửa

- Đọc lại call path controller -> service -> repository/config để chắc inputs/outputs vẫn khớp.
- Xác nhận không tạo contract drift với `client/src/services/api.js`.
- Xác nhận không đụng config thật ngoài phạm vi task.
- Nếu task đụng auth hoặc startup, cân nhắc runtime verification ngoài build.

## Lệnh kiểm tra phù hợp

```powershell
dotnet build ELearnGamePlatform.sln
dotnet run --project src/ELearnGamePlatform.API
rg -n "class .*Controller|AddScoped|AddSingleton|AddAuthentication|AddAuthorization" src/ELearnGamePlatform.API src/ELearnGamePlatform.Infrastructure src/ELearnGamePlatform.Core
```
