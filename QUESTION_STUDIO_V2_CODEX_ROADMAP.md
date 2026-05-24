# Question Studio v2 — Codex Implementation Roadmap

> Dùng file này làm spec giao Codex/agent code.
>
> Mục tiêu: thay thế flow sinh câu hỏi cũ bằng pipeline mạnh hơn: sinh câu hỏi nháp số lượng lớn, kiểm định, khử trùng lặp, để người dùng xem/sửa/chọn rồi mới import vào ngân hàng câu hỏi thật.

---

## 0. Bối cảnh dự án hiện tại

Dự án: **ELearn Game Platform**.

Stack hiện tại:

- Backend: ASP.NET Core 8 Web API
- Data access: Entity Framework Core 8
- Database: PostgreSQL
- AI local: Ollama
- OCR: Tesseract + ImageSharp
- PDF extraction: PdfPig
- DOCX processing: OpenXML
- Frontend: React 18 + React Router + Axios

Các flow hiện có:

- Upload tài liệu
- OCR / text extraction
- AI analysis để lấy `summary`, `main topics`, `key points`, `language`
- Sinh câu hỏi tự động
- Quiz
- Flashcards
- Slide Studio

Flow sinh câu hỏi cũ hiện tại:

```text
Document processed
  ↓
QuestionGeneratorService
  ↓
AI sinh câu hỏi
  ↓
Verifier / auto-repair
  ↓
Lưu trực tiếp vào bảng Question
  ↓
Quiz / Flashcards sử dụng ngay
```

Vấn đề của flow cũ:

- AI sinh xong lưu thẳng vào `Question`, người dùng không có bước duyệt nháp.
- Không có draft bank riêng để sinh nhiều rồi lọc.
- Sinh số lượng lớn dễ tạo câu trùng, câu yếu, câu chưa đủ grounding.
- Job progress cũ còn phụ thuộc in-memory state.
- Khó test ngưỡng thất bại, max generation, pass rate, duplicate rate.

Hướng mới:

```text
Document
  ↓
QuestionSourceUnit
  ↓
Canonical QuestionDraft
  ↓
Verify + dedup canonical
  ↓
Variant QuestionDrafts
  ↓
Verify + dedup variants
  ↓
Draft Bank
  ↓
User review / edit / select
  ↓
Import into Question
  ↓
Quiz / Flashcards
```

---

## 1. Quyết định kiến trúc

### 1.1. Bỏ flow cũ ở tầng sinh câu hỏi

Bỏ hướng:

```text
AI generation → save directly into Question
```

Thay bằng:

```text
AI generation → QuestionDraft → user review → import → Question
```

### 1.2. Không xóa `Question`

Giữ lại `Question` vì:

- Quiz đang đọc từ `Question`.
- Flashcards đang đọc từ `Question`.
- `GameSession` có thể đang tham chiếu question ids.
- Đây là bảng câu hỏi đã publish/import để học thật.

### 1.3. Draft không được xuất hiện trong game

Rule bắt buộc:

```text
Quiz / Flashcards chỉ dùng Question đã import/published.
Không được đọc trực tiếp QuestionDraft.
```

### 1.4. Tách rõ 2 vùng dữ liệu

```text
QuestionDraft = câu hỏi nháp, có thể lỗi, cần người dùng duyệt
Question      = câu hỏi thật, đã được import, dùng cho học tập
```

---

## 2. Mục tiêu sản phẩm

### 2.1. Người dùng cần làm được gì?

Người dùng có thể:

1. Chọn một tài liệu đã xử lý xong.
2. Tạo ngân hàng câu hỏi nháp.
3. Chọn chế độ sinh:
   - fast
   - balanced
   - quality
   - max_draft
4. Chọn số lượng draft mục tiêu.
5. Chọn loại câu hỏi:
   - MultipleChoice
   - Flashcard
   - ShortAnswer
   - TrueFalse
   - FillBlank
   - MatchPair
6. Xem danh sách draft.
7. Lọc draft theo:
   - status
   - type
   - difficulty
   - topic
   - score
   - duplicate warning
8. Sửa câu hỏi draft.
9. Chọn một số câu tốt.
10. Import vào `Question`.
11. Dùng Quiz / Flashcards với câu đã import.

### 2.2. Tư duy chính

```text
Max Draft không có nghĩa là import hết.
Max Draft nghĩa là sinh nhiều câu nháp để người dùng lọc ra câu tốt.
```

---

## 3. Entity/database cần thêm

Tạo migration mới:

```text
AddQuestionStudioV2
```

Thêm 4 entity:

1. `QuestionGenerationRun`
2. `QuestionSourceUnit`
3. `QuestionDraft`
4. `QuestionReviewEvent`

---

## 4. Entity: QuestionGenerationRun

### 4.1. Mục đích

Lưu mỗi lần người dùng tạo một batch draft question.

### 4.2. C# model đề xuất

