# react-frontend

## Khi nào dùng

- Task bắt đầu ở component, screen, route, context, API client, hoặc i18n.
- Cần kiểm tra UI state, polling, navigation, hoặc bilingual copy.
- Cần lần đường đi từ màn hình xuống `client/src/services/api.js`.

## File/thư mục liên quan

- `client/src/App.js`
- `client/src/components/`
- `client/src/services/api.js`
- `client/src/context/`
- `client/src/i18n/translations.js`
- `client/src/i18n/index.js`
- `client/src/App.css`, `client/src/index.css`

## Điều cấm

- Không thêm text mới chỉ ở một ngôn ngữ.
- Không để mojibake hoặc lưu file frontend sai UTF-8.
- Không đổi API contract âm thầm ở frontend để che lỗi backend.
- Không sửa UI slide mà không đọc flow deck/data thật nếu task chạm slide.

## Checklist trước khi sửa

- Xác định route hoặc screen mở đầu.
- Đọc component chính và component con liên quan.
- Đọc `client/src/services/api.js` cho endpoint/payload tương ứng.
- Đọc translation keys nếu task có text user-facing.
- Kiểm tra context hoặc shared state nếu màn hình phụ thuộc auth/language/progress.

## Checklist sau khi sửa

- Xác nhận `vi` và `en` đều được cập nhật nếu có text mới hoặc text đổi.
- Xác nhận route, handler, API payload, và UI state vẫn đồng bộ.
- Rà mojibake trong `client/src` nếu có chỉnh text.
- Nếu task chạm slide/workspace/study flow, đọc lại trạng thái loading/error/empty state.

## Lệnh kiểm tra phù hợp

```powershell
cd client; npm run build
rg -n "BrowserRouter|Routes|Route|axios|translations|setState|useEffect" client/src
rg -n "Ã|Ä|áº|á»|Æ|Â|ï¿½" client/src -S
```
