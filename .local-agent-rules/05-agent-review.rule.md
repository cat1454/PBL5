# Quy tắc review của Agent

## Mindset

Khi người dùng yêu cầu review, Agent phải ưu tiên tìm:

- Bug
- Rủi ro regression
- Contract mismatch
- Thiếu validation
- Thiếu test
- Sai luồng dữ liệu hoặc sai trách nhiệm tầng xử lý

Agent không được biến review thành bản tóm tắt thay đổi.

## Format nên trả

1. Trả findings trước.
2. Sắp xếp findings theo mức độ nghiêm trọng.
3. Mỗi finding nên nêu rõ:
   - Vấn đề là gì
   - Tác động nếu không sửa
   - File, vị trí hoặc flow liên quan
   - Hướng sửa đề xuất nếu có
4. Nếu không thấy finding rõ ràng, nói rõ điều đó và nêu residual risk.

## Mức độ nghiêm trọng gợi ý

- `Critical`: Có thể làm crash app, mất dữ liệu, sai bảo mật, sai contract nghiêm trọng.
- `High`: Có thể gây lỗi runtime, regression lớn, hoặc làm hỏng flow chính.
- `Medium`: Có bug hoặc thiếu xử lý edge case nhưng chưa phá flow chính.
- `Low`: Vấn đề nhỏ về maintainability, naming, readability hoặc thiếu polish.

## Điều Agent không nên làm khi review

- Không chỉ tóm tắt file đã đổi.
- Không khen chung chung.
- Không đưa quá nhiều nitpick nếu còn bug quan trọng hơn.
- Không yêu cầu rewrite lớn nếu chỉ cần patch nhỏ.
- Không tự giả định contract nếu chưa kiểm tra nguồn chuẩn.

## Mẫu output review

```md
## Findings

### High - Tiêu đề vấn đề

- Vấn đề:
- Tác động:
- Vị trí liên quan:
- Hướng sửa:

### Medium - Tiêu đề vấn đề

- Vấn đề:
- Tác động:
- Vị trí liên quan:
- Hướng sửa:

## Residual risk

- Phần chưa verify được:
- Flow còn rủi ro:
```