```csharp
public class QuestionGenerationRun
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public string UserId { get; set; } = "demo-user";

    public string Mode { get; set; } = "balanced";
    // fast | balanced | quality | max_draft

    public string Status { get; set; } = "Pending";
    // Pending | Running | Completed | Failed | Cancelled

    public string Stage { get; set; } = "Created";
    // Created | ExtractingSourceUnits | GeneratingCanonical | VerifyingCanonical
    // GeneratingVariants | VerifyingVariants | Deduplicating | Completed | Failed

    public int TargetDraftCount { get; set; }
    public int GeneratedDraftCount { get; set; }
    public int VerifiedDraftCount { get; set; }
    public int ImportedCount { get; set; }
    public int DuplicateCount { get; set; }
    public int RejectedCount { get; set; }
    public int BorderlineCount { get; set; }
    public int QuarantinedCount { get; set; }

    public string RequestedQuestionTypesJson { get; set; } = "[]";
    public string RequestedDifficultiesJson { get; set; } = "[]";
    public string ModelProfileJson { get; set; } = "{}";
    public string FailureStatsJson { get; set; } = "{}";
    public string MetricsJson { get; set; } = "{}";
    public string ErrorMessage { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
```

### 4.3. Index nên có

```csharp
builder.Entity<QuestionGenerationRun>()
    .HasIndex(x => new { x.DocumentId, x.CreatedAt });

builder.Entity<QuestionGenerationRun>()
    .HasIndex(x => x.Status);
```

---

## 5. Entity: QuestionSourceUnit

### 5.1. Mục đích

Một `QuestionSourceUnit` là một đơn vị tri thức được tách ra từ tài liệu. Từ mỗi unit có thể sinh canonical question và variants.

Ví dụ:

```text
Source unit:
OCR giúp chuyển nội dung trong ảnh hoặc PDF scan thành văn bản để hệ thống phân tích.
```

Có thể sinh:

```text
OCR có vai trò gì trong hệ thống?
```

### 5.2. C# model đề xuất

```csharp
public class QuestionSourceUnit
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public int? GenerationRunId { get; set; }
    public QuestionGenerationRun? GenerationRun { get; set; }

    public string UnitType { get; set; } = "Concept";
    // Concept | Definition | Fact | Process | Comparison | Formula | Table | OCRRisk | SummaryPoint

    public string Content { get; set; } = "";
    public string TopicTag { get; set; } = "";
    public string SourceHash { get; set; } = "";

    public int StartOffset { get; set; }
    public int EndOffset { get; set; }

    public double Confidence { get; set; } = 1.0;
    public string MetadataJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

### 5.3. Index nên có

```csharp
builder.Entity<QuestionSourceUnit>()
    .HasIndex(x => new { x.DocumentId, x.TopicTag });

builder.Entity<QuestionSourceUnit>()
    .HasIndex(x => x.SourceHash);
```

---

## 6. Entity: QuestionDraft

### 6.1. Mục đích

Đây là bảng quan trọng nhất của Question Studio v2.

`QuestionDraft` lưu tất cả câu hỏi AI sinh ra trước khi người dùng import vào `Question`.

### 6.2. C# model đề xuất

```csharp
public class QuestionDraft
{
    public int Id { get; set; }

    public int DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public int GenerationRunId { get; set; }
    public QuestionGenerationRun GenerationRun { get; set; } = null!;

    public int? SourceUnitId { get; set; }
    public QuestionSourceUnit? SourceUnit { get; set; }

    public string Status { get; set; } = "Draft";
    // Draft | Verified | Borderline | Rejected | Quarantined | Imported

    public string DraftKind { get; set; } = "Canonical";
    // Canonical | Variant

    public int? ParentDraftId { get; set; }
    public QuestionDraft? ParentDraft { get; set; }

    public string QuestionText { get; set; } = "";
    public string QuestionType { get; set; } = "MultipleChoice";
    // MultipleChoice | Flashcard | ShortAnswer | TrueFalse | FillBlank | MatchPair

    public string OptionsJson { get; set; } = "[]";
    public string CorrectAnswer { get; set; } = "";
    public string Explanation { get; set; } = "";

    public string Difficulty { get; set; } = "Medium";
    // Easy | Medium | Hard

    public string LearningObjective { get; set; } = "Understand";
    // Remember | Understand | Apply | Analyze

    public string TopicTag { get; set; } = "";

    public double GroundingScore { get; set; }
    public double AnswerScore { get; set; }
    public double ClarityScore { get; set; }
    public double DuplicateScore { get; set; }
    public double OverallScore { get; set; }

    public int RepairCount { get; set; }
    public string FailureReason { get; set; } = "";
    public string SourceEvidence { get; set; } = "";
    public string StemHash { get; set; } = "";
    public string MetadataJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? VerifiedAt { get; set; }
    public DateTime? ImportedAt { get; set; }
}
```

### 6.3. Index nên có

```csharp
builder.Entity<QuestionDraft>()
    .HasIndex(x => new { x.DocumentId, x.Status });

builder.Entity<QuestionDraft>()
    .HasIndex(x => new { x.GenerationRunId, x.Status });

builder.Entity<QuestionDraft>()
    .HasIndex(x => new { x.TopicTag, x.Difficulty });

builder.Entity<QuestionDraft>()
    .HasIndex(x => x.StemHash);
