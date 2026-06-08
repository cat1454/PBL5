# Phase 4 — Empirical Difficulty Weighted Scoring

## 1. Mục tiêu

Phase 4 bổ sung cơ chế chấm điểm theo độ khó thực nghiệm của câu hỏi trong Classroom Assignment.

Ý tưởng chính:

```text
Câu hỏi càng nhiều người trả lời đúng thì được xem là dễ hơn và có trọng số điểm thấp hơn.
Câu hỏi càng ít người trả lời đúng thì được xem là khó hơn và có trọng số điểm cao hơn.
```

Cơ chế này giúp điểm số phản ánh không chỉ số lượng câu đúng, mà còn phản ánh chất lượng câu trả lời theo độ khó thực tế trong lớp.

---

## 2. Tên cơ chế

Tên tiếng Anh:

```text
Empirical Difficulty Weighted Scoring
```

Tên tiếng Việt:

```text
Chấm điểm theo độ khó thực nghiệm của câu hỏi
```

---

## 3. Nguyên tắc thiết kế

Phase 4 giữ nguyên cách chấm điểm phần trăm hiện có và bổ sung thêm một chế độ chấm điểm mới cho assignment.

Hệ thống sẽ có hai chế độ chấm điểm:

```text
Percent
EmpiricalDifficulty
```

Trong chế độ `Percent`, assignment giữ cách tính điểm hiện tại.

Trong chế độ `EmpiricalDifficulty`, điểm chính thức của attempt được tính lại khi giảng viên đóng assignment. Khi đó hệ thống dùng kết quả làm bài của cả lớp để tính trọng số từng câu hỏi.

---

## 4. Công thức toán học

### 4.1. Tỷ lệ đúng có smoothing

Với mỗi câu hỏi `i`:

```text
p_i = (correctCount_i + alpha) / (answeredCount_i + alpha + beta)
```

Trong đó:

```text
correctCount_i  = số người trả lời đúng câu i
answeredCount_i = số người đã trả lời câu i
alpha           = hệ số smoothing đúng
beta            = hệ số smoothing sai
```

Giá trị mặc định:

```text
alpha = 1
beta  = 1
```

Vì vậy công thức mặc định là:

```text
p_i = (correctCount_i + 1) / (answeredCount_i + 2)
```

Smoothing giúp tránh trường hợp số lượt làm còn ít nhưng câu hỏi bị đánh giá quá dễ hoặc quá khó.

---

### 4.2. Trọng số độ khó của câu hỏi

```text
weight_i = minQuestionWeight + (1 - p_i) * (maxQuestionWeight - minQuestionWeight)
```

Giá trị mặc định:

```text
minQuestionWeight = 0.3
maxQuestionWeight = 2.0
```

Ý nghĩa:

```text
p_i cao  → nhiều người đúng → câu dễ → weight_i thấp
p_i thấp → ít người đúng    → câu khó → weight_i cao
```

---

### 4.3. Điểm của một attempt

Với mỗi câu hỏi `i`:

```text
earned_i = weight_i nếu người học trả lời đúng
earned_i = 0 nếu người học trả lời sai
```

Tổng điểm đạt được:

```text
rawScore = sum(earned_i)
```

Tổng điểm tối đa:

```text
maxScore = sum(weight_i của tất cả câu hỏi trong assignment)
```

Điểm phần trăm:

```text
percentScore = rawScore / maxScore * 100
```

Kết quả phần trăm được làm tròn 2 chữ số thập phân.

---

## 5. Ví dụ minh họa

Assignment có 3 câu hỏi và 10 người học đã nộp bài.

| Câu hỏi | Số người đúng | Số người trả lời | p sau smoothing | Trọng số |
|---|---:|---:|---:|---:|
| Câu 1 | 9 | 10 | 10 / 12 = 0.833 | 0.584 |
| Câu 2 | 5 | 10 | 6 / 12 = 0.500 | 1.150 |
| Câu 3 | 1 | 10 | 2 / 12 = 0.167 | 1.717 |

Người học A đúng Câu 1 và Câu 2:

```text
rawScore = 0.584 + 1.150 = 1.734
maxScore = 0.584 + 1.150 + 1.717 = 3.451
percentScore = 1.734 / 3.451 * 100 = 50.25%
```

Người học B chỉ đúng Câu 3:

```text
rawScore = 1.717
maxScore = 3.451
percentScore = 1.717 / 3.451 * 100 = 49.75%
```

Người học B đúng ít câu hơn nhưng đúng câu khó nhất nên điểm gần bằng người học A.

---

## 6. Dữ liệu cần bổ sung

### 6.1. Enum `ClassroomScoringMode`

```csharp
public enum ClassroomScoringMode
{
    Percent = 0,
    EmpiricalDifficulty = 1
}
```

