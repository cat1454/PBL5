# SKILL_INDEX

## Cách dùng nhanh

Chọn skill theo điểm bắt đầu của task. Nếu task chạm nhiều lớp, dùng skill của điểm vào đầu tiên rồi kéo thêm skill liên quan theo call path.

Nếu task cần dùng hoặc chuyển đổi từ `ECC/`, đọc `docs/agent/ECC_ADAPTER_POLICY.md`. PBL5 skill/playbook vẫn là nguồn ưu tiên; ECC là nguồn bổ sung có thể copy hoặc adapter khi hữu ích và không làm lệch runtime.

## Bảng chọn skill

| Loại task | Skill chính | Khi kéo thêm skill |
|---|---|---|
| Controller, service, DI, config bind, repository flow backend | `dotnet-backend` | Thêm `postgres-efcore` nếu chạm schema/query; thêm `ai-ocr` nếu chạm OCR/AI |
| Component, router, API client, i18n, UI state frontend | `react-frontend` | Thêm `slide-studio` nếu là màn hình slide; thêm `testing-checklist` trước khi chốt |
| Entity mapping, `ApplicationDbContext`, repository query, migration history, startup schema | `postgres-efcore` | Thêm `dotnet-backend` nếu thay đổi đi lên controller/service |
| OCR, prompt, chunking, Ollama config, analysis/question generation | `ai-ocr` | Thêm `react-frontend` nếu có progress/status UI; thêm `postgres-efcore` nếu đổi persisted metadata |
| Slide generation, deck/item shape, image sourcing, preview/editor | `slide-studio` | Gần như luôn kéo thêm `dotnet-backend` và `react-frontend` |
| Presentation Extraction, Document Understanding, Slide grounding, UI/UX metadata | `ai-ocr` | Thêm `slide-studio`, `dotnet-backend`, `react-frontend`; thêm `postgres-efcore` nếu đổi JSONB/schema; dùng `ECC_ADAPTER_POLICY` khi lấy pattern từ ECC |
| Chốt task, rà contract, chọn verify nhỏ nhất | `testing-checklist` | Dùng cho mọi task trước khi kết thúc |

## Map theo file/path

- `src/ELearnGamePlatform.API/**`
  - ưu tiên `dotnet-backend`
- `src/ELearnGamePlatform.Infrastructure/Data/ApplicationDbContext.cs`
  - ưu tiên `postgres-efcore`
- `src/ELearnGamePlatform.Infrastructure/Repositories/**`
  - `postgres-efcore`
  - thêm `dotnet-backend` nếu cần đọc ngược lên controller/service
- `src/ELearnGamePlatform.Services/OCR/**`
  - `ai-ocr`
- `src/ELearnGamePlatform.Services/DocumentProcessing/**`
  - `ai-ocr`
- `src/ELearnGamePlatform.Services/AI/SlideGeneratorService.cs`
  - `slide-studio`
  - thêm `ai-ocr` nếu đang chạm prompt/grounding
- `client/src/services/api.js`
  - `react-frontend`
- `client/src/components/SlideStudio*`
  - `slide-studio`
- `client/src/i18n/**`
  - `react-frontend`

## Những gì không coi là skill của PBL5

- Installer/hook/global config từ repo external
- MCP config, credential/env setup, package/runtime requirement external
- Agent orchestration bắt buộc
- TDD/coverage policy áp cứng
- OCR/doc-processing SaaS không có trong runtime hiện tại
- Skill/platform ngoài stack của PBL5

## ECC reusable sources

- UI/UX: `frontend-patterns`, `frontend-slides`, `design-system`, `accessibility`.
- Backend/test: `backend-patterns`, `dotnet-patterns`, `csharp-testing`.
- Database: `postgres-patterns`, `database-migrations`.
- Verification: `verification-loop`, `e2e-testing`, `browser-qa`.
- Prompt/eval: `prompt-optimizer`, `eval-harness`.
