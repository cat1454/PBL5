# testing-checklist

## Khi nào dùng

- Dùng trước khi kết thúc mọi task.
- Dùng khi cần chọn verify nhỏ nhất nhưng vẫn có ý nghĩa.
- Dùng khi phải nói rõ phần nào chưa verify được.

## File/thư mục liên quan

- `AGENTS.md`
- `docs/agent/PROJECT_CONTEXT.md`
- `client/package.json`
- `src/ELearnGamePlatform.API/Program.cs`
- `.local-agent-rules/CHANGELOG.md`

## Điều cấm

- Không tuyên bố đã verify nếu chưa chạy gì.
- Không chỉ build một phía nếu task đã tạo thay đổi cross-surface mà môi trường cho phép build cả hai.
- Không né việc ghi rõ assumption, residual risk, hoặc lý do không verify được.
- Không quên append local changelog cho task đáng kể.

## Checklist trước khi sửa

- Xác định task thuộc backend, frontend, hay cross-surface.
- Xác định lệnh verify nhỏ nhất có ý nghĩa với task đó.
- Xác định runtime-sensitive chỗ nào cần `dotnet run` thay vì chỉ build.
- Ghi nhớ constraint môi trường hiện tại trước khi hứa verify đầy đủ.

## Checklist sau khi sửa

- Đọc lại call path đã chạm để chắc contract/config còn khớp.
- Chạy verify nhỏ nhất phù hợp:
  - backend: `dotnet build ELearnGamePlatform.sln`
  - frontend: `cd client && npm run build`
  - runtime-sensitive backend: `dotnet run --project src/ELearnGamePlatform.API`
- Nếu không chạy được, ghi rõ lệnh nào bỏ qua và lý do cụ thể.
- Append `.local-agent-rules/CHANGELOG.md` với thay đổi, verify, và phần còn lại.
- Nêu rõ nếu có phần nên review tay trước khi merge.

## Lệnh kiểm tra phù hợp

```powershell
dotnet build ELearnGamePlatform.sln
cd client; npm run build
dotnet run --project src/ELearnGamePlatform.API
Get-ChildItem docs\agent -Recurse
rg -n "^## Khi nào dùng|^## File/thư mục liên quan|^## Điều cấm|^## Checklist trước khi sửa|^## Checklist sau khi sửa|^## Lệnh kiểm tra phù hợp" docs/agent/skills
```
