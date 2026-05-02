# Quy tắc ghi changelog của Agent

## Mục tiêu

Sau mỗi prompt của người dùng, Agent phải ghi lại những gì đã thay đổi vào `.local-agent-rules/CHANGELOG.md`.

Changelog này dùng để theo dõi quá trình làm việc local, giúp biết Agent đã xử lý gì, sửa file nào, verify ra sao, và còn điểm nào chưa chắc chắn.

## Rule

1. Mỗi task sau khi hoàn tất đều phải append một entry mới vào `.local-agent-rules/CHANGELOG.md`.

2. Entry phải ghi ngắn gọn:
   - Ngữ cảnh yêu cầu.
   - File nào đã thay đổi.
   - Đã thay đổi gì ở mức độ cao.
   - Đã verify bằng cách nào.
   - Còn gì chưa verify được hoặc assumption nào cần nói rõ.

3. Nếu prompt không dẫn đến sửa file, vẫn phải note rõ:
   - Không có thay đổi file.
   - Agent đã trả lời, phân tích, review, hoặc gợi ý gì.

4. Không copy nguyên diff dài vào changelog.

5. Changelog chỉ là log vận hành local, không thay thế final response cho người dùng.

6. Chỉ append log mới, không rewrite, không xóa, không sắp xếp lại các entry cũ.

7. Nếu task bị dừng giữa chừng, vẫn phải ghi rõ lý do dừng và trạng thái hiện tại nếu đã có thay đổi file.

8. Viết changelog bằng tiếng việt có dấu.

## Mẫu entry

```md
## YYYY-MM-DD HH:mm

Prompt:
- ...

Thay đổi:
- ...

Verify:
- ...

Còn lại:
- ...
```

## Mẫu entry khi không sửa file

```md
## YYYY-MM-DD HH:mm

Prompt:
- ...

Thay đổi:
- Không có thay đổi file.

Phân tích / phản hồi:
- ...

Verify:
- Không áp dụng.

Còn lại:
- ...
```
