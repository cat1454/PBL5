# Checklist mức đáp ứng yêu cầu NCKH của PBL5

## Mục tiêu đánh giá

Tài liệu này đánh giá mức độ đáp ứng của repo PBL5 đối với hướng đề tài:

> Tích hợp AI trong việc tạo Flashcard, Quiz, Test ứng dụng hỗ trợ xây dựng bài giảng tự động.

## Phạm vi bằng chứng

- Ưu tiên runtime code hiện có trong backend, frontend, entity, repository, controller, service, và `ApplicationDbContext`.
- `README.md`, `AGENTS.md`, và `docs/agent/PROJECT_CONTEXT.md` chỉ được dùng làm tài liệu phụ trợ để đối chiếu luồng hệ thống.
- Không suy rộng từ roadmap hoặc ý tưởng chưa có trong runtime.

## Quy ước đánh giá

- `Đã có`: đã thấy flow runtime rõ ràng ở backend và/hoặc frontend.
- `Một phần`: đã có nền tảng hoặc flow gần đúng nhưng chưa đủ để khẳng định đáp ứng trọn yêu cầu.
- `Chưa có`: chưa thấy triển khai runtime hoặc chưa có dữ liệu/bằng chứng cần thiết.

## Checklist chính

| Yêu cầu | Trạng thái | Bằng chứng trong repo | Thiếu gì | Đề xuất bổ sung tối thiểu để đạt demo NCKH |
| --- | --- | --- | --- | --- |
| Flashcard – Quiz – Test theo tiến trình | Một phần | `Quiz` có API và UI qua `src/ELearnGamePlatform.API/Controllers/GamesController.cs`, `client/src/components/StudyHub.js`, `client/src/components/QuizGame.js`. `Flashcard` có API và UI qua `GamesController.cs`, `client/src/components/FlashcardGame.js`, `StudyHub.js`. `Streak` có giao diện theo tiến trình, thanh progress, streak counter trong `StudyHub.js`. `GameSession` có `GameType.Test` trong `src/ELearnGamePlatform.Core/Entities/GameSession.cs`. | Chưa thấy route/UI `Test` riêng hoàn chỉnh ở frontend. Chưa có flow đánh giá nhiều phần hoặc nhiều đợt theo tiến trình học. `Streak` hiện là biến thể luyện tập, chưa phải test mode độc lập. | Dùng `Quiz`, `Flashcard`, `Streak` làm minh chứng demo học theo tiến trình; bổ sung 1 kịch bản demo “Test” tối thiểu bằng cách tái sử dụng `GameSession` + route frontend riêng sau bước này. |
| Thuật toán cập nhật ghi nhớ/thành thạo realtime | Chưa có | Repo có `GameSession` lưu `score`, `correctAnswers`, `status` trong `src/ELearnGamePlatform.Core/Entities/GameSession.cs`; `StudyHub.js` có `currentStreak`, `bestStreak`, progress cục bộ theo phiên. | Chưa thấy mastery score theo người học, chưa có spaced repetition, Leitner, SM-2, lịch ôn tập, hay persisted proficiency state trong `ApplicationDbContext`. Chưa có cập nhật realtime mức thành thạo theo từng câu hỏi/chủ đề. | Cho demo NCKH tối thiểu, có thể dùng `score`, `correctAnswers`, `bestStreak` làm chỉ số gần đúng trong báo cáo; sau đó bổ sung một bảng/log proficiency riêng nếu muốn khẳng định có thuật toán ghi nhớ thực thụ. |
| Web thực nghiệm tích hợp học tập, kiểm tra, tạo slide | Đã có | Luồng upload/OCR/analysis có trong `src/ELearnGamePlatform.API/Controllers/DocumentsController.cs`, `src/ELearnGamePlatform.Services/OCR/TesseractOcrService.cs`, `src/ELearnGamePlatform.Services/AI/ContentAnalyzerService.cs`. Luồng sinh câu hỏi có trong `QuestionsController.cs` và `QuestionGeneratorService.cs`. Luồng học tập có trong `StudyHub.js`, `QuizGame.js`, `FlashcardGame.js`, `StreakGame.js`. Luồng workspace và slide có trong `WorkspacesController.cs`, `SlidesController.cs`, `client/src/components/FolderStudio.js`, `SlideStudio.js`. | Chưa có lớp “thực nghiệm” chuyên biệt như dashboard khảo sát, phân nhóm thí nghiệm, hay export dữ liệu nghiên cứu. | Dùng chính web hiện tại như hệ thống thực nghiệm MVP; bổ sung quy trình thao tác chuẩn cho người tham gia và biểu mẫu thu dữ liệu ngoài hệ thống. |
| Báo cáo khoa học có dữ liệu thực nghiệm | Chưa có | Repo hiện có luồng tạo tài liệu học và gameplay, nhưng không thấy module thu thập dữ liệu nghiên cứu hoặc tệp dữ liệu thực nghiệm đã có sẵn. `QuestionRepository.cs`, `GamesController.cs`, `GameSessionRepository.cs` chủ yếu phục vụ vận hành ứng dụng. | Chưa có tập dữ liệu pre-test/post-test, nhật ký học tập chuẩn hóa, dữ liệu khảo sát hài lòng, hay pipeline tổng hợp dữ liệu cho báo cáo khoa học. | Thu dữ liệu thủ công bằng Google Form hoặc CSV ngoài hệ thống trong giai đoạn demo; dùng repo để tạo nội dung học và lấy kết quả phiên chơi làm dữ liệu đầu vào cho báo cáo. |
| Mục tiêu tăng ghi nhớ 15–20% | Chưa có | `StudyHub.js` và `GamesController.cs` cho phép làm quiz/flashcard/streak, nhưng chưa có phép đo “trước - sau” hoặc “sau trễ” để chứng minh cải thiện ghi nhớ. | Chưa có định nghĩa chỉ số retention, chưa có baseline, chưa có dữ liệu đo lại sau học. | Trong demo NCKH, định nghĩa retention = tỉ lệ câu trả lời đúng ở post-test hoặc delayed test; thu số liệu ngoài hệ thống và chỉ kết luận khi có mẫu đủ lớn. |
| Cải thiện điểm kiểm tra tối thiểu 10% | Chưa có | `GamesController.cs` có tính `score` và `correctAnswers`; `GameSession` có lưu điểm phiên học. | Chưa có cặp dữ liệu pre-test/post-test trên cùng người học nên chưa thể chứng minh mức tăng tối thiểu 10%. | Thiết kế bài pre-test và post-test cùng blueprint nội dung; dùng app để học giữa hai lần đo; tính mức tăng theo phần trăm sau khi thu dữ liệu. |
| Kiểm định `p < 0.05` | Chưa có | Không thấy code thống kê, pipeline phân tích, hay file dữ liệu tổng hợp cho kiểm định. | Chưa có mẫu nghiên cứu, chưa có dữ liệu cặp, chưa có phép kiểm định nào được thực hiện. | Phân tích ngoài repo bằng Excel/SPSS/R/Python sau khi thu dữ liệu; dùng paired t-test hoặc Wilcoxon tùy phân phối và cỡ mẫu. |
| Tối thiểu 70% người dùng hài lòng | Chưa có | Frontend hiện tập trung vào trải nghiệm học tập và slide, nhưng chưa có survey form hoặc persistence cho phản hồi hài lòng. | Chưa có thang đo Likert, chưa có biểu mẫu đánh giá UX, chưa có dữ liệu tổng hợp hài lòng người dùng. | Dùng khảo sát Likert 1–5 ngoài hệ thống; quy đổi “hài lòng” là mức 4 hoặc 5 và tính tỷ lệ đạt từ tổng số người tham gia. |

