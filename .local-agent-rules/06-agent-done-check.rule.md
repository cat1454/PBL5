# Quy tắc kiểm tra trước khi hoàn tất của Agent

## Trước khi chốt task

Agent phải tự kiểm tra:

1. Đã sửa đúng root cause hay chưa.
2. Đã verify bằng cách tốt nhất có thể hay chưa.
3. Có file nào bị thay đổi nhưng không liên quan đến task không.
4. Đã ghi log vào `.local-agent-rules/CHANGELOG.md` hay chưa.
5. Có assumption nào cần nói rõ không.
6. Có edge case mới nào vừa được mở ra không.
7. Nếu không verify được, đã nói thẳng lý do chưa.
8. Có thay đổi nào cần người dùng review kỹ trước khi merge không.

## Mẫu close-out ngắn

```md
Root cause:
...

Đã sửa:
...

Đã verify:
...

Chưa verify được:
...

Còn lại:
...
```
