# ECC_ADAPTER_POLICY

## Mục tiêu

Cho phép dùng `ECC/` như nguồn tham khảo mạnh hơn cho workflow, skill, rule, prompt, UI, test, và agent pattern, kể cả copy hoặc chuyển đổi khi thật sự hữu ích. Mọi phần lấy từ ECC phải đi qua lớp adapter của PBL5 để giữ runtime hiện tại: `.NET + React + PostgreSQL + Ollama`.

Policy này áp dụng khi task nhắc tới ECC, cần nâng chất lượng code bằng skill/rule external, hoặc đang làm việc cross-surface như Presentation Extraction, Document Understanding, Slide Studio, UI/UX, schema/API, hoặc verification.

## Thứ tự ưu tiên skill

1. Dùng PBL5 trước:
   - `.agents/skills/ai-ocr`
   - `.agents/skills/dotnet-backend`
   - `.agents/skills/testing-checklist`
   - playbook trong `docs/agent/skills/slide-studio.md`
   - playbook trong `docs/agent/skills/react-frontend.md`
   - playbook trong `docs/agent/skills/postgres-efcore.md`
2. Dùng ECC làm nguồn bổ sung có chọn lọc:
   - `frontend-patterns`, `frontend-slides`, `design-system`, `accessibility` cho UI/UX, Slide Studio, DocumentUnderstandingPanel.
   - `backend-patterns`, `dotnet-patterns`, `csharp-testing` cho service/API/test structure.
   - `postgres-patterns`, `database-migrations` cho schema, migration, JSONB, query, rollback thinking.
   - `verification-loop`, `e2e-testing`, `browser-qa` cho kiểm chứng flow UI/runtime.
   - `prompt-optimizer`, `eval-harness` cho prompt, rubric, extraction quality, và eval.
3. Nếu PBL5 và ECC mâu thuẫn, ưu tiên `AGENTS.md`, source runtime, và playbook PBL5. Ghi rõ phần ECC nào bị bỏ qua nếu nó đáng chú ý.

## Quy tắc copy/chuyển đổi

### Có thể copy trực tiếp

Chỉ copy trực tiếp các phần generic, ít rủi ro và không kéo dependency/runtime mới:

- checklist
- naming pattern
- test matrix
- UI review heuristic
- prompt rubric
- acceptance criteria
- manual QA flow
- accessibility checklist

### Nên chuyển đổi thay vì copy

Các phần sau phải được chuyển đổi sang shape hiện tại của PBL5 trước khi đưa vào source:

- React component structure
- CSS/layout
- API contract
- EF entity/migration
- JSONB/persisted result shape
- prompt schema
- background job/progress payload
- deck/item/image candidate flow
- verification command hoặc CI assumption

### Không copy nguyên

Không copy nguyên các phần làm PBL5 lệch stack hoặc chạm private/global state:

- installer
- hook
- global config
- MCP config
- credential/env setup
- package/runtime requirement
- external Python/SVG/PPTX pipeline nếu trái stack PBL5
- paid SaaS/OCR/doc-processing flow nếu không được user yêu cầu rõ

## PBL5 adapter checklist

Sau khi lấy ý tưởng hoặc code từ ECC, phải đổi ngữ cảnh về PBL5:

- Map file path sang repo hiện tại.
- Giữ API shape và frontend consumer hiện có, hoặc kiểm tra consumer trước khi đổi.
- Giữ persisted shape/migration rules; không tạo migration nếu scope chưa chốt.
- Cập nhật cả `vi` và `en` nếu thêm UI text.
- Giữ fallback behavior cho OCR, Document Understanding, slide generation, image planning, và progress polling.
- Giữ DI/repository/service pattern đang dùng trong `Program.cs`, Core, Infrastructure, Services.
- Không thêm model, port, package, secret, hoặc external service assumption âm thầm.

## Presentation Extraction + UI/UX default

Khi làm Presentation Extraction, Document Understanding, Slide Studio, hoặc UI/UX extraction:

- Dùng `ai-ocr` để xác định extraction contract, confidence, grounding, chart/image warnings.
- Dùng `slide-studio` để đảm bảo deck/item/image/preview/editor shape không drift.
- Dùng `dotnet-backend` cho API/service/DI/contract.
- Dùng `react-frontend` cho panel, inspector, badge, warning banner, i18n, và responsive state.
- Dùng `postgres-efcore` khi đổi `document_understanding_runs.result`, entity mapping, migration, hoặc repository query.
- Dùng ECC `frontend-slides` như taxonomy/rhythm/layout inspiration, không thay renderer bằng pipeline external.
- Dùng ECC `frontend-patterns`, `design-system`, và `accessibility` để polish UI nhưng triển khai theo component/CSS hiện có.

## Verify

Với docs-only change, inspect Markdown và link/reference bằng `rg` là đủ. Với code change:

- Backend: `dotnet build ELearnGamePlatform.sln`
- Frontend: `cd client; npm run build`
- Startup-sensitive backend: `dotnet run --project src/ELearnGamePlatform.API`

Nếu không verify được, ghi rõ lệnh nào đã bỏ qua hoặc thất bại và lý do.