## Ghi chú phân tích theo runtime

- `Flashcard` và `Quiz` đã có ở mức runtime, có thể dùng ngay cho demo tính năng AI hỗ trợ học tập.
- `Streak` mới là flow luyện nhanh trong `StudyHub.js`, chưa phải bằng chứng cho thuật toán nhớ dài hạn.
- `Test` mới xuất hiện ở mức entity/session backend qua `GameType.Test`, chưa thấy màn hình hoặc API riêng để xem như một mode hoàn chỉnh.
- Hệ thống đã có nền tảng tốt cho demo kỹ thuật:
  - ingest tài liệu,
  - OCR,
  - AI question generation,
  - study hub,
  - workspace,
  - slide studio.
- Hệ thống chưa có nền tảng đủ mạnh cho “minh chứng khoa học” nếu không bổ sung quy trình thu dữ liệu thực nghiệm.

## Kết luận

### Các yêu cầu đã đạt cho demo

- Web thực nghiệm tích hợp upload tài liệu, OCR, AI question generation, học tập, và tạo slide.
- Flashcard và Quiz ở mức tính năng runtime có thể demo trực tiếp.

### Các yêu cầu mới đạt một phần

- Flashcard – Quiz – Test theo tiến trình:
  - đã có `Flashcard`, `Quiz`, `Streak`;
  - `Test` chưa hoàn chỉnh ở frontend/runtime flow.

### Khoảng trống NCKH còn thiếu

- Chưa có thuật toán cập nhật ghi nhớ/thành thạo realtime.
- Chưa có dữ liệu thực nghiệm chuẩn cho báo cáo khoa học.
- Chưa có bằng chứng định lượng để khẳng định:
  - tăng ghi nhớ 15–20%,
  - tăng điểm kiểm tra tối thiểu 10%,
  - kiểm định `p < 0.05`,
  - tối thiểu 70% người dùng hài lòng.

## Tóm tắt chốt

PBL5 hiện phù hợp để làm **MVP demo kỹ thuật** cho hướng đề tài “AI tạo Flashcard, Quiz và hỗ trợ xây dựng bài giảng tự động”. Tuy nhiên, để nâng thành **demo NCKH có sức thuyết phục**, nhóm cần bổ sung lớp thực nghiệm và đo lường ngoài hệ thống hoặc ở pha phát triển tiếp theo.
