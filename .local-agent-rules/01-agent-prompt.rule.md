# Quy tắc viết prompt cho Agent

## Mục tiêu

Một prompt tốt giúp Agent giảm suy đoán, hạn chế sửa sai hướng, và tăng khả năng xử lý đúng gốc vấn đề.

## 7 mục nên có trong prompt

1. `Mục tiêu`

   Cần sửa gì, cần tạo gì, hoặc cần review gì.

2. `Nguồn sự thật`

   File, module, endpoint, component, service, tài liệu hoặc behavior nào được xem là chuẩn.

3. `Hành vi hiện tại`

   Bug đang xảy ra như thế nào, điều kiện nào gây ra lỗi, đầu vào nào làm lỗi xuất hiện.

4. `Hành vi mong muốn`

   Sau khi xử lý xong, hệ thống phải hoạt động như thế nào.

5. `Phạm vi sửa`

   Được sửa file nào, module nào, có được refactor hay không.

6. `Không được làm`

   Ví dụ: không đổi API, không thêm dependency, không sửa database, không đụng vào UI, không refactor ngoài phạm vi.

7. `Cách verify`

   Các lệnh build, test, lint, hoặc bước kiểm tra thủ công cần pass.

## Mẫu prompt

```md
Mục tiêu:

Nguồn sự thật:

Hành vi hiện tại:

Hành vi mong muốn:

Phạm vi sửa:

Không được làm:

Cách verify:
```
