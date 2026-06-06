# SlideGen Reload Resume Job Report

## Changed files

| File | Change | Risk |
|---|---|---|
| `src/ELearnGamePlatform.API/Services/SlideGenerationJobStore.cs` | Added `TryGetLatestActiveJobForFolder` to expose the latest queued/running folder slide job from the existing in-memory indexes. | Low: read-only lookup; no schema, migration, package, model, or persisted shape change. |
| `src/ELearnGamePlatform.API/Controllers/SlidesController.cs` | Added `GET /api/slides/folders/{folderId}/generate/active` and resume logs; folder start still returns immediately after launching the background task. | Low: additive endpoint; existing start/progress/deck endpoints and generation flow are unchanged. |
| `client/src/services/api.js` | Added `slideService.getActiveGenerateJobForFolder(folderId)`. | Low: additive API client method. |
| `client/src/components/FolderStudio.js` | Workspace load now queries the active folder job and resumes polling it after reload. | Low: no auto-start path added; Generate still only runs from explicit user actions. |
| `docs/working-notes/SLIDEGEN_RELOAD_RESUME_JOB_REPORT.md` | Documented the reload resume fix. | Low: docs only. |
| `.local-agent-rules/CHANGELOG.md` | Added local changelog entry for the reload resume patch. | Low: local-only documentation. |

## Root cause

The audited Workspace slide generation path did not pass `HttpContext.RequestAborted` into the background slide generation job. The job is launched with `Task.Run`, resolves its own scoped services, and the slide generator calls do not accept a request cancellation token in the current contract.

The confirmed reload gap was frontend orchestration: `jobId` lived in React state after `POST /slides/folders/{folderId}/generate/start`. A browser reload cleared that state, so the page did not know which existing in-memory backend job to poll until a deck snapshot existed.

## New behavior

Reload page does not create a new `POST /slides/folders/{folderId}/generate/start`.

On Workspace load, the frontend now calls:

`GET /api/slides/folders/{folderId}/generate/active`

If the backend has a latest queued/running job for that folder, it returns the normal slide progress payload with `jobId`, `status`, `percent`, `stage`, `message`, `detail`, counters, and deck id when available. `FolderStudio` sets `jobId`, restores progress state, logs `[SlideGen] { action: 'resume-active-job', ... }` in development, and the existing polling effect continues with `GET /slides/generate/progress/{jobId}`.

If there is no active job, the endpoint returns `204 No Content`; the frontend falls back to deck `generationProgress` or clears terminal/no-progress state as before.

Terminal cleanup remains handled by the existing polling logic:

- `completed`: stop polling, clear generation error, reload workspace/deck, select newest slide.
- `failed`: stop polling, show the existing failure message, reload workspace silently.
- `cancelled`, `canceled`, `superseded`: stop polling and reload silently.
- `job-not-found`: reload workspace and treat missing/terminal progress as terminal.

## Compatibility

- Endpoint/schema: one additive API endpoint; no DB schema or migration change.
- Backend job lifecycle: no request cancellation token is passed into Workspace background generation.
- Fast Preview: preserved. This patch does not touch `fast-local-plans SkippedAi=true`, compact fast outline context, `fast-outline NumCtx=8192`, or `fast-slide NumCtx=16384`.
- Quality mode: unchanged.
- Editor/import image/elements/autosave/export/presentation: unchanged.

## Acceptance tests

- Start Fast Preview, reload page: backend job remains represented by the active job store entry and can be resumed by `GET /slides/folders/{folderId}/generate/active`.
- After reload, frontend polls the returned `jobId` via existing `slideService.getGenerateProgress(jobId)`.
- Reload does not call `POST /slides/folders/{folderId}/generate/start`; the patch adds only a GET during load.
- Completed after reload still reloads the deck through the existing completed terminal branch.
- Failed after reload still shows the existing failure message.
- Superseded/cancelled terminal statuses still stop polling through `isTerminalProgress`.
- Fast Preview logs/settings are untouched by this patch.
- Backend build pass: `dotnet build ELearnGamePlatform.sln`.
- Frontend build pass: `cd H:\pbl5\client && npm run build`.
- JS syntax checks pass: `node --check client/src/components/FolderStudio.js`; `node --check client/src/services/api.js`.
