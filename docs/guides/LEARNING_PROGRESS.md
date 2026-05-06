# Learning Progress and Test Mode

## Phase 6 Goal

Phase 6 turns StudyHub learning actions into research-ready measurement data. The backend records each learner attempt, maintains per-question progress, stores test sessions/results, and exposes CSV exports for experiment analysis.

## Entity Design

`LearningAttempt` stores one learning action:

- `Id`, `UserId`, `DocumentId`, `QuestionId`
- `Mode`: `Flashcard`, `Quiz`, `Test`, `Streak`
- `SelectedAnswer`, `IsCorrect`, `ResponseTimeMs`
- `TestResultId` for attempts created by Test Mode
- `CreatedAt`

`LearningProgress` stores the current user-question state:

- `Id`, `UserId`, `DocumentId`, `QuestionId`
- `AttemptCount`, `CorrectCount`, `WrongCount`
- `CurrentStreak`, `BestStreak`
- `LastReviewedAt`, `MemoryScore`, `MasteryScore`
- `Level`: `Weak`, `Learning`, `Good`, `Mastered`
- `UpdatedAt`

`LearningTestResult` stores a full test session:

- `Id`, `UserId`, `DocumentId`, `TestSessionId`
- `TotalQuestions`, `CorrectCount`, `WrongCount`, `Score`
- `StartedAt`, `SubmittedAt`, `DurationMs`
- `TestType`: `PreTest`, `PostTest`, `Retention`, `PracticeTest`
- `Status`: `InProgress`, `Completed`
- `QuestionIdsJson`, `ResultSnapshotJson`, `CreatedAt`

Learning history is preserved when questions are regenerated. Existing questions are archived, and learning rows keep their question references.

## Scoring Formula

The progress service calculates:

```text
AccuracyScore = CorrectCount / AttemptCount * 100
RecencyScore = decayFactor * 100
StreakScore = CurrentStreak / BestStreak * 100
SpeedScore = response-time bucket, or 50 when responseTimeMs is null

MasteryScore = 0.5 * AccuracyScore
             + 0.2 * RecencyScore
             + 0.2 * StreakScore
             + 0.1 * SpeedScore

MemoryScore = MasteryScore * decayFactor
decayFactor = 1 / (1 + daysSinceLastReview * 0.1)
```

Scores are clamped to `0..100`. First attempts, null response time, zero attempts, and old review dates are handled safely. Levels are:

- `0..39`: `Weak`
- `40..69`: `Learning`
- `70..84`: `Good`
- `85..100`: `Mastered`

## API Examples

All endpoints require `Authorization: Bearer <jwt>`. The API uses the current JWT user and does not accept client-provided `userId`.

### Record Attempt

`POST /api/learning/attempts`

```json
{
  "documentId": 12,
  "questionId": 45,
  "mode": 2,
  "selectedAnswer": "B",
  "responseTimeMs": 8420
}
```

Response:

```json
{
  "id": 7,
  "userId": "3",
  "documentId": 12,
  "questionId": 45,
  "attemptCount": 3,
  "correctCount": 2,
  "wrongCount": 1,
  "currentStreak": 1,
  "bestStreak": 2,
  "memoryScore": 70.25,
  "masteryScore": 71.4,
  "level": 3
}
```

For `Flashcard`, send `isCorrect` from self-assessment instead of relying on `selectedAnswer`.

### Progress Summary

`GET /api/learning/progress/summary/{documentId}`

```json
{
  "totalQuestions": 10,
  "attemptedQuestions": 6,
  "averageMasteryScore": 68.5,
  "averageMemoryScore": 64.3,
  "weakCount": 2,
  "masteredCount": 1
}
```

### Start Test

`POST /api/learning/tests/start`

```json
{
  "documentId": 12,
  "count": 10,
  "testType": 4
}
```

Response questions omit `correctAnswer` and `explanation` so Test Mode does not reveal answers before submit.

```json
{
  "testSessionId": "a58e4f28-e69f-4f18-9d8e-53ce6022f81a",
  "testResultDraftId": 21,
  "documentId": 12,
  "testType": 4,
  "startedAt": "2026-05-06T06:00:00Z",
  "questions": [
    {
      "id": 45,
      "questionText": "Which option describes the main concept?",
      "questionType": "MultipleChoice",
      "options": [
        { "key": "A", "text": "First option" },
        { "key": "B", "text": "Second option" }
      ],
      "difficulty": "Medium",
      "topic": "Introduction"
    }
  ]
}
```

### Submit Test

`POST /api/learning/tests/submit`

```json
{
  "testSessionId": "a58e4f28-e69f-4f18-9d8e-53ce6022f81a",
  "durationMs": 185000,
  "answers": [
    {
      "questionId": 45,
      "selectedAnswer": "B",
      "responseTimeMs": 8420
    }
  ]
}
```

Response includes result details after final submit:

```json
{
  "id": 21,
  "testSessionId": "a58e4f28-e69f-4f18-9d8e-53ce6022f81a",
  "documentId": 12,
  "totalQuestions": 10,
  "correctCount": 8,
  "wrongCount": 2,
  "score": 80,
  "durationMs": 185000,
  "testType": 4,
  "masteryScoreAfterTest": 76.5,
  "memoryScoreAfterTest": 75.8,
  "answers": [
    {
      "questionId": 45,
      "selectedAnswer": "B",
      "correctAnswer": "B",
      "isCorrect": true,
      "responseTimeMs": 8420
    }
  ],
  "weakQuestions": []
}
```

Submitting the same completed `testSessionId` returns the persisted result snapshot and does not double count attempts.

## CSV Export

CSV export endpoints are current-user scoped:

- `GET /api/learning/export/attempts.csv`
- `GET /api/learning/export/test-results.csv`
- `GET /api/learning/export/progress.csv`

Common filters:

- `documentId`
- `fromDate`
- `toDate`

Additional filters:

- `mode` for attempts, using enum number or name accepted by ASP.NET model binding
- `testType` for test results

Examples:

```text
GET /api/learning/export/attempts.csv?documentId=12&mode=Quiz
GET /api/learning/export/test-results.csv?fromDate=2026-05-01T00:00:00Z&testType=PracticeTest
GET /api/learning/export/progress.csv?documentId=12
```

CSV values are escaped for commas, newlines, quotes, and null values.

## Frontend Integration

StudyHub uses `client/src/services/api.js`:

- Quiz and Streak call `learningService.recordAttempt` after each submitted answer.
- Flashcards call `learningService.recordAttempt` after self-assessment.
- Test Mode calls `learningService.startTest`, stores answers locally, and calls `learningService.submitTestResult` only at the end.
- Progress summary calls `learningService.getDocumentSummary` and refreshes after learning attempts or test submit.
- CSV helpers return blobs from `learningService.exportAttemptsCsv`, `exportProgressCsv`, and `exportTestResultsCsv`.

## Known Limitations

- Export is scoped to the current user only. Admin-wide experiment export is not enabled in Phase 6.
- Runtime API verification requires PostgreSQL to be available because startup applies EF migrations.
- Test question selection currently uses the available multiple-choice bank order and count from the start request.
