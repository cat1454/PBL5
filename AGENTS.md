# AGENTS.md

PBL5 là MVP `.NET + React` cho ingest tài liệu, OCR, AI analysis, question generation, study flows, workspace/folder, và slide generation.

## Repo map

- `src/ELearnGamePlatform.API`: entrypoint, controller, DI, appsettings, uploads runtime
- `src/ELearnGamePlatform.Core`: entity, enum, interface dùng chung
- `src/ELearnGamePlatform.Infrastructure`: `ApplicationDbContext`, migration, repository, `OllamaService`
- `src/ELearnGamePlatform.Services`: OCR, document processing, AI generation/verification
- `client`: React 18 + React Router + Axios

## Source of truth

- Ưu tiên source code runtime hơn README hoặc docs cũ.
- Backend chạy `http://localhost:5000` trong `src/ELearnGamePlatform.API/Program.cs`.
- Frontend proxy trỏ `http://127.0.0.1:5000` trong `client/package.json`.
- Database hiện tại là PostgreSQL + EF Core, không phải MongoDB.
- `global.json` đang pin .NET SDK `9.0.306`; đừng giả định docs cũ về .NET 8 còn đúng.
- Ollama, JWT, upload, image pipeline nằm trong `src/ELearnGamePlatform.API/appsettings.json`.

## Cách đọc trước khi sửa

- Backend: controller -> service -> repository/config -> entity/interface liên quan.
- Frontend: screen/component -> `client/src/services/api.js` -> i18n/shared styles.
- AI/OCR: processor/OCR service -> prompt/config -> progress payload/UI coupling.
- Slide: `SlidesController` -> `SlideGeneratorService`/`SlideImageService` -> repository/deck shape -> Slide Studio/HTML preview.
- Schema/contract: xem đồng thời `ApplicationDbContext`, migration history, API payload, frontend consumer.

## Guardrails

- Không mở rộng scope nếu user không yêu cầu.
- Không copy nguyên xi rule, hook, installer, global config từ repo external.
- Không sửa backend/frontend source chỉ để "đồng bộ docs" nếu task là tài liệu.
- Không đổi secret, `appsettings`, package version, migration, port, model name nếu chưa được yêu cầu rõ.
- Không tự đổi API contract hoặc persisted shape mà không kiểm tra consumer liên quan.
- Frontend phải giữ UTF-8 và không để mojibake trong `client/src`.
- Mọi thay đổi text UI phải cập nhật đủ `vi` và `en` trong cùng task.

## Verify tối thiểu

- Backend: `dotnet build ELearnGamePlatform.sln`
- Frontend: `cd client && npm run build`
- Startup-sensitive backend: `dotnet run --project src/ELearnGamePlatform.API`
- Không tự chạy lại test/build/verify nếu user muốn tiết kiệm thời gian; thay vào đó, nêu rõ lệnh cần chạy để user tự thực thi.
- Nếu không verify được, nói rõ lệnh nào đã bỏ qua và vì sao.

## Local rules

- Dùng `.local-agent-rules/*.md` như lớp hướng dẫn chi tiết bổ sung.
- Luôn xem `ECC/` như thư mục tham chiếu mặc định để tối ưu workflow, skill, rule, MCP, prompt, và agent pattern trước khi làm task đáng kể.
- Khi dùng `ECC/`, chỉ áp dụng có chọn lọc phần phù hợp với repo hiện tại; không copy nguyên xi rule, hook, installer, global config, package, hoặc credential nếu user chưa yêu cầu rõ.
- Nếu rule local mâu thuẫn với `AGENTS.md` hoặc source runtime hiện tại, ưu tiên `AGENTS.md` và code.
- Sau mỗi task có thay đổi đáng kể, append `.local-agent-rules/CHANGELOG.md` và giữ file này local-only.

## Tham chiếu nhanh

- Bối cảnh dự án: `docs/agent/PROJECT_CONTEXT.md`
- Chỉ mục skill: `docs/agent/SKILL_INDEX.md`
- Rule chọn skill: `docs/agent/SKILL_SELECTION_RULES.md`
