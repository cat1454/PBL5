# Seed Classroom Demo Tool

Công cụ dùng để seed dữ liệu demo phục vụ phát triển (local dev/demo) và kiểm thử các tính năng:
- Leaderboard (Bảng xếp hạng)
- Analytics (Phân tích học tập)
- Empirical Difficulty Scoring (Chấm điểm theo độ khó thực nghiệm)
- Assignment Attempts (Lượt làm bài)
- Student History & Teacher Attempts Page

> [!WARNING]
> Công cụ này chỉ dùng cho môi trường phát triển local dev/demo. **KHÔNG** sử dụng trên môi trường Production.

## Yêu cầu trước khi chạy
1. Database PostgreSQL đã được migrate đầy đủ (API đã chạy ít nhất một lần để tự động áp dụng các migration mới nhất).

## Cách chạy công cụ
Tại thư mục gốc của repository (thư mục chứa file `ELearnGamePlatform.sln`), chạy lệnh sau trong PowerShell hoặc Terminal:

```bash
dotnet run --project tools/SeedClassroomDemo
```

## Dữ liệu được tạo ra
Khi chạy, công cụ sẽ tự động khởi tạo hoặc tái sử dụng:

### 1. Tài khoản Demo
- **Teacher (Giáo viên)**:
  - Email: `teacher.demo@elearn.local`
  - Password: `Password123!`
  - Vai trò: Instructor
- **Students (Học sinh)**:
  - Danh sách email: `student01@elearn.local` đến `student12@elearn.local`
  - Password: `Password123!`
  - Vai trò: Learner

### 2. Classroom Workspace (Lớp học)
- Tên lớp: **"Lớp Demo Leaderboard"**
- Giáo viên quản lý lớp: `teacher.demo@elearn.local`
- Danh sách thành viên: Gồm giáo viên và toàn bộ 12 học sinh trên (trạng thái Active).

### 3. Tài liệu & Câu hỏi
- 1 tài liệu: **"Demo Leaderboard Knowledge Base"** thuộc sở hữu của giáo viên.
- 10 câu hỏi trắc nghiệm (Q1 - Q10) được gán vào tài liệu với độ khó phân bố:
  - Q1 - Q3: Easy (Dễ)
  - Q4 - Q7: Medium (Trung bình)
  - Q8 - Q10: Hard (Khó)

### 4. Bộ câu hỏi (Question Set)
- Tên bộ câu hỏi: **"Bộ câu hỏi Demo Leaderboard"** chứa 10 câu hỏi trên, được phân chia theo SectionCode:
  - Q1 - Q3: `Knowledge`
  - Q4 - Q7: `Understanding`
  - Q8 - Q10: `Application`

### 5. Bài kiểm tra (Assignments)
Công cụ tạo ra 3 bài kiểm tra để phục vụ các tình huống test khác nhau:
1. **Bài kiểm tra Percent Demo (Assignment A)**: Chế độ tính điểm phần trăm (Percent). Có 10 học sinh tham gia làm bài, một số học sinh làm 2 lần (do giới hạn tối đa 2 lần).
2. **Bài kiểm tra Độ khó thực nghiệm (Assignment B)**: Chế độ chấm điểm trọng số theo độ khó thực tế của học sinh (Empirical Difficulty). Bài kiểm tra được đóng (Closed) tự động qua service.
   - Phân bố tỷ lệ làm đúng thực tế của 12 học sinh đối với 10 câu hỏi được phân chia rõ rệt để kiểm thử thuật toán điều chỉnh trọng số:
     - Q1: 11/12 làm đúng (câu rất dễ)
     - Q2: 10/12 làm đúng
     - Q3: 9/12 làm đúng
     - Q4: 7/12 làm đúng
     - Q5: 6/12 làm đúng
     - Q6: 5/12 làm đúng
     - Q7: 4/12 làm đúng
     - Q8: 3/12 làm đúng
     - Q9: 2/12 làm đúng
     - Q10: 1/12 làm đúng (câu rất khó)
3. **Bài kiểm tra Đang mở (Assignment C)**: Chế độ Percent, bài kiểm tra đang mở để học sinh vào làm (Published).
   - `student01`, `student02`: Đã nộp bài (Submitted).
   - `student03`, `student04`: Đang làm dở (InProgress, đã trả lời một vài câu).
   - Các học sinh còn lại: Chưa bắt đầu làm bài.

## Tính chất Idempotent (Chạy lại nhiều lần)
Công cụ được thiết kế để có thể chạy lại nhiều lần một cách an toàn mà không sinh ra dữ liệu trùng lặp vô hạn:
- Các tài khoản, lớp học, tài liệu, câu hỏi và bộ câu hỏi nếu đã tồn tại sẽ được tái sử dụng.
- Đối với 3 bài kiểm tra trên, mỗi khi chạy lại công cụ sẽ xóa toàn bộ các lượt làm bài (attempts) cũ của chúng và tiến hành seed mới, đảm bảo tính nhất quán và độ chính xác của phân phối điểm số và trọng số độ khó.
