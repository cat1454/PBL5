# Slide Generation Bottleneck Report

## A. Ket luan ngan

- Bottleneck chinh: phase `section-summaries` is a sequential per-section AI loop before outline generation. A 10-section scope performs 10 section-summary calls before the deck can move past roughly 12-22%.
- Muc chac chan: high for the static code path; runtime timing still needs a live run with the new `[SlideGen:*]` logs.
- Co phai do AI/model khong: not enough evidence to conclude model slowness. The confirmed issue is orchestration and timing visibility: one AI call per section, then outline polish, per-slide generation, verifier, optional repair, image sourcing, and DB saves.
- Co phai do Workspace mismatch khong: partial. The Workspace request fields match the backend folder endpoint, but folder generation resolves a primary source document while folder deck payloads can still have `documentId = null` because the folder generation context keeps `FolderProjectId` only.

## B. Luong hien tai

| Step | Frontend file/function | Backend file/function | Data passed | Risk mismatch |
|---|---|---|---|---|
| Load workspace studio | `FolderStudio.loadWorkspace` | `GET /workspaces/{workspaceId}`, `GET /workspaces/{workspaceId}/sources`, `GET /slides/folders/{folderId}` | `workspaceId` as folder id | Naming mismatch: UI calls it `workspaceId`, slide API calls it `folderId`. |
| Select source/scope | `FolderStudio` section picker, `buildScopedSectionId` | none yet | `selectedSource.id`, `selectedSectionIds = sourceId::sectionKey` | Good guard: scoped ids prevent sections from another source being accepted. |
| Click Generate deck | `FolderStudio.handleGenerateDeck` | `SlidesController.StartGenerateSlidesForFolder` | `sourceIds`, `selectedSectionIds`, `desiredSlideCount`, `mode`, `scopePolicy`, `confirmLowConfidence` | V1 backend accepts only one `sourceId`; UI sends one selected source. |
| Create job | `slideService.startGenerateSlidesForFolder` | `SlideGenerationJobStore.CreateFolderJob` | `folderId`, `desiredSlideCount`, user id | No debounce existed before patch; fast repeated clicks could create multiple jobs. |
| Poll progress | `FolderStudio` slide progress effect | `GET /slides/generate/progress/{jobId}` | `jobId` | Before patch, effect depended on the whole `progress` object and recreated intervals on each update. |
| Resolve content | none | `ResolveGenerationContextAsync` | primary source id, selected section ids | If selected scope is absent, backend can aggregate all ready folder sources; Workspace UI currently sends selected scope. |
| Summarize sections | none | `SlideGeneratorService.GenerateSectionPlansAsync` | filtered chunks grouped into section plans | Sequential per-section AI calls; 10 sections means 10 summary calls. |
| Generate outline/deck | none | `GenerateOutlineAsync`, `ReplaceForFolderAsync` | outline, placeholder items | Folder deck can save with `FolderProjectId` and nullable `DocumentId`. |
| Generate slide items | none | `GenerateSlideAsync`, verifier, auto-repair, image sourcing | selected evidence chunks, slide item payload | More sequential AI calls happen after summaries; not part of the 17% symptom but affects total runtime. |
| Completion reload | `FolderStudio` poll completion branch | `GET /slides/folders/{folderId}` | `folderId` | Correct deck reload path for Workspace/folder decks. |

## C. Cac mismatch tim thay

| Mismatch | File | Function/Line | Evidence | Impact |
|---|---|---|---|---|
| Poll interval churn | `client/src/components/FolderStudio.js` | progress polling around `poll-start` | Previous effect depended on `progress`; patch makes the interval stable per `jobId` and stops explicitly. | Reduced noisy polling and easier proof of duplicate/non-duplicate requests. |
| Duplicate start window | `client/src/components/FolderStudio.js` | `handleGenerateDeck` around `isStartingGenerationRef` | Patch adds a synchronous ref latch before calling `startGenerateSlidesForFolder`. | Fast double-clicks cannot create multiple folder jobs from the same UI instance. |
| Folder source document id not retained in folder context | `src/ELearnGamePlatform.API/Controllers/SlidesController.cs` | `ResolveGenerationContextAsync`, `SlideGenerationContext.FromFolder` | Backend resolves `primarySource`, but `FromFolder` stores folder id and processed content only. | Payload/logs/decks may not show the source document id for folder runs; low UI impact because Workspace reloads by folder id. |
| ETA can look absurd early | `src/ELearnGamePlatform.API/Controllers/SlidesController.cs` | `UpdateEta` | ETA = elapsed / percent; section summaries live at low global percent. | A slow section at 17% can project a very large remaining time. |
| Section summary loop is intentionally sequential | `src/ELearnGamePlatform.Services/AI/SlideGeneratorService.cs` | `GenerateSectionPlansAsync` | `for` loop awaits one summary call per section. | Main likely explanation for `section 5/10` taking long. |