---

### 6.2. Bổ sung vào `ClassroomAssignment`

```csharp
public ClassroomScoringMode ScoringMode { get; set; } = ClassroomScoringMode.Percent;

public decimal MinQuestionWeight { get; set; } = 0.3m;

public decimal MaxQuestionWeight { get; set; } = 2.0m;

public decimal SmoothingAlpha { get; set; } = 1m;

public decimal SmoothingBeta { get; set; } = 1m;
```

Các field này cho phép mỗi assignment cấu hình cách tính trọng số câu hỏi.

---

### 6.3. Entity `ClassroomAssignmentQuestionStat`

Entity này lưu thống kê của từng câu hỏi trong từng assignment.

```csharp
public class ClassroomAssignmentQuestionStat
{
    public int Id { get; set; }

    public int ClassroomAssignmentId { get; set; }

    public int QuestionId { get; set; }

    public int AnsweredCount { get; set; }

    public int CorrectCount { get; set; }

    public decimal SmoothedCorrectRate { get; set; }

    public decimal DifficultyWeight { get; set; }

    public decimal? DiscriminationIndex { get; set; }

    public string? QualityFlag { get; set; }

    public DateTime CalculatedAt { get; set; }
}
```

Ràng buộc dữ liệu:

```text
unique(ClassroomAssignmentId, QuestionId)
```

Mỗi câu hỏi chỉ có một thống kê trong một assignment.

---

## 7. Luồng xử lý

### 7.1. Khi người học nộp attempt

Hệ thống tiếp tục lưu:

```text
QuestionId
SelectedAnswer
IsCorrect
PointEarned tạm thời
Attempt status
SubmittedAt
```

Với assignment dùng `EmpiricalDifficulty`, điểm chính thức được chốt khi assignment được đóng.

---

### 7.2. Khi giảng viên đóng assignment

Khi giảng viên gọi close assignment:

```text
1. Lấy danh sách câu hỏi trong assignment.
2. Lấy toàn bộ submitted attempts của assignment.
3. Tính AnsweredCount và CorrectCount cho từng câu hỏi.
4. Tính SmoothedCorrectRate cho từng câu hỏi.
5. Tính DifficultyWeight cho từng câu hỏi.
6. Lưu ClassroomAssignmentQuestionStat.
7. Tính lại RawScore và PercentScore cho từng submitted attempt.
8. Chuyển assignment sang trạng thái Closed.
```

---

## 8. Quy tắc chấm lại điểm khi close assignment

Với mỗi submitted attempt:

```text
rawScore = tổng DifficultyWeight của các câu trả lời đúng
maxScore = tổng DifficultyWeight của tất cả câu hỏi trong assignment
percentScore = rawScore / maxScore * 100
```

Các attempt chưa submitted không được tính vào điểm chính thức.

---

## 9. Discrimination và quality flag

Phase 4 lưu thêm thông tin đánh giá chất lượng câu hỏi nếu có đủ dữ liệu.

Mục đích:

```text
Phát hiện câu hỏi có dấu hiệu khó hiểu, mơ hồ hoặc không phân loại tốt người học.
```

Các field:

```text
DiscriminationIndex
QualityFlag
```

Gợi ý quality flag:

```text
InsufficientData
LowDiscrimination
SuspiciousItem
```

Cách dùng:

```text
DiscriminationIndex và QualityFlag dùng để hỗ trợ giảng viên review chất lượng câu hỏi.
Điểm chính thức của attempt vẫn dựa trên DifficultyWeight.
```

---

## 10. Migration

Tạo migration:

```text
AddClassroomEmpiricalDifficultyScoring
```

Migration bổ sung:

```text
ClassroomAssignment.ScoringMode
ClassroomAssignment.MinQuestionWeight
ClassroomAssignment.MaxQuestionWeight
ClassroomAssignment.SmoothingAlpha
ClassroomAssignment.SmoothingBeta
ClassroomAssignmentQuestionStats table
```

---

## 11. Service cần cập nhật

Cập nhật `ClassroomAssignmentService` tại các điểm:

```text
Create assignment
Update assignment
Submit attempt
Close assignment
Get assignment detail
Get attempt result
```

Trong `CloseAssignmentAsync`, thêm nhánh xử lý:

```text
Nếu ScoringMode = Percent:
    giữ hành vi hiện có.

Nếu ScoringMode = EmpiricalDifficulty:
    tính question stats.
    lưu stats.
    recalculate submitted attempts.
    đóng assignment.
```

---

## 12. DTO/API cần cập nhật

Các response liên quan assignment có thể bổ sung:

```text
scoringMode
minQuestionWeight
maxQuestionWeight
smoothingAlpha
smoothingBeta
```

