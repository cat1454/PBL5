# Thiết kế thực nghiệm tối thiểu cho demo NCKH của PBL5

## Mục tiêu thực nghiệm

Thực nghiệm này nhằm kiểm tra liệu PBL5 có thể hỗ trợ:

- tạo nội dung học bằng AI từ tài liệu đầu vào;
- học tập bằng quiz, flashcards, và streak-based practice;
- tạo slide hỗ trợ xây dựng bài giảng tự động;
- cải thiện ghi nhớ, điểm số, và mức hài lòng của người dùng.

Mục tiêu của bản thiết kế là phục vụ **demo NCKH tối thiểu**, bám sát năng lực hiện có của repo và không đòi hỏi sửa database, migration, dependency, hay runtime source code.

## Phạm vi năng lực hiện có của hệ thống

### Các thành phần có thể dùng trực tiếp trong thực nghiệm

- Upload tài liệu và xử lý OCR:
  - `src/ELearnGamePlatform.API/Controllers/DocumentsController.cs`
  - `src/ELearnGamePlatform.Services/OCR/TesseractOcrService.cs`
- Phân tích nội dung và sinh câu hỏi:
  - `src/ELearnGamePlatform.API/Controllers/QuestionsController.cs`
  - `src/ELearnGamePlatform.Services/AI/QuestionGeneratorService.cs`
- Học tập qua Study Hub:
  - `client/src/components/StudyHub.js`
  - `client/src/components/QuizGame.js`
  - `client/src/components/FlashcardGame.js`
  - `client/src/components/StreakGame.js`
- Tạo slide và chỉnh sửa slide:
  - `src/ELearnGamePlatform.API/Controllers/SlidesController.cs`
  - `client/src/components/FolderStudio.js`
  - `client/src/components/SlideStudio.js`
- Tổ chức nguồn học liệu theo workspace:
  - `src/ELearnGamePlatform.API/Controllers/WorkspacesController.cs`
  - `src/ELearnGamePlatform.API/Services/WorkspaceService.cs`

### Những gì chưa nên tuyên bố là đã có

- Thuật toán mastery model hoặc spaced repetition.
- Test mode độc lập hoàn chỉnh ở frontend.
- Hệ thống thu thập và phân tích dữ liệu nghiên cứu nội bộ.
- Tính năng khảo sát hài lòng tích hợp sẵn.

## Thiết kế nghiên cứu tối thiểu

### 1. Nhóm mẫu đề xuất

- Đối tượng phù hợp:
  - sinh viên đại học,
  - học sinh THPT,
  - hoặc nhóm người học có cùng môn/chuyên đề.
- Cỡ mẫu tối thiểu để demo:
  - `n = 20-30` người nếu chỉ cần pilot study;
  - nếu có thể, `n >= 30` sẽ thuận lợi hơn cho phân tích thống kê cơ bản.
- Điều kiện chọn mẫu:
  - cùng học một chủ đề hoặc bộ tài liệu giống nhau,
  - chưa học sâu tài liệu ngay trước thời điểm thử nghiệm,
  - có khả năng thao tác web cơ bản.

### 2. Quy trình thực nghiệm

Quy trình khuyến nghị:

1. Chuẩn bị một hoặc vài tài liệu học chuẩn.
2. Upload tài liệu vào PBL5 để hệ thống:
   - OCR/trích xuất văn bản,
   - phân tích nội dung,
   - sinh question bank,
   - sinh slide hỗ trợ bài giảng.
3. Cho người tham gia làm `pre-test`.
4. Cho người tham gia học bằng hệ thống:
   - xem tài liệu AI-processed,
   - làm quiz,
   - xem flashcards,
   - luyện streak,
   - nếu phù hợp, xem slide deck như tài liệu giảng dạy tóm tắt.
5. Cho người tham gia làm `post-test`.
6. Thu khảo sát hài lòng ngay sau khi hoàn thành.
7. Nếu muốn đo ghi nhớ bền hơn, thêm `delayed test` sau 1-7 ngày.

### 3. Vai trò từng tính năng hiện có trong thực nghiệm

- Upload/OCR:
  - dùng để biến tài liệu gốc thành nội dung số xử lý được.
