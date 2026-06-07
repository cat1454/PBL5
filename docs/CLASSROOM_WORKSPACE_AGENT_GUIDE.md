# Classroom Workspace Roadmap & Repo Reference Guide

> Mục đích: tài liệu này dùng cho AI agent/code agent khi mở rộng PBL5 từ hệ thống học cá nhân thành hệ thống lớp học có giảng viên và người học.  
> Nguyên tắc: **không copy code từ repo tham khảo**. Chỉ học domain model, flow nghiệp vụ, UX pattern, schema gợi ý và cách chia module.

---

## 1. Mục tiêu sản phẩm

Hiện tại PBL5 là hệ thống học cá nhân:

```text
Upload tài liệu
→ OCR / text extraction
→ AI analysis
→ Generate questions
→ Quiz / Flashcards
→ Generate slides
→ Slide Studio
```

Mục tiêu mới là nâng lên thành:

```text
Teacher-managed classroom workspace
```

Tức là:

```text
Giảng viên tạo workspace/lớp học
→ Thêm người học bằng mã lớp hoặc QR
→ Upload tài liệu
→ AI phân tích tài liệu
→ Sinh câu hỏi/slide
→ Review và publish học liệu
→ Tạo assignment
→ Người học làm bài
→ Hệ thống chấm điểm, lưu lịch sử
→ Giảng viên xem leaderboard và analytics
```

---

## 2. Phạm vi role

### 2.1. Teacher / Lecturer

Giảng viên có quyền:

```text
- Tạo workspace/lớp học
- Quản lý người học trong workspace
- Tạo mã join hoặc QR join
- Upload tài liệu
- Chạy OCR/AI analysis
- Sinh câu hỏi từ tài liệu
- Review/chỉnh sửa/xóa câu hỏi
- Gom câu hỏi thành Question Set
- Tạo assignment từ Question Set
- Cấu hình deadline, thời gian làm bài, số lần làm lại
- Sinh slide từ tài liệu
- Chỉnh slide trong Slide Studio
- Publish slide cho người học
- Xem lịch sử làm bài
- Xem bảng xếp hạng
- Xem thống kê lớp
```

### 2.2. Student / Learner

Người học có quyền:

```text
- Join workspace bằng mã lớp hoặc QR
- Xem danh sách lớp đã tham gia
- Xem slide đã được giảng viên publish
- Làm assignment được giao
- Làm quiz/test/flashcard được mở quyền
- Xem điểm cá nhân
- Xem lịch sử làm bài
- Xem leaderboard nếu giảng viên bật
```

Người học không có quyền trong workspace của giảng viên:

```text
- Upload tài liệu để AI phân tích
- Sinh câu hỏi trực tiếp từ tài liệu gốc
- Chỉnh sửa câu hỏi của giảng viên
- Chỉnh slide của giảng viên
- Xem tài liệu/slide/câu hỏi còn ở trạng thái draft
- Xem điểm chi tiết của người học khác
```

---

## 3. Product principle cho AI agent

Khi triển khai, AI agent phải giữ các nguyên tắc sau:

```text
1. Không phá flow cũ:
   Document → Analysis → Question → Game → Slide vẫn phải chạy.

2. Thêm classroom/workspace module theo hướng mở rộng:
   Không rewrite toàn bộ repo.

3. Enforce permission ở backend:
   Frontend chỉ ẩn nút là chưa đủ.

4. Student không được upload/analyze trong teacher workspace.

5. Teacher content phải có trạng thái Draft/Published.

6. Assignment phải lưu Attempt/Answer riêng, không chỉ lưu score cuối.

7. Leaderboard MVP nên tính từ AssignmentAttempt, chưa cần bảng snapshot riêng.

8. Chấm điểm JLPT-inspired phải có section score, total score và pass/fail rule.

9. Không copy code GPL/AGPL từ repo tham khảo. Chỉ học ý tưởng và tự implement lại.
```

---

## 4. Repo tham khảo chính

### 4.1. ClassroomIO

Repo: `classroomio/classroomio`  
Link: `https://github.com/classroomio/classroomio`

Nên học:

