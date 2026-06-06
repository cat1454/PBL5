# Background Generation Controls Report

Date: 2026-06-06
Branch: `feature/slidegen-optimization`

## Changed files

| File | Change | Risk |
|---|---|---|
| `src/ELearnGamePlatform.API/Services/SlideGenerationJobStore.cs` | Added paused/cancelled state, cooperative execution gate, document active-job lookup, and transition validation. | Medium: shared in-memory slide job lifecycle changed, with terminal-state guards retained. |
| `src/ELearnGamePlatform.API/Controllers/SlidesController.cs` | Added pause/resume/cancel and deck-delete endpoints plus checkpoints around generation and saves. | Medium: generation can now stop between phases; an Ollama call already in progress is not interrupted. |
| `src/ELearnGamePlatform.Core/Interfaces/ISlideDeckRepository.cs`, `src/ELearnGamePlatform.Infrastructure/Repositories/SlideDeckRepository.cs` | Added deck deletion through the existing repository boundary. | Low: scoped delete of the selected owner-authorized deck and its slide items. |
| `src/ELearnGamePlatform.API/Services/QuestionStudioRunControlStore.cs` | Added singleton cooperative pause/resume/cancel gates for live Question Studio v2 runs. | Medium: controls only live tasks in the current API process. |
| `src/ELearnGamePlatform.API/Controllers/QuestionStudioController.cs` | Added active-run lookup and run control endpoints; blocks duplicate active runs. | Medium: adds persisted Paused/Cancelled transitions while retaining existing owner authorization. |
| `src/ELearnGamePlatform.API/Services/QuestionStudio/QuestionStudioOrchestrator.cs` | Added checkpoints between phases, verifier loops, AI responses, saves, and completion. | Medium: cancellation preserves generated drafts/source units but prevents Completed. |
| `src/ELearnGamePlatform.API/Controllers/QuestionsController.cs` | Added owner-authorized active question-bank deletion by document. | Low: archives active questions only; run, draft, review, and learning records remain. |
| `src/ELearnGamePlatform.API/Program.cs` | Registered the Question Studio run control store as a singleton. | Low: DI registration only. |
| `client/src/services/api.js`, `client/src/services/progress.js` | Added control/delete API methods and treated paused progress as active. | Low: additive frontend service behavior. |
| `client/src/components/FolderStudio.js` | Added slide Pause, Resume, Cancel, and Delete deck actions with loading/disabled states. | Medium: controls the existing polling flow without changing its interval or terminal handling. |
| `client/src/components/question-studio/QuestionStudioPage.js` | Restores active runs after reload and adds run controls and Delete question bank. | Medium: active-run restoration depends on the same API process for live execution. |
| `client/src/i18n/translations.js`, `client/src/styles/pages/folder-studio.css`, `client/src/styles/pages/question-studio.css` | Added Vietnamese/English copy and compact action layouts. | Low: UI-only changes. |
| `tests/ELearnGamePlatform.Services.Tests/GenerationControlStoreTests.cs`, `tests/ELearnGamePlatform.Services.Tests/QuestionStudioReviewFixTests.cs`, `tests/ELearnGamePlatform.Services.Tests/SlideImagePlannerServiceTests.cs`, `client/src/services/progress.test.js` | Added transition, persistence, deletion, and frontend capability coverage. | Low: test-only changes. |

## Slide generation behavior

- Running or queued jobs expose `Pause` and `Cancel`; paused jobs expose `Resume` and `Cancel`.
- `paused` remains active and polling continues, so reload can reconnect to the same in-memory job.
- Pause and cancel are cooperative. The current Ollama call can finish, but the controller checks the gate before and after outline generation, placeholder saves, each slide, image sourcing, slide saves, and final completion.
- Cancel is terminal and does not delete partial output.
- Deck deletion is separate and returns `409 generation_active` while the deck document or folder has a queued, running, or paused job.
- Successful deletion reloads the workspace and clears the selected slide/editor state.

Endpoints:

- `POST /api/slides/generate/{jobId}/pause`
- `POST /api/slides/generate/{jobId}/resume`
- `POST /api/slides/generate/{jobId}/cancel`
- `DELETE /api/slides/{deckId}`

## Question Studio v2 behavior

- Page load queries the latest Pending, Running, or Paused run and restores polling without starting a new run.
- A document cannot start another run while one is Pending, Running, or Paused; the API returns `409 generation_active` with the active run.
- The orchestrator checks pause/cancel between phases, inside verification loops, after AI calls, and before saves/completion.
- Cancelled runs do not transition to Completed. Existing drafts and source units remain available for audit.
- Delete question bank archives active `Question` records through `DeleteByDocumentIdAsync`. Runs, drafts, review events, and learning history are retained.

Endpoints:

- `GET /api/question-studio/documents/{documentId}/runs/active`
- `POST /api/question-studio/runs/{runId}/pause`
- `POST /api/question-studio/runs/{runId}/resume`
- `POST /api/question-studio/runs/{runId}/cancel`
- `DELETE /api/questions/document/{documentId}`

## Compatibility and limits

- Existing owner authorization is applied to every new control and delete endpoint.
- No database schema, migration, package, model name, port, or persisted payload shape changed.
- Resume continues only a task still alive in the current API process. API restart recovery is not checkpoint-resume.
- Pause does not abort an Ollama HTTP request already in progress.
- Legacy StudyHub question generation is unchanged.

## Acceptance tests

- Slide and Question Studio transition tests pass for running to paused to running and running/paused to cancelled.
- Terminal and sealed jobs/runs reject invalid transitions.
- Deck deletion removes the deck and items; question-bank deletion archives active questions.
- Active slide jobs and Question Studio runs restore without issuing a new start request.
- Paused progress remains non-terminal; cancelled progress remains terminal.
- Frontend control visibility tests pass: 4/4.
- Targeted backend generation-control tests pass: 15/15.
- `node --check` passes for the changed frontend JavaScript service/components.
- `dotnet build ELearnGamePlatform.sln --no-restore` passes with 0 warnings and 0 errors.
- `cd H:\pbl5\client && npm run build` passes.
- Full backend test project result: 89 passed, 3 failed. The failures are in Dashboard and Learning tests outside the changed generation-control files:
  - `DashboardHomePayloadTests.BuildDashboardHomePayloadAsync_MarksLatestDeckStaleWhenSourceChangedAfterDeck`
  - `LearningHardeningTests.RecordAttemptAsync_PersistsFlashcardConfidence`
  - `LearningHardeningTests.GetReviewQueueAsync_ClassifiesNewWeakDueAndMasteredQuestions`