- Question generation:
  - tạo question bank nhanh từ nội dung tài liệu.
- StudyHub:
  - là khu học tập chính cho quiz, flashcards, streak.
- Workspace:
  - quản lý nguồn tài liệu cho từng đợt thực nghiệm.
- Slide Studio:
  - tạo slide bài giảng tự động phục vụ học tập hoặc dạy thử.

## Biến số và chỉ số đo

### 1. Biến độc lập

- Cách học với PBL5:
  - học bằng quiz,
  - học bằng flashcards,
  - học bằng streak,
  - học với slide AI-generated như tài liệu hỗ trợ.

### 2. Biến phụ thuộc

- Điểm `pre-test`.
- Điểm `post-test`.
- Nếu có, điểm `delayed test`.
- Tỉ lệ nhớ đúng sau học.
- Số câu đúng trong quiz hoặc streak.
- Thời gian hoàn thành phiên học hoặc bài kiểm tra.
- Mức hài lòng người dùng theo thang Likert `1-5`.

### 3. Chỉ số có thể lấy trực tiếp hoặc gần trực tiếp từ hệ thống

- `score`, `correctAnswers`, `totalQuestions` từ game session backend:
  - `src/ELearnGamePlatform.API/Controllers/GamesController.cs`
  - `src/ELearnGamePlatform.Core/Entities/GameSession.cs`
- `currentStreak`, `bestStreak` trong frontend:
  - `client/src/components/StudyHub.js`

Lưu ý:

- Các chỉ số trên mới phản ánh hiệu suất trong phiên sử dụng ứng dụng.
- Chúng **không tự động tương đương** với “mức ghi nhớ dài hạn” hay “thành thạo realtime”.

## Cách tính để kiểm tra từng mục tiêu

### 1. Mục tiêu tăng ghi nhớ 15–20%

Có thể định nghĩa một trong hai cách:

- Cách A:
  - `Retention gain (%) = (Post-test correct rate - Pre-test correct rate) / Pre-test correct rate * 100`
- Cách B:
  - dùng `Delayed test` sau 1-7 ngày;
  - so sánh tỷ lệ nhớ đúng giữa trước học và sau học trễ.

Khuyến nghị:

- Nếu có thời gian, ưu tiên `delayed test` vì gần với “ghi nhớ” hơn.
- Nếu chỉ demo nhanh, dùng `pre-test -> post-test` nhưng phải ghi rõ đây là chỉ số gần đúng.

### 2. Mục tiêu cải thiện điểm kiểm tra tối thiểu 10%

- Tính cho từng người:
  - `Improvement (%) = (Post-test score - Pre-test score) / Pre-test score * 100`
- Hoặc dùng chênh lệch điểm tuyệt đối:
  - `Delta score = Post-test score - Pre-test score`

Tiêu chí báo cáo:

- Trung bình nhóm đạt tăng ít nhất `10%`, hoặc
- tỷ lệ lớn người học có cải thiện dương và mức tăng trung bình vượt ngưỡng.

### 3. Mục tiêu kiểm định `p < 0.05`

- Nếu dữ liệu là cặp `pre-test/post-test` trên cùng người học:
  - ưu tiên `paired t-test` nếu dữ liệu gần chuẩn;
  - dùng `Wilcoxon signed-rank test` nếu cỡ mẫu nhỏ hoặc phân phối không chuẩn.
- Nếu có nhiều nhóm học khác nhau:
  - có thể mở rộng sang ANOVA hoặc Kruskal-Wallis ở giai đoạn sau.

Tiêu chí:

- Chỉ kết luận “có ý nghĩa thống kê” khi `p < 0.05`.
- Nếu không đạt, báo cáo trung thực là “chưa đủ bằng chứng thống kê”.

### 4. Mục tiêu tối thiểu 70% người dùng hài lòng

- Dùng khảo sát Likert `1-5` cho các mục:
  - dễ sử dụng,
  - hữu ích cho học tập,
  - câu hỏi phù hợp,
  - flashcards dễ ôn,
  - slide hữu ích cho bài giảng,
  - sẵn sàng dùng lại.
- Quy ước:
  - `4` và `5` được tính là “hài lòng”.
