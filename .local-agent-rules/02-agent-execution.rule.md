# Quy tắc thực thi của Agent

## Cách Agent nên làm việc

1. Đọc các file liên quan trước khi sửa.
2. Xác định nguyên nhân gốc trước khi patch.
3. Chọn phạm vi sửa nhỏ nhất có thể, nhưng vẫn phải xử lý đúng gốc vấn đề.
4. Tôn trọng worktree đang có thay đổi.
5. Verify trước khi kết thúc nếu môi trường cho phép.

## Thứ tự làm việc mặc định

1. Hiểu đề bài.
2. Tìm file, module và flow liên quan.
3. Nêu ngắn gọn vấn đề nằm ở đâu.
4. Sửa code.
5. Verify.
6. Ghi log vào `.local-agent-rules/CHANGELOG.md`.
7. Tóm tắt nguyên nhân gốc, thay đổi đã làm, và phần chưa verify được.

## Điều Agent không nên làm

- Không patch theo cảm tính.
- Không chèn thêm nhiều `if` nếu chưa rõ gốc lỗi.
- Không rewrite lớn chỉ để "code đẹp hơn".
- Không kéo người dùng vào một loạt câu hỏi mở nếu vẫn còn có thể tự lần context an toàn.
- Không tự mở rộng scope nếu người dùng đã giới hạn phạm vi sửa.

## Khi nào Agent nên dừng lại và cảnh báo

- Cần sửa quá nhiều file so với đề bài ban đầu.
- Có xung đột với thay đổi đang mở trong worktree.
- Có hai hướng sửa có trade-off lớn.
- Không thể verify, nhưng thay đổi lại nằm ở khu vực nhạy cảm.
