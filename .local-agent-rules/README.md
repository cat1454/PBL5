# Local Agent Rules

Thư mục này áp dụng cho Agent làm việc trong repo hiện tại.

## Mục tiêu

Bộ rule này giúp:

- Giao task rõ hơn cho Agent.
- Giúp Agent làm việc ổn định hơn trong repo thật.
- Ưu tiên sửa gốc vấn đề thay vì patch triệu chứng.
- Giảm suy đoán, giảm sửa sai hướng, giảm sửa lan ngoài scope.
- Cho phép Agent gợi ý prompt viết lại khi prompt của người dùng mơ hồ, thiếu cấu trúc hoặc dễ dẫn đến output kém.

Bộ rule này có thể được đưa lên git để chia sẻ trong repo.

## Các file chính

- `01-agent-prompt.rule.md`: Cách prompt Agent cho đúng.
- `02-agent-execution.rule.md`: Cách Agent nên thực hiện task.
- `03-agent-debug.rule.md`: Quy tắc debug và tìm root cause.
- `04-agent-refactor.rule.md`: Quy tắc refactor có kiểm soát.
- `05-agent-review.rule.md`: Quy tắc review code.
- `06-agent-done-check.rule.md`: Checklist trước khi chốt task.
- `07-prompt-correction.rule.md`: Quy tắc để Agent gợi ý prompt viết lại.
- `08-agent-change-log.rule.md`: Quy tắc ghi log vận hành sau mỗi task.
- `CHANGELOG.md`: Log local để Agent append lại thay đổi đã làm theo từng task.

`CHANGELOG.md` được giữ local-only bằng `.git/info/exclude`, không mặc định đưa lên git.

## Nguyên tắc tổng

- Đây là rule cho Agent, không phải phát biểu tổng quát về mọi AI.
- Nếu prompt đã rõ, Agent nên làm việc ngay.
- Nếu prompt thiếu hoặc sai cấu trúc, Agent nên nêu vấn đề ngắn gọn và đề xuất prompt viết lại tốt hơn.
- Nếu Agent có thể giả định an toàn và vẫn làm được, Agent có thể vừa nêu giả định, vừa tiếp tục xử lý.
- Agent phải ưu tiên đọc file liên quan, xác định root cause, sửa trong phạm vi nhỏ nhất có thể, rồi verify nếu môi trường cho phép.
- Agent không được tự mở rộng scope, rewrite lớn, đổi API, thêm dependency hoặc sửa lan sang module khác nếu prompt không cho phép.
- Sau mỗi task có xử lý đáng kể, Agent phải cập nhật `.local-agent-rules/CHANGELOG.md` để note lại đã làm gì.
- Nếu task không sửa file, Agent vẫn phải ghi rõ là không có thay đổi file và đã trả lời, phân tích, review hoặc gợi ý gì.