- Tính:
  - `Satisfaction rate = số người chọn 4 hoặc 5 / tổng số người tham gia * 100`

## Dữ liệu cần thu nhưng repo hiện chưa có

Các dữ liệu sau chưa thấy được thu thập hoặc tổng hợp sẵn trong runtime:

- log `pre-test/post-test`;
- log lượt học chuẩn hóa theo user/session/chủ đề;
- log phản hồi hài lòng;
- bộ dữ liệu tổng hợp để chạy thống kê;
- dữ liệu delayed test để đánh giá retention;
- mastery/proficiency theo từng người học và từng chủ đề.

Điều này có nghĩa là:

- repo hiện phù hợp để làm **nền tảng thao tác thực nghiệm**;
- nhưng lớp **đo lường nghiên cứu** vẫn phải thu bổ sung ngoài hệ thống hoặc ở pha phát triển tiếp theo.

## Đề xuất demo NCKH tối thiểu không đổi runtime

### Phương án khuyến nghị

- Dùng PBL5 để:
  - upload tài liệu,
  - tạo question bank,
  - cho người học làm quiz/flashcards/streak,
  - tạo slide phục vụ học hoặc giảng.
- Dùng công cụ ngoài hệ thống để thu dữ liệu:
  - Google Form,
  - Microsoft Forms,
  - hoặc file CSV thủ công.
- Dùng công cụ ngoài hệ thống để phân tích thống kê:
  - Excel,
  - SPSS,
  - R,
  - hoặc Python.

### Bộ dữ liệu tối thiểu nên có

- `participant_id`
- `topic`
- `pre_test_score`
- `post_test_score`
- `delayed_test_score` nếu có
- `quiz_score_in_app`
- `best_streak`
- `study_duration_minutes`
- `satisfaction_overall`
- `satisfaction_usefulness`
- `satisfaction_ui`
- `notes`

### Kịch bản demo gọn

1. Chọn 1 tài liệu môn học.
2. Dùng PBL5 sinh question bank và slide deck.
3. Cho người học làm pre-test 10 câu.
4. Cho học bằng quiz + flashcards + streak trong PBL5 khoảng 15-20 phút.
5. Cho làm post-test 10 câu cùng blueprint.
6. Thu khảo sát hài lòng 5-10 câu Likert.
7. Tổng hợp kết quả vào CSV và phân tích ngoài hệ thống.

## Tiêu chí chấp nhận cho demo NCKH

- Chứng minh được hệ thống chạy xuyên suốt:
  - tài liệu -> AI -> question bank -> study -> slide.
- Có ít nhất một bộ dữ liệu pilot:
  - pre-test/post-test,
  - hài lòng người dùng,
  - và kết quả học tập trong phiên.
- Báo cáo trung thực phần nào là:
  - minh chứng kỹ thuật,
  - phần nào là minh chứng thực nghiệm,
  - phần nào mới là giả thuyết cần kiểm định tiếp.

## Hạn chế nghiên cứu cần ghi rõ

- Chưa có mastery model.
- Chưa có adaptive scheduling hoặc spaced repetition.
- `Streak` hiện là flow UI luyện nhanh, chưa phải thuật toán nhớ dài hạn.
- `Test` chưa có flow độc lập hoàn chỉnh ở frontend.
- Dữ liệu thực nghiệm chưa được hệ thống tự động thu và phân tích.
- Chất lượng question bank và slide vẫn phụ thuộc vào chất lượng tài liệu đầu vào và model AI local.

## Kết luận đề xuất

PBL5 hiện thích hợp để làm:

- nền tảng demo cho đề tài AI hỗ trợ học tập và xây dựng bài giảng;
- môi trường chạy pilot study quy mô nhỏ;
- minh chứng ban đầu rằng hệ thống có thể hỗ trợ tạo quiz, flashcards, streak practice, và slide từ tài liệu học.

PBL5 hiện chưa đủ để tự thân chứng minh:

- tăng ghi nhớ 15–20%,
- tăng điểm tối thiểu 10%,
- `p < 0.05`,
- hoặc `>= 70%` hài lòng,

nếu chưa triển khai lớp thu thập dữ liệu và phân tích thực nghiệm đi kèm.
