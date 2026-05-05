# postgres-efcore

## Khi nào dùng

- Task bắt đầu ở `ApplicationDbContext`, entity mapping, repository query, migration history, hoặc startup schema validation.
- Cần xác nhận shape dữ liệu đang được lưu trong PostgreSQL.
- Cần kiểm tra query/index/cascade/JSON fields trước khi sửa backend phía trên.

## File/thư mục liên quan

- `src/ELearnGamePlatform.Infrastructure/Data/ApplicationDbContext.cs`
- `src/ELearnGamePlatform.Infrastructure/Repositories/`
- `src/ELearnGamePlatform.Infrastructure/Migrations/`
- `src/ELearnGamePlatform.API/Program.cs`
- `src/ELearnGamePlatform.Core/Entities/`

## Điều cấm

- Không tạo hoặc chỉnh migration nếu task không yêu cầu rõ.
- Không đoán schema từ README cũ.
- Không đổi kiểu dữ liệu persisted hoặc quan hệ cascade nếu chưa đọc consumer liên quan.
- Không bỏ qua startup auto-migrate và schema validation trong `Program.cs`.

## Checklist trước khi sửa

- Đọc entity liên quan trong Core.
- Đọc mapping trong `ApplicationDbContext`.
- Đọc repository query hoặc update path liên quan.
- Rà migration history để hiểu schema hiện tại.
- Kiểm tra caller phía service/controller và consumer payload nếu thay đổi có thể nổi lên ở API/UI.

## Checklist sau khi sửa

- Xác nhận mapping, query shape, và relation vẫn nhất quán.
- Xác nhận JSON fields vẫn serialize/deserialize đúng nơi dùng.
- Xác nhận không vô tình tạo drift giữa schema runtime và code hiện tại.
- Nếu task đụng startup-sensitive schema, cân nhắc chạy API để bắt mismatch sớm.

## Lệnh kiểm tra phù hợp

```powershell
dotnet build ELearnGamePlatform.sln
dotnet run --project src/ELearnGamePlatform.API
rg -n "HasColumnType|HasIndex|OnDelete|DbSet|jsonb|processed_metadata|Migrate\\(" src/ELearnGamePlatform.Infrastructure src/ELearnGamePlatform.API
```
