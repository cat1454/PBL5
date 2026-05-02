# Quy tắc refactor của Agent

## Khi nào được refactor

Agent được refactor khi:

- Bug nằm ở contract hoặc flow gốc.
- Logic bị lặp lại từ 2 nơi trở lên.
- Tên gọi làm hiểu sai domain.
- Một hàm đang ôm quá nhiều trách nhiệm.
- Cấu trúc hiện tại khiến việc sửa bug dễ tạo regression.

Agent không nên refactor khi:

- Người dùng chỉ cần một hotfix nhỏ.
- Chưa đủ context để hiểu flow.
- Không có cách verify sau khi sửa.
- Refactor chỉ để tăng độ "đẹp" bề ngoài.
- Refactor làm mở rộng phạm vi vượt quá yêu cầu ban đầu.

## Rule refactor

1. Dọn flow dữ liệu trước khi thêm abstraction.
2. Đưa logic về nơi gần nguồn sự thật nhất.
3. Đặt tên theo domain, không đặt tên theo implementation tạm thời.
4. Giữ nguyên behavior bên ngoài nếu chưa được phép đổi.
5. Mỗi refactor phải có lý do chức năng rõ ràng.
6. Không gom nhiều refactor không liên quan vào cùng một patch.
7. Sau refactor phải verify lại các flow bị ảnh hưởng trực tiếp.

## Dấu hiệu refactor sai hướng

- Tách file nhiều hơn nhưng flow khó hiểu hơn.
- Thêm abstraction nhưng chỉ có một nơi dùng.
- Đổi tên hàng loạt mà không sửa được vấn đề thật.
- Refactor làm thay đổi behavior ngoài ý muốn.
- Sửa một bug nhỏ nhưng kéo theo thay đổi quá nhiều module.

## Nguyên tắc an toàn

Refactor phải phục vụ mục tiêu sửa lỗi, giảm lặp logic, làm rõ domain hoặc giảm rủi ro regression.

Agent không được refactor lớn chỉ vì code "chưa đẹp" nếu người dùng không yêu cầu và không có lợi ích chức năng rõ ràng.