```text
- Course/class management
- Invite students
- Add assignments
- Grade assignments
- Student dashboard
- Multi-teacher management
- AI-assisted course/assignment generation
- Mobile-first learning UI
```

Lý do phù hợp:

```text
ClassroomIO rất gần bài toán PBL5 đang muốn làm:
giảng viên quản lý lớp, mời học viên, giao assignment, chấm assignment,
người học có dashboard riêng để xem course/assignment.
```

Áp dụng vào PBL5:

```text
ClassroomIO Course        → PBL5 Workspace
ClassroomIO Student       → PBL5 WorkspaceMember(Student)
ClassroomIO Assignment    → PBL5 Assignment
ClassroomIO Grade         → PBL5 AssignmentAttempt/Score
ClassroomIO Student UI    → PBL5 Student Learning Space
```

Không nên lấy:

```text
- Stack SvelteKit/Hono
- Cấu trúc monorepo của họ
- Code AGPL
```

---

### 4.2. Frappe LMS

Repo: `frappe/lms`  
Link: `https://github.com/frappe/lms`

Nên học:

```text
- Course hierarchy 3 tầng: course → chapter → lesson
- Batch để nhóm learner
- Quiz và assignment trong course
- Certificate/completion concept
- UI đơn giản hơn Moodle
```

Lý do phù hợp:

```text
PBL5 cần gom tài liệu/slide/question thành một learning workspace.
Frappe LMS có cách chia course/chapter/lesson dễ hiểu, không quá nặng.
```

Áp dụng vào PBL5:

```text
Frappe Course             → Workspace/Course
Frappe Chapter/Lesson     → SlideDeck/SlideItem hoặc LearningMaterial
Frappe Batch              → WorkspaceMember group
Frappe Quiz               → QuestionSet/Assignment
Frappe Certificate        → Future: completion badge/certificate
```

Không nên lấy:

```text
- Frappe Framework
- Vue/Frappe UI implementation
- Code AGPL
```

---

### 4.3. StudentQuiz Moodle Plugin

Repo: `studentquiz/moodle-mod_studentquiz`  
Link: `https://github.com/studentquiz/moodle-mod_studentquiz`

Nên học:

```text
- Student-generated question pool
- Question rating/comment
- Usage data per question
- Ranking based on contribution and quiz performance
- Teacher config:
  - anonymous mode
  - allow rating/comment
  - allowed question types
  - points for contributed questions
  - points for answered questions
```

Lý do phù hợp:

```text
Rất sát với ý tưởng:
"giảng viên đưa người học làm list câu hỏi họ đã tạo ra" và "có bảng xếp hạng".
```

Áp dụng vào PBL5:

```text
Question.Source = AI | Teacher | Student
Question.ReviewStatus = Pending | Approved | Rejected
Question.CreatedByUserId
Question.QualityScore
Question.UsageCount
Question.CorrectRate
Question.RatingAverage

StudentQuestionSubmission
- Id
- WorkspaceId
- StudentUserId
- QuestionText
- OptionsJson
- CorrectAnswer
- Explanation
- Status
- ReviewedByUserId
```

MVP chưa nên mở student-generated question ngay. Nên làm sau khi Assignment ổn.

---

### 4.4. jovVix

Repo: `Improwised/jovVix`  
Link: `https://github.com/Improwised/jovVix`

Nên học:

```text
- Real-time quiz session
- Instant feedback
- Live leaderboard
- Points, ranks, avatars
- Admin quiz management
- Answer breakdown
- Performance analytics
- CSV upload/preview/edit questions
```

Lý do phù hợp:

```text
PBL5 đã có quiz/flashcard nhưng còn thiếu cảm giác game.
jovVix phù hợp để học cách làm leaderboard, feedback và result dashboard.
```

Áp dụng vào PBL5:

```text
GameSession/AssignmentAttempt
→ Answer submit
→ Score calculated
→ Leaderboard query
→ Result dashboard
```

MVP không cần realtime WebSocket ngay. Có thể làm:

```text
GET /api/assignments/{assignmentId}/leaderboard
```

Sau này nếu cần realtime thì dùng SignalR.

---

### 4.5. Chamilo LMS

Repo: `chamilo/chamilo-lms`  
Link: `https://github.com/chamilo/chamilo-lms`

