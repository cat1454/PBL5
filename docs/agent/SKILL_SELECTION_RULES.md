# SKILL_SELECTION_RULES

## Nguyên tắc chung

- Ưu tiên skill theo nơi bug hoặc yêu cầu bắt đầu xuất hiện, không theo nơi lỗi cuối cùng lộ ra.
- Nếu task chạm nhiều lớp, đi theo call path thật của PBL5 thay vì đổi lung tung ở nhiều đầu.
- Nếu rule external mâu thuẫn với PBL5, bỏ rule external và ghi rõ trong báo cáo.

## Chọn skill theo điểm bắt đầu

### 1. Bắt đầu từ backend API

Dùng `dotnet-backend` khi task bắt đầu ở:

- controller
- service API
- DI trong `Program.cs`
- auth/config binding
- repository flow nhìn từ backend

Call path mặc định cần đọc:

1. controller
2. service
3. repository hoặc config
4. entity/interface liên quan

Nếu phát hiện có query/schema/persisted shape trong đường đi, kéo thêm `postgres-efcore`.

## 2. Bắt đầu từ frontend

Dùng `react-frontend` khi task bắt đầu ở:

- component/screen
- router
- API helper trong `client/src/services/api.js`
- i18n/translations
- UI state hoặc progress display

Call path mặc định cần đọc:

1. screen/component
2. `client/src/services/api.js`
3. route hoặc context liên quan
4. i18n/shared styling nếu có text hoặc UI user-facing

Nếu màn hình là slide preview/editor, kéo thêm `slide-studio`.

## 3. Bắt đầu từ database/persistence

Dùng `postgres-efcore` khi task bắt đầu ở:

- `ApplicationDbContext`
- entity mapping
- repository query
- migration history
- startup schema validation

Call path mặc định cần đọc:

1. entity mapping hoặc repository
2. migration history
3. caller phía service/controller
4. consumer phía frontend nếu contract bị ảnh hưởng

Không tự tạo/chỉnh migration nếu task không yêu cầu rõ.

## 4. Bắt đầu từ AI/OCR

Dùng `ai-ocr` khi task bắt đầu ở:

- OCR
- chunking
- prompt
- Ollama settings
- analysis/question generation
- progress/status liên quan AI processing

Call path mặc định cần đọc:

1. processor hoặc AI service
2. config/prompt
3. persisted metadata hoặc progress payload
4. UI polling/status nếu có

Nếu task đụng slide generation, kéo thêm `slide-studio`.

## 5. Bắt đầu từ slide pipeline

Dùng `slide-studio` khi task bắt đầu ở:

- `SlidesController`
- `SlideGeneratorService`
- `SlideImageService`
- `SlideDeckRepository`
- `SlideStudio*`
- deck HTML preview

Call path mặc định cần đọc:

1. generation entrypoint
2. stored deck/item shape
3. image pipeline hoặc preview renderer
4. frontend editor/preview consumer

Không sửa riêng backend hoặc frontend của slide nếu chưa đọc phía còn lại.

## Khi dùng nhiều skill

- Backend + schema: `dotnet-backend` -> `postgres-efcore`
- Frontend + slide: `react-frontend` -> `slide-studio`
- AI/OCR + UI progress: `ai-ocr` -> `react-frontend`
- Slide full-flow: `slide-studio` + `dotnet-backend` + `react-frontend`
- Mọi task trước khi chốt: thêm `testing-checklist`

## Khi phải dừng lại và báo rõ

- Task đòi đổi API contract hoặc persisted shape nhưng user chưa chốt phạm vi.
- Task kéo theo migration/schema change ngoài phạm vi được phép.
- Hướng giải quyết duy nhất là copy rule, installer, hook, hoặc workflow external trái với PBL5.
- Có nhiều đường sửa với trade-off lớn mà không thể khóa bằng context repo.