Teacher assignment detail sau khi close có thể trả thêm thống kê câu hỏi:

```text
questionId
answeredCount
correctCount
smoothedCorrectRate
difficultyWeight
discriminationIndex
qualityFlag
calculatedAt
```

Student result tiếp tục dùng rule bảo mật hiện có:

```text
Không lộ đáp án trước submit.
ShowAnswerAfterSubmit quyết định có hiện correctAnswer/explanation sau submit hay không.
```

---

## 13. Test cần có

Thêm test vào `ClassroomAssignmentServiceTests`.

### 13.1. Percent scoring giữ nguyên

```text
Assignment dùng ScoringMode = Percent vẫn tính RawScore và PercentScore như trước.
```

---

### 13.2. Câu dễ có trọng số thấp

Seed data:

```text
Câu A có nhiều người đúng.
```

Kỳ vọng:

```text
DifficultyWeight của Câu A thấp hơn câu có ít người đúng.
```

---

### 13.3. Câu khó có trọng số cao

Seed data:

```text
Câu B có ít người đúng.
```

Kỳ vọng:

```text
DifficultyWeight của Câu B cao hơn câu có nhiều người đúng.
```

---

### 13.4. Smoothing hoạt động đúng

Seed data:

```text
answeredCount nhỏ.
```

Kỳ vọng:

```text
SmoothedCorrectRate không bị cực đoan 0 hoặc 1 nếu dữ liệu còn ít.
```

---

### 13.5. Close assignment tạo question stats

Kỳ vọng:

```text
Mỗi câu hỏi trong assignment có một ClassroomAssignmentQuestionStat.
```

---

### 13.6. Close assignment tính lại submitted attempts

Kỳ vọng:

```text
Submitted attempts được cập nhật RawScore và PercentScore theo DifficultyWeight.
```

---

### 13.7. Đúng ít câu khó có thể đạt điểm cao hơn đúng nhiều câu dễ

Seed data:

```text
Student A đúng nhiều câu dễ.
Student B đúng ít câu hơn nhưng đúng câu khó.
```

Kỳ vọng:

```text
Điểm của Student B có thể cao hơn Student A nếu câu đúng của B có DifficultyWeight đủ cao.
```

---

### 13.8. Không submit sau khi assignment closed

Kỳ vọng:

```text
Student không thể submit answer hoặc submit attempt sau khi assignment đã Closed.
```

---

### 13.9. Close assignment không tạo duplicate stat

Kỳ vọng:

```text
Gọi close nhiều lần không tạo duplicate ClassroomAssignmentQuestionStat.
```

---

### 13.10. Bảo mật đáp án vẫn giữ nguyên

Kỳ vọng:

```text
Student pre-submit không thấy correctAnswer.
ShowAnswerAfterSubmit=false không lộ correctAnswer sau submit.
ShowAnswerAfterSubmit=true chỉ hiện correctAnswer sau submit.
```

---

## 14. Seed data kiểm thử

Seed data nên tạo một assignment có 3 câu hỏi:

```text
Question 1: dễ
Question 2: trung bình
Question 3: khó
```

Phân bố 10 người học:

```text
Question 1: 9/10 đúng
Question 2: 5/10 đúng
Question 3: 1/10 đúng
```

Kỳ vọng:

```text
Question 1 có DifficultyWeight thấp nhất.
Question 3 có DifficultyWeight cao nhất.
```

Tạo thêm 2 người học mẫu:

```text
Student A đúng Question 1 và Question 2.
Student B chỉ đúng Question 3.
```

Kỳ vọng:

```text
Student B có thể đạt điểm gần bằng hoặc cao hơn Student A tùy trọng số.
```

---

## 15. Build và kiểm thử

Sau khi implement, chạy:

```powershell
dotnet test tests/ELearnGamePlatform.Services.Tests/ELearnGamePlatform.Services.Tests.csproj --filter ClassroomAssignment
```

```powershell
dotnet build ELearnGamePlatform.sln
```

Kết quả mong muốn:

```text
ClassroomAssignment tests pass.
Solution build pass.
```

---

## 16. Definition of Done

Phase 4 hoàn thành khi:

```text
- Assignment có ScoringMode.
- Percent scoring cũ vẫn hoạt động.
- EmpiricalDifficulty scoring tính trọng số theo tỷ lệ đúng có smoothing.
- Close assignment tạo ClassroomAssignmentQuestionStat.
- Close assignment tính lại điểm submitted attempts.
- Câu nhiều người đúng có trọng số thấp hơn.
- Câu ít người đúng có trọng số cao hơn.
- Student không submit được sau khi assignment closed.
- Không tạo duplicate stat khi close nhiều lần.
- Rule bảo mật đáp án vẫn giữ nguyên.
- Tests pass.
- Build pass.
```
