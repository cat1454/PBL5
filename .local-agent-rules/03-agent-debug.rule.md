# Quy tắc debug của Agent

## Mục tiêu

Agent phải debug theo nguyên nhân gốc, không fix theo triệu chứng bề mặt.

## Checklist

1. Tái hiện lỗi nếu có thể.
2. Xác định input, trạng thái và flow gây lỗi.
3. Tìm đúng lớp đang chịu trách nhiệm:
   - UI
   - Service
   - API
   - Repository
   - Transform / Adapter / Mapper
   - Model
   - Contract
4. Nêu root cause bằng một câu ngắn.
5. Sửa tại điểm gần nguồn lỗi nhất có thể.
6. Verify lại happy path và các edge case liên quan.

## Dấu hiệu Agent đang fix sai hướng

- Thêm guard lặp lại ở nhiều lớp.
- Sửa UI để che lỗi contract backend.
- Map hoặc parse cùng một kiểu dữ liệu ở nhiều nơi.
- Fix được case hiện tại nhưng mở ra regression mới.
- Thêm fallback để che lỗi thay vì sửa đúng nguồn lỗi.
- Đổi dữ liệu đầu ra để khớp UI mà không kiểm tra contract gốc.