## D. Diem nghen timing

| Phase | Expected | Actual/Observed | Cause |
|---|---|---|---|
| Request/job start | One POST, one job id | Static path now guarded; live network trace still needed | Frontend ref latch plus disabled state. |
| Section summaries | Roughly proportional to selected section count | Observed UI was at 5/10 and 17% | Backend summarizes each section sequentially. |
| ETA | Stable enough to guide user | Observed 122m | Low percent plus elapsed-time projection exaggerates early slow phases. |
| Polling | One active interval per current job | Previous code recreated intervals on progress object changes | Patched to one interval per `jobId`. |
| Deck reload | Reload folder deck when complete | Static path uses `/slides/folders/{folderId}` | Correct for Workspace/folder decks. |

## E. Root cause ranking

1. Sequential section-summary AI loop. Evidence: `GenerateSectionPlansAsync` awaits one summary call for every section plan.
2. ETA math amplifies early slow phases. Evidence: `UpdateEta` divides elapsed by global percent; section summaries occupy low percent.
3. Poll interval churn. Evidence: previous polling effect depended on `progress`; patch uses stable `jobId` and terminal stop.
4. Missing start latch. Evidence: previous buttons disabled after progress state, but no synchronous in-flight ref before the POST.
5. Folder context document-id ambiguity. Evidence: folder generation resolves a primary source but folder context does not retain its document id.

## F. Patch plan nho nhat da ap dung

- Added frontend duplicate-start protection with `isStartingGenerationRef` and `isStartingGeneration`.
- Changed slide progress polling to start once per current `jobId`, emit debug logs, and stop on completed/failed/job-not-found terminal states.
- Added development-only frontend `[SlideGen]` console logs for click payload, job receipt, poll start/stop, raw progress, and completion reload.
- Added backend `[SlideGen:*]` timing logs for request receipt, job creation, context resolution, outline, placeholder save, per-slide generation, image sourcing, DB saves, progress updates, completion, and failure.
- Added service timing logs for section summaries, outline polish, slide polish, verifier, and auto-repair.
- Did not change endpoint paths, DTO fields, schema, package versions, UI layout, model settings, or persisted deck/source contracts.

## G. Files can sua

| File | Change | Risk |
|---|---|---|
| `client/src/components/FolderStudio.js` | Start latch, stable polling, debug logs | Low; shared Workspace Studio surface, must preserve existing editor/export flows. |
| `src/ELearnGamePlatform.API/Controllers/SlidesController.cs` | Backend job/request/progress/timing logs | Low; observability only. |
| `src/ELearnGamePlatform.Services/AI/SlideGeneratorService.cs` | Section/outline/slide/verifier/repair timing logs | Low; observability only. |
| `docs/working-notes/SLIDE_GENERATION_BOTTLENECK_REPORT.md` | This report | None runtime. |

## H. Acceptance test

- Pressing Generate deck creates only one `POST /slides/folders/{folderId}/generate/start` from the UI; repeated fast clicks are ignored while start is in flight.
- Frontend polls only the current `jobId`, and polling stops when status is `completed` or `failed`.
- Progress phase/message/percent come from backend progress payload and are visible in `[SlideGen]` frontend debug logs.
- Changing tab/state should not call generate again; generate remains click-driven through `handleGenerateDeck`.
- 2 selected sections should run fewer section-summary calls than 10 selected sections; verify with `[SlideGen:{JobId}] Phase=section-summaries Step=section-ai-completed`.
- Failed jobs should set `generationError` from backend error/detail and stop polling.
- Completed jobs reload the folder deck through `slideService.getDeckByFolder(workspaceId)`.
- Backend and frontend builds must pass before the patch is considered complete.

## Runtime verification status

- Static code audit: completed.
- Build verification: `node --check client/src/components/FolderStudio.js` passed; `dotnet build ELearnGamePlatform.sln` was blocked by a running `ELearnGamePlatform.API` process locking API bin DLLs; `dotnet build src\ELearnGamePlatform.API\ELearnGamePlatform.API.csproj --no-restore -p:OutputPath=H:\pbl5\.tmp\codex-api-output\` passed; `cd client && BUILD_PATH=build-codex-slidegen-audit npm run build` passed.
- Live browser/network trace: not yet run. Use the new frontend console debug and backend logs to prove duplicate request behavior during the next live run.