Nên học:

```text
- Assignments: create, hand in, grade
- Gradebook
- Badges/certificates with QR code
- Learning analytics:
  - progress
  - course completion
  - participation
  - average time spent
  - average score
- Groups/classes
- Quizzes with time limits and question categories
- Roles and permissions
- Skills management with levels
```

Lý do phù hợp:

```text
Chamilo cho thấy một LMS đầy đủ cần có gradebook, analytics,
groups/classes, permission và skill levels. Đây là nguồn tốt để thiết kế roadmap dài hạn.
```

Áp dụng vào PBL5:

```text
Chamilo Groups/Classes    → Workspace + WorkspaceMember
Chamilo Gradebook         → AssignmentAttempt summary
Chamilo Analytics         → TeacherAnalytics
Chamilo Skills            → JLPT-inspired level/competency score
Chamilo QR Certificate    → Future certificate verification
```

Không nên lấy:

```text
- Scope quá lớn
- PHP/Symfony/Vue implementation
- Code GPL
```

---

### 4.6. Moodle Quiz Analytics Plugin

Repo: `dualcube/moodle-gradereport_quizanalytics`  
Link: `https://github.com/dualcube/moodle-gradereport_quizanalytics`

Nên học:

```text
- Improvement curve qua nhiều attempts
- Peer performance comparison
- Hardest questions
- Attempt snapshot
- Question per category
- Challenging categories across all users
- Challenging categories for current student
- Score distribution
- Question analysis
```

Lý do phù hợp:

```text
PBL5 cần dashboard cho giảng viên và lịch sử học cho người học.
Plugin này rất sát với phần analytics sau khi làm assignment/test.
```

Áp dụng vào PBL5:

```text
TeacherAnalytics:
- Assignment completion rate
- Average score
- Hardest questions
- Weakest topics
- Score distribution
- Students at risk

StudentHistory:
- Attempts over time
- Improvement curve
- Weak topics
- Last score
- Best score
```

---

### 4.7. RELATE

Repo: `inducer/relate`  
Link: `https://github.com/inducer/relate`

Nên học:

```text
- Flexible access rules
- Flexible grading rules
- Text-based reusable course content
- Versioning content
- Gradebook
- Statistics/analytics of student answers
- Live quizzes
```

Lý do phù hợp:

```text
PBL5 muốn chấm điểm kiểu JLPT-inspired.
Muốn làm tốt thì cần rule rõ ràng cho access, deadline, attempt limit, scoring section.
```

Áp dụng vào PBL5:

```text
AssignmentRule / ScoringConfig
- StartAt
- DueAt
- TimeLimitMinutes
- AttemptLimit
- ShowAnswerAfterSubmit
- TotalScaledScore
- SectionMinScore
- PassingTotalScore
```

---

### 4.8. Open edX Platform

Repo: `openedx/openedx-platform`  
Link: `https://github.com/openedx/openedx-platform`

Nên học:

```text
- Tách rõ Authoring và Learning
- CMS/Studio cho người soạn nội dung
- LMS cho người học
- Learner dashboard
- Learning micro-frontend
```

Lý do phù hợp:

```text
PBL5 cũng nên tách UI thành hai không gian:
Teacher Studio và Student Learning Space.
```

Áp dụng vào PBL5:

```text
Open edX Studio           → PBL5 Teacher Workspace Studio
Open edX LMS              → PBL5 Student Learning Space
Authoring MFE             → Slide/Question/Assignment authoring
Learner Home              → Student assignments/history/slides
```

Không nên lấy:

```text
- Kiến trúc quá lớn
- Micro-frontend phức tạp
- Code AGPL
```

---

## 5. Mapping tính năng PBL5 với repo nên học