```

---

## 7. Entity: QuestionReviewEvent

### 7.1. Mục đích

Lưu lịch sử thao tác của user trên draft.

Các action:

- Accept
- Reject
- Edit
- Import
- Quarantine
- Restore

### 7.2. C# model đề xuất

```csharp
public class QuestionReviewEvent
{
    public int Id { get; set; }

    public int QuestionDraftId { get; set; }
    public QuestionDraft QuestionDraft { get; set; } = null!;

    public string UserId { get; set; } = "demo-user";

    public string Action { get; set; } = "";
    // Accept | Reject | Edit | Import | Quarantine | Restore

    public string BeforeJson { get; set; } = "{}";
    public string AfterJson { get; set; } = "{}";
    public string Note { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

---

## 8. Update entity Question

Không xóa `Question`.

Có thể thêm các field sau nếu chưa có:

```csharp
public string Status { get; set; } = "Published";
// Published | Archived

public int? SourceDraftId { get; set; }
public double? QualityScore { get; set; }
```

Rule:

```text
QuestionDraft.Status = Imported sau khi import.
Question.SourceDraftId = QuestionDraft.Id.
Question.Status = Published.
```

---

## 9. Folder/service backend mới

Tạo folder:

```text
src/ELearnGamePlatform.Services/QuestionStudio/
```

Các service cần có:

```text
IQuestionSourceUnitExtractor.cs
QuestionSourceUnitExtractor.cs

ICanonicalQuestionGenerator.cs
CanonicalQuestionGenerator.cs

IQuestionVariantGenerator.cs
QuestionVariantGenerator.cs

IQuestionDraftVerifier.cs
QuestionDraftVerifier.cs

IQuestionDraftDeduplicator.cs
QuestionDraftDeduplicator.cs

IQuestionDraftImportService.cs
QuestionDraftImportService.cs

QuestionStudioOrchestrator.cs
```

---

## 10. Pipeline backend mới

### 10.1. Tổng quan

```text
QuestionStudioOrchestrator.StartRunAsync(request)
  ↓
Create QuestionGenerationRun
  ↓
Extract source units
  ↓
Generate canonical drafts
  ↓
Verify canonical drafts
  ↓
Deduplicate canonical drafts
  ↓
Generate variant drafts
  ↓
Verify variant drafts
  ↓
Deduplicate variant drafts
  ↓
Update run metrics
  ↓
Draft Bank ready for review
```

---

## 11. Service: QuestionSourceUnitExtractor

### 11.1. Input

```text
Document.ExtractedText
Document.Summary
Document.MainTopicsJson
Document.KeyPointsJson
```

### 11.2. Output

```text
List<QuestionSourceUnit>
```

### 11.3. Rule extraction

```text
- Ưu tiên main topics và key points.
- Nếu tài liệu dài, chia chunk 500-900 từ.
- Mỗi chunk lấy 3-8 source units.
- Unit quá ngắn hoặc OCR bẩn thì confidence thấp.
- Unit có nhiều ký tự lỗi/OCR noise thì UnitType = OCRRisk.
```

### 11.4. UnitType

```text
Concept
Definition
Fact
Process
Comparison
Formula
Table
OCRRisk
SummaryPoint
```

---

## 12. Service: CanonicalQuestionGenerator

### 12.1. Mục tiêu

Sinh câu hỏi gốc từ source unit.

Canonical question không cần nhiều biến thể. Nó là câu hỏi gốc để sau này sinh variant.

### 12.2. Rule

```text
- Mỗi source unit sinh 1-3 canonical questions.
- Canonical phải bám source evidence.
- Không sinh câu hỏi ngoài tài liệu.
- Không tạo câu quá giống câu đã có.
- Không lưu vào Question, chỉ lưu vào QuestionDraft.
```

### 12.3. DraftKind

```text
QuestionDraft.DraftKind = Canonical
QuestionDraft.ParentDraftId = null
```

---

## 13. Service: QuestionDraftVerifier

### 13.1. Local verifier

Check không cần AI:

```text
- QuestionText không rỗng.
- CorrectAnswer không rỗng nếu type cần đáp án.
- MultipleChoice phải có ít nhất 4 options.
- MultipleChoice không được có options trùng nhau.
- CorrectAnswer phải nằm trong options.
- Explanation không quá ngắn.
- QuestionText không quá dài.
- QuestionText không chứa JSON lỗi / markdown rác.
- SourceEvidence không rỗng với canonical.
```

### 13.2. AI verifier

Check bằng Ollama verification model:

```text
- GroundingScore: câu hỏi có bám tài liệu không?
- AnswerScore: đáp án có đúng không?
- ClarityScore: câu hỏi có rõ không?
- Difficulty fit: độ khó có đúng không?
- Explanation quality: giải thích có hữu ích không?
```

### 13.3. Score mapping

```text
OverallScore = weighted average:
- GroundingScore: 40%
- AnswerScore: 30%
- ClarityScore: 20%
- DuplicateScore: 10%
```

### 13.4. Status rule

```text
OverallScore >= 0.85:
  Status = Verified

0.70 <= OverallScore < 0.85:
  Status = Borderline

0.50 <= OverallScore < 0.70:
  Status = Rejected

OverallScore < 0.50:
  Status = Quarantined
```

---

## 14. Service: QuestionDraftDeduplicator

### 14.1. Mục tiêu

Sinh nhiều draft nhưng không để draft bank thành rác do trùng lặp.

### 14.2. Dedup 3 tầng

#### Tầng 1 — exact hash

```text
Normalize question text → StemHash
```

Normalize rule:

```text
- lowercase
- trim whitespace
- collapse multiple spaces
- bỏ dấu câu thừa
- chuẩn hóa tiếng Việt cơ bản nếu có helper
```

#### Tầng 2 — same source unit + same intent

Nếu cùng:

```text
SourceUnitId
QuestionType
Difficulty
LearningObjective
```

và text gần giống → reject duplicate.

#### Tầng 3 — semantic dedup

Để backlog. Không bắt buộc ở sprint đầu.

### 14.3. Interface đề xuất

```csharp
public interface IQuestionDraftDeduplicator
{
    Task<bool> IsExactDuplicateAsync(QuestionDraft draft, CancellationToken cancellationToken = default);
    Task<bool> IsNearDuplicateAsync(QuestionDraft draft, CancellationToken cancellationToken = default);
    Task MarkDuplicatesAsync(int generationRunId, CancellationToken cancellationToken = default);
}
```

---

## 15. Service: QuestionVariantGenerator

### 15.1. Input

Chỉ lấy canonical draft đã pass:

```text
QuestionDraft.DraftKind = Canonical
QuestionDraft.Status = Verified hoặc Borderline nếu mode = max_draft
```

### 15.2. Output

Variant drafts.

```text
QuestionDraft.DraftKind = Variant
QuestionDraft.ParentDraftId = canonical.Id
```

### 15.3. Loại câu hỏi

Ưu tiên sprint đầu:

```text
MultipleChoice
Flashcard
ShortAnswer
```

Có thể mở rộng sau:

```text
TrueFalse
FillBlank
MatchPair
```

### 15.4. Difficulty

```text
Easy
Medium
Hard
```

### 15.5. Learning objective

```text
Remember
Understand
Apply
Analyze
```

---

## 16. Service: QuestionDraftImportService

### 16.1. Mục tiêu

Import draft đã chọn vào bảng `Question`.

### 16.2. Rule

```text
- Không import draft Rejected / Quarantined.
- Cho import Verified.
- Cho import Borderline nếu user chủ động chọn.
- Sau import, đổi QuestionDraft.Status = Imported.
- Tạo QuestionReviewEvent action = Import.
```

### 16.3. Mapping

```text
QuestionDraft.QuestionText    → Question.QuestionText
QuestionDraft.QuestionType    → Question.QuestionType
QuestionDraft.OptionsJson     → Question.OptionsJson
QuestionDraft.CorrectAnswer   → Question.CorrectAnswer
QuestionDraft.Explanation     → Question.Explanation
QuestionDraft.Difficulty      → Question.Difficulty
QuestionDraft.TopicTag        → Question.TopicTag
QuestionDraft.Id              → Question.SourceDraftId
QuestionDraft.OverallScore    → Question.QualityScore
```

---

## 17. Mode profiles

Tạo config trong code hoặc appsettings:

```json
{
  "QuestionStudio": {
    "Profiles": {
      "fast": {
        "canonicalPerUnit": 1,
        "variantsPerCanonical": 2,
        "maxRepairRounds": 0,
        "targetVerifierScore": 0.70,
        "allowBorderlineDrafts": false
      },
      "balanced": {
        "canonicalPerUnit": 2,
        "variantsPerCanonical": 3,
        "maxRepairRounds": 1,
        "targetVerifierScore": 0.80,
        "allowBorderlineDrafts": false
      },
      "quality": {
        "canonicalPerUnit": 2,
        "variantsPerCanonical": 2,
        "maxRepairRounds": 2,
        "targetVerifierScore": 0.88,
        "allowBorderlineDrafts": false
      },
      "max_draft": {
        "canonicalPerUnit": 3,
        "variantsPerCanonical": 6,
        "maxRepairRounds": 1,
        "targetVerifierScore": 0.72,
        "allowBorderlineDrafts": true
      }
    }
  }
}
```

---

## 18. Failure thresholds

Hard-code trước, sau này đưa vào config.

```text
Nếu duplicate rate > 35%:
  dừng sinh thêm variants cho run đó.

Nếu verifier pass rate < 70%:
  giảm batch size hoặc dừng run.

Nếu repair success rate < 50%:
  không repair tiếp, chuyển draft lỗi sang Quarantined.

Nếu OCR-risk units > 25%:
  không sinh câu hỏi Hard/Analyze từ vùng đó.

Nếu RepairCount > 2:
  dừng repair draft đó.

Nếu Ollama timeout liên tục >= 3 lần:
  mark run Failed với ErrorMessage rõ ràng.
```

---

## 19. API mới

Tạo controller:

```text
src/ELearnGamePlatform.API/Controllers/QuestionStudioController.cs
```

Base route:

```csharp
[Route("api/question-studio")]
[ApiController]
public class QuestionStudioController : ControllerBase
```

---

## 20. API: start run

```http
POST /api/question-studio/runs/start
```

Request:

```json
{
  "documentId": 27,
  "targetDraftCount": 120,
  "mode": "balanced",
  "questionTypes": ["MultipleChoice", "Flashcard", "ShortAnswer"],
  "difficulties": ["Easy", "Medium", "Hard"]
}
```

Response:

```json
{
  "runId": 12,
  "status": "Pending",
  "message": "Question Studio run created."
}
```

Validation:

```text
- documentId phải tồn tại.
- document status phải Completed.
- targetDraftCount > 0.
- mode thuộc fast/balanced/quality/max_draft.
- questionTypes không rỗng.
```

---

## 21. API: get run progress

```http
GET /api/question-studio/runs/{runId}
```

Response:

```json
{
  "runId": 12,
  "documentId": 27,
  "status": "Running",
  "stage": "GeneratingVariants",
  "targetDraftCount": 120,
  "generatedDraftCount": 84,
  "verifiedDraftCount": 66,
  "duplicateCount": 12,
  "rejectedCount": 6,
  "borderlineCount": 10,
  "quarantinedCount": 2,
  "importedCount": 0,
  "errorMessage": ""
}
```

---

## 22. API: list drafts

```http
GET /api/question-studio/drafts?documentId=27&status=Verified&type=MultipleChoice&difficulty=Medium&minScore=0.8
```

Query params:

```text
documentId: required
runId: optional
status: optional
type: optional
difficulty: optional
topic: optional
minScore: optional
page: optional
pageSize: optional
```

Response:

```json
{
  "data": [
    {
      "id": 1,
      "documentId": 27,
      "generationRunId": 12,
      "status": "Verified",
      "draftKind": "Variant",
      "parentDraftId": 3,
      "questionText": "OCR có vai trò gì trong hệ thống?",
      "questionType": "MultipleChoice",
      "options": ["..."],
      "correctAnswer": "...",
      "explanation": "...",
      "difficulty": "Medium",
      "topicTag": "OCR",
      "groundingScore": 0.91,
      "answerScore": 0.89,
      "clarityScore": 0.87,
      "duplicateScore": 0.95,
      "overallScore": 0.90,
      "sourceEvidence": "..."
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 20,
    "totalItems": 120,
    "totalPages": 6
  }
}
```

---

## 23. API: update draft

```http
PUT /api/question-studio/drafts/{draftId}
```

Request:

```json
{
  "questionText": "OCR có vai trò gì trong hệ thống xử lý tài liệu?",
  "options": ["..."],
  "correctAnswer": "...",
  "explanation": "...",
  "difficulty": "Medium",
  "topicTag": "OCR"
}
```

Rule:

```text
- Lưu QuestionReviewEvent action = Edit.
- Sau khi sửa, nên reset score hoặc chạy verify lại nếu có endpoint reverify.
```

---

## 24. API: draft actions

```http
POST /api/question-studio/drafts/{draftId}/accept
POST /api/question-studio/drafts/{draftId}/reject
POST /api/question-studio/drafts/{draftId}/quarantine
POST /api/question-studio/drafts/{draftId}/restore
```

Rule:

```text
- Accept: Status = Verified
- Reject: Status = Rejected
- Quarantine: Status = Quarantined
- Restore: Status = Draft hoặc Borderline tùy score
- Luôn ghi QuestionReviewEvent
```

---

## 25. API: import drafts

```http
POST /api/question-studio/import
```

Request:

```json
{
  "documentId": 27,
  "draftIds": [1, 2, 3, 4, 5]
}
```

Response:

```json
{
  "importedCount": 5,
  "skippedCount": 0,
  "skippedDraftIds": []
}
```

Rule:

```text
- Không import draft Rejected / Quarantined.
- Không import draft đã Imported.
- Nếu duplicate với Question đã tồn tại thì skip hoặc báo warning.
```

---

## 26. Frontend mới

Tạo folder:

```text
client/src/components/question-studio/
```

Các component:

```text
QuestionStudioPage.js
DraftGenerationPanel.js
DraftProgressPanel.js
DraftFilters.js
DraftQuestionCard.js
DraftBulkActions.js
DraftImportSummary.js
```

---

## 27. Frontend flow

```text
1. User mở document đã Completed.
2. Thay nút Generate Questions cũ bằng "Tạo câu hỏi nháp".
3. Mở QuestionStudioPage.
4. User chọn targetDraftCount, mode, types, difficulties.
5. Bấm Start.
6. UI poll GET /api/question-studio/runs/{runId}.
7. Khi run có draft, UI hiển thị draft list.
8. User lọc / sửa / accept / reject / chọn nhiều.
9. User bấm Import selected.
10. Sau import, UI hiển thị số câu đã import.
11. User có thể vào Quiz / Flashcards.
```

---

## 28. UI DraftQuestionCard

Mỗi card cần hiển thị:

```text
- Question text
- Question type
- Difficulty
- Learning objective
- Topic tag
- Overall score
- Grounding score
- Answer score
- Clarity score
- Duplicate warning nếu có
- Status badge
- Source evidence
- Parent canonical nếu là variant
```

Buttons:

```text
- Sửa
- Duyệt
- Bỏ
- Cách ly
- Import
```

Bulk actions:

```text
- Select all visible
- Import selected
- Reject selected
- Accept selected
```

---

## 29. UI mode labels

```text
Fast:
Sinh nhanh, ít kiểm tra hơn, phù hợp demo nhanh.

Balanced:
Cân bằng giữa số lượng và chất lượng, nên dùng mặc định.

Quality:
Ít câu hơn nhưng kiểm tra kỹ hơn.

Max Draft:
Sinh nhiều câu nháp để người dùng lọc, không tự import toàn bộ.
```

---

## 30. Không cho game đọc draft

Kiểm tra các service/controller game:

```text
GamesController
Quiz service / endpoint
Flashcard service / endpoint
```

Rule:

```text
Chỉ query Question.
Không query QuestionDraft.
Nếu thêm Question.Status thì chỉ lấy Published.
```

---

## 31. Repository layer

Tạo repository/interfaces nếu project đang dùng pattern này:

```text
IQuestionGenerationRunRepository
QuestionGenerationRunRepository

IQuestionSourceUnitRepository
QuestionSourceUnitRepository

IQuestionDraftRepository
QuestionDraftRepository

IQuestionReviewEventRepository
QuestionReviewEventRepository
```

Hoặc nếu project đang dùng DbContext trực tiếp trong service, giữ style hiện tại của repo, nhưng cần nhất quán.

---

## 32. Dependency Injection

Đăng ký trong `Program.cs`:

```csharp
builder.Services.AddScoped<IQuestionSourceUnitExtractor, QuestionSourceUnitExtractor>();
builder.Services.AddScoped<ICanonicalQuestionGenerator, CanonicalQuestionGenerator>();
builder.Services.AddScoped<IQuestionVariantGenerator, QuestionVariantGenerator>();
builder.Services.AddScoped<IQuestionDraftVerifier, QuestionDraftVerifier>();
builder.Services.AddScoped<IQuestionDraftDeduplicator, QuestionDraftDeduplicator>();
builder.Services.AddScoped<IQuestionDraftImportService, QuestionDraftImportService>();
builder.Services.AddScoped<QuestionStudioOrchestrator>();
```

Nếu dùng repository:

```csharp
builder.Services.AddScoped<IQuestionDraftRepository, QuestionDraftRepository>();
builder.Services.AddScoped<IQuestionGenerationRunRepository, QuestionGenerationRunRepository>();
builder.Services.AddScoped<IQuestionSourceUnitRepository, QuestionSourceUnitRepository>();
builder.Services.AddScoped<IQuestionReviewEventRepository, QuestionReviewEventRepository>();
```

---

## 33. Background job strategy

Sprint đầu có thể dùng cách hiện tại nếu repo chưa có job runner bền vững.

Nhưng cần cải thiện so với flow cũ:

```text
- Progress không chỉ nằm trong RAM.
- QuestionGenerationRun phải lưu stage/status/count vào PostgreSQL.
- Nếu app restart, user vẫn xem được run trước đó và draft đã sinh.
```

Tạm thời:

```text
POST start tạo QuestionGenerationRun trong DB.
Background Task.Run chạy orchestrator.
Mỗi stage update run vào DB.
```

Sau này:

```text
Thay Task.Run bằng persistent job queue / Hangfire / background worker.
```

---

## 34. Prompt mẫu cho AI generation

### 34.1. Canonical generation prompt

```text
You are generating grounded study questions from a Vietnamese learning document.

Use only the provided source unit.
Do not invent facts outside the source.
Return valid JSON only.

Source unit:
{{sourceUnit}}

Topic:
{{topicTag}}

Generate {{count}} canonical questions.
Each question must include:
- questionText
- questionType
- correctAnswer
- explanation
- difficulty
- learningObjective
- sourceEvidence

Allowed questionType:
- MultipleChoice
- ShortAnswer

Output JSON schema:
{
  "questions": [
    {
      "questionText": "...",
      "questionType": "MultipleChoice",
      "options": ["A", "B", "C", "D"],
      "correctAnswer": "...",
      "explanation": "...",
      "difficulty": "Easy|Medium|Hard",
      "learningObjective": "Remember|Understand|Apply|Analyze",
      "sourceEvidence": "..."
    }
  ]
}
```

### 34.2. Variant generation prompt

```text
You are creating variants of a verified canonical study question.

Do not change the underlying factual answer.
Do not invent facts outside the source evidence.
Return valid JSON only.

Canonical question:
{{canonicalQuestion}}

Correct answer:
{{correctAnswer}}

Source evidence:
{{sourceEvidence}}

Generate variants with these types:
{{questionTypes}}

Generate variants with these difficulties:
{{difficulties}}

Output JSON schema:
{
  "variants": [
    {
      "questionText": "...",
      "questionType": "MultipleChoice|Flashcard|ShortAnswer|TrueFalse|FillBlank|MatchPair",
      "options": [],
      "correctAnswer": "...",
      "explanation": "...",
      "difficulty": "Easy|Medium|Hard",
      "learningObjective": "Remember|Understand|Apply|Analyze",
      "sourceEvidence": "..."
    }
  ]
}
```

### 34.3. Verifier prompt

```text
You are verifying a generated study question against source evidence.

Evaluate only based on the provided source evidence.
Return valid JSON only.

Question:
{{questionText}}

Options:
{{options}}

Correct answer:
{{correctAnswer}}

Explanation:
{{explanation}}

Source evidence:
{{sourceEvidence}}

Return:
{
  "groundingScore": 0.0,
  "answerScore": 0.0,
  "clarityScore": 0.0,
  "difficultyFitScore": 0.0,
  "explanationScore": 0.0,
  "overallScore": 0.0,
  "failureReason": "",
  "recommendedStatus": "Verified|Borderline|Rejected|Quarantined"
}
```

---

## 35. Sprint plan cho Codex

## Sprint A — Backend schema + API skeleton

### Tasks

```text
1. Add entities:
   - QuestionGenerationRun
   - QuestionSourceUnit
   - QuestionDraft
   - QuestionReviewEvent

2. Add DbSet into ApplicationDbContext.

3. Add EF Core configurations and indexes.

4. Add migration:
   AddQuestionStudioV2

5. Add QuestionStudioController.

6. Add stub endpoints:
   - POST /api/question-studio/runs/start
   - GET /api/question-studio/runs/{runId}
   - GET /api/question-studio/drafts
   - PUT /api/question-studio/drafts/{draftId}
   - POST /api/question-studio/import
```

### Acceptance criteria

```text
- dotnet build passes.
- Migration runs.
- Swagger shows new endpoints.
- Existing Quiz / Flashcards still compile.
```

---

## Sprint B — Source unit extraction + canonical drafts

### Tasks

```text
1. Implement QuestionSourceUnitExtractor.
2. Implement CanonicalQuestionGenerator.
3. Use existing Ollama generation model config.
4. Save canonical questions as QuestionDraft.
5. Do not import into Question automatically.
```

### Acceptance criteria

```text
- From one completed document, system creates QuestionGenerationRun.
- System extracts source units.
- System creates canonical QuestionDraft records.
- DraftKind = Canonical.
- Status starts as Draft or Verified after local checks.
```

---

## Sprint C — Verify + dedup

### Tasks

```text
1. Implement local verifier.
2. Implement AI verifier.
3. Implement score mapping.
4. Implement StemHash.
5. Implement exact duplicate detection.
6. Implement same-source-unit near duplicate detection.
7. Set status:
   - Verified
   - Borderline
   - Rejected
   - Quarantined
```

### Acceptance criteria

```text
- Bad MCQ with duplicate options is rejected.
- MCQ with correct answer missing from options is rejected.
- Duplicate question text is detected.
- Draft score fields are populated.
```

---

## Sprint D — Variant generation

### Tasks

```text
1. Implement QuestionVariantGenerator.
2. Generate variants from canonical Verified drafts.
3. Support first:
   - MultipleChoice
   - Flashcard
   - ShortAnswer
4. Link variant to canonical via ParentDraftId.
5. Verify and dedup variants.
```

### Acceptance criteria

```text
- One canonical can have multiple variants.
- Variant has ParentDraftId.
- Variant does not appear in Quiz until imported.
- Duplicate variants are rejected or quarantined.
```

---

## Sprint E — Import service

### Tasks

```text
1. Implement QuestionDraftImportService.
2. Map QuestionDraft to Question.
3. Add SourceDraftId and QualityScore to Question if needed.
4. Update QuestionDraft.Status = Imported.
5. Create QuestionReviewEvent action = Import.
6. Prevent double import.
```

### Acceptance criteria

```text
- Selected Verified drafts become Question records.
- Imported drafts are not imported twice.
- Quiz / Flashcards can use imported questions.
```

---

## Sprint F — Frontend Question Studio

### Tasks

```text
1. Create client/src/components/question-studio/QuestionStudioPage.js.
2. Create generation panel.
3. Create progress panel.
4. Create draft filters.
5. Create draft cards.
6. Add edit / accept / reject / quarantine / import actions.
7. Replace old Generate Questions UI entry with Question Studio v2.
```

### Acceptance criteria

```text
- User can start draft generation.
- User can see run progress.
- User can list drafts.
- User can filter drafts.
- User can import selected drafts.
- npm run build passes.
```

---

## Sprint G — Metrics + failure report

### Tasks

```text
1. Add metrics to QuestionGenerationRun.MetricsJson.
2. Track:
   - sourceUnitCount
   - canonicalCount
   - variantCount
   - duplicateCount
   - rejectedCount
   - borderlineCount
   - quarantinedCount
   - importedCount
   - verifierPassRate
   - repairSuccessRate
   - averageGenerationTime
   - averageVerifyTime
3. Show metrics in UI summary.
```

### Acceptance criteria

```text
- Each run has measurable quality stats.
- User can see why generated count differs from target count.
- System reports duplicate/rejected/quarantined counts clearly.
```

---

## 36. Backward compatibility rules

### Must keep working

```text
- Upload document
- Document processing
- OCR
- AI analysis
- Quiz
- Flashcards
- Slide Studio
```

### Can deprecate

```text
- Old /api/questions/generate/start UI usage
- Old direct QuestionGeneratorService as main path
```

### Should not delete immediately

```text
- Question entity
- QuestionsController read/update/delete endpoints
- GamesController
- Existing Question records
```

---

## 37. Test checklist

### Backend

```powershell
dotnet build H:\pbl5\ELearnGamePlatform.sln
```

Or:

```powershell
dotnet build src/ELearnGamePlatform.API/ELearnGamePlatform.API.csproj
```

### Frontend

```powershell
cd H:\pbl5\client
npm run build
```

### Manual test flow

```text
1. Start PostgreSQL.
2. Start Ollama.
3. Start backend.
4. Start frontend.
5. Upload a document.
6. Wait until status = Completed.
7. Open Question Studio.
8. Start run with targetDraftCount = 30, mode = balanced.
9. Wait for drafts.
10. Filter Verified drafts.
11. Import 5 drafts.
12. Open Quiz.
13. Confirm imported questions appear.
14. Open Flashcards.
15. Confirm imported questions appear.
```

---

## 38. Build commands

Backend:

```powershell
cd H:\pbl5

dotnet restore

dotnet build
```

Frontend:

```powershell
cd H:\pbl5\client
npm install
npm run build
```

EF migration:

```powershell
cd H:\pbl5\src\ELearnGamePlatform.API

dotnet ef migrations add AddQuestionStudioV2 --project ../ELearnGamePlatform.Infrastructure

dotnet ef database update --project ../ELearnGamePlatform.Infrastructure
```

---

## 39. Commit plan

Suggested commits:

```text
feat(question-studio): add draft bank entities and migration
feat(question-studio): add run and draft API skeleton
feat(question-studio): implement source unit extraction
feat(question-studio): generate canonical question drafts
feat(question-studio): add verifier and dedup pipeline
feat(question-studio): generate question variants
feat(question-studio): import verified drafts into question bank
feat(ui): add question studio draft review page
feat(metrics): add generation run quality report
```

---

## 40. Codex master prompt

Use this prompt directly with Codex:

```text
You are working on ELearn Game Platform.

Goal:
Replace the old direct question generation flow with Question Studio v2.

Current system:
- Backend: ASP.NET Core 8, EF Core 8, PostgreSQL, Ollama.
- Frontend: React 18.
- Current Question entity powers Quiz and Flashcards.
- Old flow generates questions directly into the Question table.

New architecture:
- Do not save generated AI questions directly into Question.
- Create a draft-review-import pipeline:
  Document -> QuestionSourceUnit -> Canonical QuestionDraft -> Verified Canonical
  -> Variant QuestionDrafts -> Draft Bank -> User Review -> Import into Question.

Implementation rules:
1. Keep existing Quiz and Flashcards working by reading only imported/published Question records.
2. Add new entities:
   - QuestionGenerationRun
   - QuestionSourceUnit
   - QuestionDraft
   - QuestionReviewEvent
3. Add EF Core migration.
4. Add QuestionStudioController with:
   - POST /api/question-studio/runs/start
   - GET /api/question-studio/runs/{runId}
   - GET /api/question-studio/drafts
   - PUT /api/question-studio/drafts/{draftId}
   - POST /api/question-studio/import
5. Add services:
   - QuestionSourceUnitExtractor
   - CanonicalQuestionGenerator
   - QuestionVariantGenerator
   - QuestionDraftVerifier
   - QuestionDraftDeduplicator
   - QuestionDraftImportService
   - QuestionStudioOrchestrator
6. Implement mode profiles:
   - fast
   - balanced
   - quality
   - max_draft
7. Add draft statuses:
   - Draft
   - Verified
   - Borderline
   - Rejected
   - Quarantined
   - Imported
8. Add UI page:
   client/src/components/question-studio/QuestionStudioPage.js
9. Replace old Generate Questions UI entry with Question Studio v2.
10. Do not remove old Question entity.
11. Do not let draft questions appear in Quiz or Flashcards until imported.
12. Persist progress into PostgreSQL through QuestionGenerationRun, not only in memory.
13. Keep build passing:
   - dotnet build
   - npm run build

Work in small commits following the sprint order:
Sprint A: schema + API skeleton
Sprint B: source units + canonical drafts
Sprint C: verify + dedup
Sprint D: variants
Sprint E: import service
Sprint F: frontend
Sprint G: metrics
```

---

## 41. Final expected outcome

Sau khi hoàn thành, hệ thống sẽ có flow mới:

```text
Upload document
  ↓
Process / OCR / AI analysis
  ↓
Open Question Studio
  ↓
Generate many draft questions
  ↓
Verify / dedup / score
  ↓
User review / edit / filter
  ↓
Import selected drafts
  ↓
Quiz / Flashcards use imported questions
```

Kết quả mong muốn:

```text
- Sinh được nhiều câu hỏi hơn.
- Có ngưỡng thất bại rõ ràng.
- Có draft bank riêng.
- User không bị ép dùng toàn bộ câu AI sinh.
- Quiz/Flashcards chỉ dùng câu đã import.
- Có metric để biết chất lượng generation.
- Kiến trúc đủ mạnh để mở rộng thêm MatchPair, Weakness Mode, Test Mode sau này.
```