| Tính năng cần làm | Repo nên học | Bài học chính |
|---|---|---|
| Workspace/lớp học | ClassroomIO, Chamilo, Frappe LMS | Course/class, batch, member, teacher/student |
| Mã join/QR | ClassroomIO, Chamilo | Invite student, join flow, QR as encoded join link |
| Assignment | ClassroomIO, Frappe LMS, Chamilo, RELATE | Giao bài, deadline, attempt, grading |
| Question Set | StudentQuiz, Frappe LMS | Question pool, reusable quiz, review status |
| Người học tạo câu hỏi | StudentQuiz | Contribution score, teacher approval |
| Leaderboard | jovVix, StudentQuiz | Rank theo điểm, contribution, performance |
| Quiz gameplay | jovVix | Instant feedback, game session, rank UI |
| Analytics | Moodle Quiz Analytics, Chamilo, RELATE | Hardest questions, weak topics, progress |
| JLPT-inspired scoring | RELATE, Chamilo | Scoring rule, section score, level/skill |
| Teacher/Student UI split | Open edX, ClassroomIO | Teacher Studio vs Student Learning |
| Slide publish cho người học | Open edX, Frappe LMS | Draft/publish learning content |

---

## 6. Data model đề xuất

### 6.1. Workspace

```csharp
public class Workspace
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string OwnerUserId { get; set; } = "";
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### 6.2. WorkspaceMember

```csharp
public class WorkspaceMember
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string UserId { get; set; } = "";
    public WorkspaceRole Role { get; set; } // Teacher, Student
    public WorkspaceMemberStatus Status { get; set; } // Active, Pending, Removed
    public DateTime JoinedAt { get; set; }
}
```

### 6.3. WorkspaceJoinCode

```csharp
public class WorkspaceJoinCode
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public string Code { get; set; } = "";
    public DateTime? ExpiresAt { get; set; }
    public int? MaxUses { get; set; }
    public int UsedCount { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

QR chỉ encode URL:

```text
https://your-domain.com/join?code=ABC123
```

---

### 6.4. QuestionSet

```csharp
public class QuestionSet
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public int? DocumentId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public PublishStatus Visibility { get; set; } // Draft, Published
    public DateTime CreatedAt { get; set; }
}
```

### 6.5. QuestionSetItem

```csharp
public class QuestionSetItem
{
    public int Id { get; set; }
    public int QuestionSetId { get; set; }
    public int QuestionId { get; set; }
    public int OrderIndex { get; set; }
    public decimal PointWeight { get; set; } = 1;
    public string? SectionCode { get; set; } // Knowledge, Understanding, Application
}
```

---

### 6.6. Assignment

```csharp
public class Assignment
{
    public int Id { get; set; }
    public int WorkspaceId { get; set; }
    public int QuestionSetId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public AssignmentType Type { get; set; } // Quiz, Test, Flashcard, Mixed
    public DateTime? StartAt { get; set; }
    public DateTime? DueAt { get; set; }
    public int? TimeLimitMinutes { get; set; }
    public int AttemptLimit { get; set; } = 1;
    public bool ShuffleQuestions { get; set; }
    public bool ShuffleOptions { get; set; }
    public bool ShowAnswerAfterSubmit { get; set; }
    public ScoringMode ScoringMode { get; set; } // Percent, Points, JlptInspired
    public bool IsPublished { get; set; }
    public string CreatedByUserId { get; set; } = "";
}
```

### 6.7. AssignmentAttempt

```csharp
public class AssignmentAttempt
{
    public int Id { get; set; }
    public int AssignmentId { get; set; }
    public string UserId { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public decimal RawScore { get; set; }
    public decimal PercentScore { get; set; }
    public decimal? ScaledScore { get; set; }
    public AttemptStatus Status { get; set; } // InProgress, Submitted, Expired
    public int DurationSeconds { get; set; }
}
```

### 6.8. AssignmentAnswer

```csharp
public class AssignmentAnswer
{
    public int Id { get; set; }
    public int AttemptId { get; set; }
    public int QuestionId { get; set; }
    public string? SelectedAnswer { get; set; }
    public bool IsCorrect { get; set; }
    public decimal PointEarned { get; set; }
    public int? TimeSpentSeconds { get; set; }
}
```

---

## 7. JLPT-inspired scoring

Không gọi là "chấm điểm JLPT y hệt". Gọi là:

```text
JLPT-inspired section-based scoring
```

MVP đề xuất:

```text
Total score: 180

Section 1: Knowledge        60 điểm
Section 2: Understanding    60 điểm
Section 3: Application      60 điểm
```

Pass rule:

```text
Passed nếu:
- TotalScaledScore >= 100/180
- Mỗi section >= 19/60
```

Kết quả:

```text
Passed
FailedByTotalScore
FailedBySectionMinimum
```

Level gợi ý:

```text
A: 150 - 180
B: 120 - 149
C: 100 - 119
D: 70 - 99
F: < 70
```

Entity gợi ý:

```csharp
public class AssignmentScoringConfig
{
    public int Id { get; set; }
    public int AssignmentId { get; set; }
    public int TotalScaledScore { get; set; } = 180;
    public int PassingTotalScore { get; set; } = 100;
    public int MinimumSectionScore { get; set; } = 19;
    public string SectionsJson { get; set; } = "[]";
}
```

---

## 8. API đề xuất

### 8.1. Workspace

```text
POST   /api/workspaces
GET    /api/workspaces/my
GET    /api/workspaces/{workspaceId}
PUT    /api/workspaces/{workspaceId}
DELETE /api/workspaces/{workspaceId}
```

### 8.2. Members / Join code

```text
POST   /api/workspaces/{workspaceId}/join-codes
GET    /api/workspaces/{workspaceId}/join-codes/active
POST   /api/workspaces/join
GET    /api/workspaces/{workspaceId}/members
DELETE /api/workspaces/{workspaceId}/members/{userId}
```

### 8.3. Question sets

```text
POST   /api/question-sets
GET    /api/workspaces/{workspaceId}/question-sets
GET    /api/question-sets/{questionSetId}
PUT    /api/question-sets/{questionSetId}
DELETE /api/question-sets/{questionSetId}

POST   /api/question-sets/{questionSetId}/items
DELETE /api/question-sets/{questionSetId}/items/{itemId}
```

### 8.4. Assignments

```text
POST   /api/assignments
GET    /api/workspaces/{workspaceId}/assignments
GET    /api/students/me/assignments
GET    /api/assignments/{assignmentId}
PUT    /api/assignments/{assignmentId}
POST   /api/assignments/{assignmentId}/publish
```

### 8.5. Attempts

```text
POST   /api/assignments/{assignmentId}/attempts/start
POST   /api/attempts/{attemptId}/answers
POST   /api/attempts/{attemptId}/submit
GET    /api/students/me/history
GET    /api/assignments/{assignmentId}/attempts
```

### 8.6. Leaderboard / analytics

```text
GET    /api/assignments/{assignmentId}/leaderboard
GET    /api/workspaces/{workspaceId}/leaderboard
GET    /api/workspaces/{workspaceId}/analytics
GET    /api/students/me/analytics
```

### 8.7. Slide publish

```text
POST   /api/slides/{deckId}/publish
GET    /api/workspaces/{workspaceId}/slides/published
GET    /api/students/me/workspaces/{workspaceId}/slides
```

---

## 9. Frontend route đề xuất

### Teacher side

```text
/workspaces
/workspaces/:workspaceId
/workspaces/:workspaceId/members
/workspaces/:workspaceId/documents
/workspaces/:workspaceId/questions
/workspaces/:workspaceId/question-sets
/workspaces/:workspaceId/assignments
/workspaces/:workspaceId/assignments/:assignmentId/results
/workspaces/:workspaceId/leaderboard
/workspaces/:workspaceId/analytics
/workspaces/:workspaceId/slides
```

### Student side

```text
/student/classes
/student/classes/:workspaceId
/student/classes/:workspaceId/slides
/student/assignments
/student/assignments/:assignmentId
/student/attempts/:attemptId/result
/student/history
/student/leaderboard
```

### Join flow

```text
/join?code=ABC123
```

---

## 10. UI/UX cần hướng đến

### Teacher Workspace Studio

Không gian soạn và quản lý:

```text
- Members
- Documents
- Analysis
- Questions
- Question Sets
- Assignments
- Slides
- Results
- Leaderboard
- Analytics
```

### Student Learning Space

Không gian học và làm bài:

```text
- Lớp của tôi
- Bài cần làm
- Slide đã chia sẻ
- Lịch sử làm bài
- Điểm của tôi
```

Học từ Open edX:

```text
Teacher Studio != Student Learning
```

Tách hai không gian sẽ làm UI rõ hơn và tránh người học nhìn thấy chức năng soạn/generate.

---

## 11. Roadmap triển khai

### Phase 1 — Role + Workspace + Join class

Mục tiêu: có lớp học thật.

```text
- Thêm Workspace
- Thêm WorkspaceMember
- Thêm WorkspaceJoinCode
- Teacher tạo workspace
- Teacher tạo mã join/QR
- Student join bằng code/QR
- Backend chặn student upload/analyze trong teacher workspace
- Frontend tách menu Teacher/Student cơ bản
```

Acceptance:

```text
- Teacher thấy danh sách học viên
- Student thấy lớp đã join
- Student không thấy nút upload/analyze
- Join code dùng được và có thể disable
```

---

### Phase 2 — Question Set + Assignment

Mục tiêu: giao bài được.

```text
- Teacher tạo QuestionSet từ questions đã sinh
- Teacher tạo Assignment từ QuestionSet
- Cấu hình deadline/time limit/attempt limit
- Publish Assignment
- Student thấy assignment
- Student start attempt
- Student submit answer
- Hệ thống chấm điểm và lưu history
```

Acceptance:

```text
- Một assignment có thể được giao cho cả workspace
- Student làm bài xong có score
- Teacher xem danh sách attempt
- Student xem lịch sử làm bài
```

---

### Phase 3 — JLPT-inspired scoring + Leaderboard

Mục tiêu: đánh giá năng lực rõ hơn.

```text
- Thêm section cho QuestionSetItem
- Thêm ScoringConfig
- Tính RawScore
- Tính PercentScore
- Tính ScaledScore / 180
- Tính SectionScore
- Tính Passed/Failed
- Leaderboard theo assignment
```

Acceptance:

```text
- Test có điểm tổng /180
- Có điểm từng section
- Có pass/fail rule
- Có leaderboard sau submit
```

---

### Phase 4 — Slide publish + Student slide viewer

Mục tiêu: người học xem slide của giảng viên.

```text
- SlideDeck có Visibility: Draft/Published
- Teacher publish slide deck
- Student chỉ xem slide đã published
- Student không sửa được slide
```

Acceptance:

```text
- Teacher preview/chỉnh slide trước khi publish
- Student xem slide trong class
- Draft không lộ sang student
```

---

### Phase 5 — Analytics

Mục tiêu: dashboard có giá trị giáo dục.

```text
Teacher analytics:
- Assignment completion rate
- Average score
- Hardest questions
- Weakest topics
- Score distribution
- Students at risk

Student analytics:
- Attempt history
- Improvement curve
- Weak topics
- Best score
- Last score
```

Acceptance:

```text
- Teacher biết câu nào/chủ đề nào học viên sai nhiều
- Student biết mình yếu phần nào
```

---

## 12. Implementation notes cho backend

### 12.1. Permission service

Nên tạo service riêng:

```csharp
public interface IWorkspacePermissionService
{
    Task<bool> IsTeacherAsync(int workspaceId, string userId);
    Task<bool> IsStudentAsync(int workspaceId, string userId);
    Task<bool> CanManageWorkspaceAsync(int workspaceId, string userId);
    Task<bool> CanViewPublishedContentAsync(int workspaceId, string userId);
    Task<bool> CanSubmitAssignmentAsync(int assignmentId, string userId);
}
```

Mọi controller liên quan workspace phải gọi permission service.

### 12.2. Không hardcode demo-user lâu dài

Hiện tại repo còn dùng `demo-user`. Module mới nên viết sao cho sau này cắm auth thật được:

```text
CurrentUserId provider
```

Gợi ý:

```csharp
public interface ICurrentUserService
{
    string UserId { get; }
    string? Email { get; }
}
```

Trong MVP có thể fallback `demo-teacher`, `demo-student`, nhưng không hardcode rải rác trong controller.

### 12.3. EF Core migration

Thêm migration riêng:

```powershell
dotnet ef migrations add AddClassroomWorkspaceModule --project ../ELearnGamePlatform.Infrastructure
dotnet ef database update --project ../ELearnGamePlatform.Infrastructure
```

### 12.4. Không thay đổi pipeline AI hiện tại

Tận dụng:

```text
Document
Question
SlideDeck
SlideItem
GameSession
```

Chỉ thêm liên kết với Workspace/Assignment khi cần.

Ví dụ:

```text
Document.WorkspaceId nullable
SlideDeck.WorkspaceId nullable
Question.WorkspaceId nullable hoặc đi qua Document/QuestionSet
```

---

## 13. Implementation notes cho frontend

### 13.1. Teacher UI

Teacher workspace page nên có layout:

```text
Sidebar:
- Overview
- Members
- Documents
- Question Sets
- Assignments
- Slides
- Results
- Leaderboard
- Analytics
```

### 13.2. Student UI

Student page nên cực gọn:

```text
- Lớp của tôi
- Bài cần làm
- Slide đã chia sẻ
- Lịch sử làm bài
- Điểm của tôi
```

### 13.3. Không để student thấy authoring controls

Ẩn các action:

```text
- Upload
- Generate questions
- Generate slide
- Edit question
- Edit slide
- Publish
- Delete classroom content
```

Nhưng vẫn phải enforce ở backend.

---

## 14. Prompt mẫu cho AI coding agent

Dùng prompt này khi yêu cầu AI agent triển khai:

```text
Bạn đang làm trong repo PBL5 (.NET 8 Web API + EF Core + PostgreSQL + React).
Mục tiêu là mở rộng hệ thống từ học cá nhân thành classroom workspace có hai role Teacher/Student.

Không rewrite pipeline cũ. Giữ nguyên flow Document → Analysis → Questions → Games → Slides.
Chỉ thêm module classroom/workspace theo hướng mở rộng.

Ưu tiên Phase 1:
- Workspace
- WorkspaceMember
- WorkspaceJoinCode
- Permission service
- Teacher tạo lớp
- Student join bằng code
- Teacher xem member list
- Student không được upload/analyze trong teacher workspace

Yêu cầu:
- Backend enforce permission.
- Thêm EF Core entities, DbContext config, repositories/services nếu phù hợp.
- Thêm migration.
- Thêm API endpoints REST rõ ràng.
- Frontend tách view Teacher/Student tối thiểu.
- Không copy code từ repo tham khảo.
- Nếu cần học ý tưởng, tham khảo docs/CLASSROOM_WORKSPACE_AGENT_GUIDE.md.
- Sau khi sửa, chạy dotnet build và npm run build nếu môi trường cho phép.
```

---

## 15. Chống scope creep

Không làm trong Phase 1:

```text
- Real-time leaderboard
- Student-generated question pool
- Certificate
- AI grading tự luận
- Full auth system phức tạp
- Parent/guardian account
- Payment/course marketplace
- Mobile app
```

Để backlog:

```text
- Student question contribution
- Contribution leaderboard
- Weakness mode
- Certificate with QR
- Teacher comments on attempts
- Export Excel/CSV
- SignalR realtime quiz
- Class analytics nâng cao
```

---

## 16. Definition of Done tổng

Module classroom coi là ổn khi:

```text
- Teacher tạo workspace được
- Student join bằng code/QR được
- Teacher quản lý member được
- Student không upload/analyze được trong workspace
- Teacher tạo QuestionSet được
- Teacher giao Assignment được
- Student làm Assignment được
- Attempt/Answer/Score được lưu
- Student xem history được
- Teacher xem results được
- Có leaderboard theo assignment
- Có slide published cho student xem
```

---

## 17. Source notes

Các repo/sources đã tham khảo:

```text
ClassroomIO:
https://github.com/classroomio/classroomio

Frappe LMS:
https://github.com/frappe/lms

StudentQuiz:
https://github.com/studentquiz/moodle-mod_studentquiz

jovVix:
https://github.com/Improwised/jovVix

Chamilo LMS:
https://github.com/chamilo/chamilo-lms

Moodle Quiz Analytics:
https://github.com/dualcube/moodle-gradereport_quizanalytics

RELATE:
https://github.com/inducer/relate

Open edX Platform:
https://github.com/openedx/openedx-platform
```

License warning:

```text
Nhiều repo LMS/quiz lớn dùng GPL hoặc AGPL.
Không copy code hoặc component nguyên bản.
Chỉ học idea, domain model, flow, UX pattern rồi tự viết lại.
```
