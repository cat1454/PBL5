# SlideGen Reference Repo Audit

## A. PBL5 current bottlenecks

| Bottleneck | File/function | Evidence | Impact | Fix size |
|---|---|---|---|---|
| Section summaries run sequentially | `src/ELearnGamePlatform.Services/AI/SlideGeneratorService.cs` / `GenerateSectionPlansAsync` | `BuildSectionPlans(chunks)` then a `for` loop awaits `GenerateStructuredResponseAsync<SlideSectionSummaryDraft>` once per section; progress is reported after each section as `MapProgress(12, 22, index + 1, plans.Count)`. | A 10-section scope does 10 local-model calls before outline generation can move past the low-percent stage. This matches the observed "section 5/10" stall. | Medium |
| ETA is global percent math, not phase-aware | `src/ELearnGamePlatform.API/Controllers/SlidesController.cs` / `UpdateEta` | ETA uses `elapsed / (percent / 100)` after percent is above 3. Section summaries occupy only 12-22%, so slow early calls project very large remaining times. | UX can look frozen or absurd even when work is progressing. | Small |
| Progress percent has coarse phase bands | `SlidesController.RunGenerateSlidesJobAsync`, `SlideGeneratorService.GenerateSectionPlansAsync` | Controller sets validation 3%, outline/summary via generator progress, placeholder 24%, slide content/image 26-90/30-90, completed 100%. | Progress can spend long time in 12-22% and then jump, which makes local Ollama latency feel worse. | Small |
| Folder run loses primary source document id in generation context | `SlidesController.ResolveGenerationContextAsync`, `SlideGenerationContext.FromFolder` | Folder path resolves `primarySource`, but returns `FromFolder(folder.Id, ...)`; `SlideGenerationContext` only stores `FolderProjectId`, not the selected source `DocumentId`. `SlideDeck.DocumentId` is nullable and folder decks are saved through `ReplaceForFolderAsync`. | Logs/progress/deck payloads can show `DocumentId = null`, making debugging workspace/source mismatches harder. UI reload by folder id still works. | Small |
| No section-summary cache | `SlideGeneratorService.GenerateSectionPlansAsync`; entities/repos | PBL5 persists `Document.Summary`, `CoverageMapJson`, `SlideDeck.OutlineJson`, and `SlideItem` data, but there is no persisted or in-memory cache keyed by section text/hash + model/profile + prompt/version. | Re-generating the same selected scope repeats expensive section summary calls. | Medium |
| Fast mode is a content mode, not a speed mode | `FolderStudio.js` deck mode controls; `SlidesController.IsSupportedGenerationMode`; `SlideGeneratorService.BuildBriefBlock` | Modes are `lecture`, `summary`, `exam-review`, `timeline`; service prompt changes emphasis, but the same section-summary, outline, per-slide, verifier/repair, image flow still runs. | Selecting summary does not materially reduce orchestration cost. | Medium |
| No bounded parallelism for AI sections/slides | `GenerateSectionPlansAsync`; `RunGenerateSlidesJobAsync` slide loop | Section summaries and slide generation use sequential loops. No `SemaphoreSlim`, `Parallel.ForEachAsync`, or bounded task batch around local-model calls. | Leaves potential latency wins unused, but unbounded parallelism would be risky for Ollama. | Medium |
| Cancellation is UI cleanup only, not job cancellation | `FolderStudio.js` polling effect; `SlidesController.RunGenerateSlidesJobAsync` | React effect sets `cancelled` and stops polling, but backend launches `Task.Run` without a job cancellation token or cancel endpoint. | Navigating away stops UI polling but does not stop the local model job. | Medium |
| Duplicate start is guarded in current patch | `FolderStudio.handleGenerateDeck` | `isStartingGenerationRef` blocks repeated click while POST is in flight; disabled state still handles normal UI. | This mitigates duplicate job creation from one UI instance. | Done |
| Retry exists, but not for the section-summary bottleneck | `SlideGeneratorService` | `SlideRetryLimit = 1` is used around outline/slide generation and repair paths; section summary catches failures and falls back to heuristic plan fields. | Good resilience later in the pipeline, but not a speed fix and not a cache. | Small |
| `client/src/components/SlideStudio.js` is absent in this checkout | `git status`, `rg --files client/src` | File is deleted in the current worktree; `/workspaces/:workspaceId` is served by `FolderStudio`, and shared slide components remain under `client/src/components/slide-studio/*`. | Audit/fixes must avoid assuming a separate document SlideStudio owner for this current surface. | N/A |

## B. Reference patterns worth adopting

| Repo | Pattern | Where found | Why useful for PBL5 | Safe to adopt now? |
|---|---|---|---|---|
| Presenton | Async task state with status/message/data/error | `H:\refs\presenton\servers\fastapi\models\sql\async_presentation_generation_status.py`; `api/v1/ppt/endpoints/presentation.py` `/generate/async` and `/status/{id}` | PBL5 already has in-memory job state; Presenton confirms the value of clear stage messages and terminal error payloads. Persisting full job state would be larger, but clearer message/error fields are safe. | Yes, message UX only |
| Presenton | Break presentation generation into named phases | `presentation.py`: outline, structure, generating slides, fetching assets, saving, exporting | PBL5 has similar phases but progress bands are coarse. Naming phase steps in UI can reduce "stuck" perception without changing contracts. | Yes |
| Presenton | Batched concurrent slide generation plus overlapping asset fetch | `presentation.py` around `batch_size = 10`, `asyncio.gather`, and `asyncio.create_task(process_slide_and_fetch_assets(...))` | Shows a proven pattern for parallelizing independent slide work while still batching. For PBL5/Ollama, adopt only as bounded parallelism later, not batch size 10. | Later, with cap 2 |
| Presenton | Reuse old assets when prompts/queries are unchanged | `utils/process_slides.py` / `process_old_and_new_slides_and_fetch_assets` | PBL5 image refresh could avoid refetching same image candidates. Related but not the current section-summary bottleneck. | Later |
| Kernel Memory | Cache decorator around expensive model operation | `src/Core/Embeddings/CachedEmbeddingGenerator.cs` | A small `ISlideSectionSummaryCache` decorator-like helper could check summary cache before calling Ollama and store misses after success. | Yes, but as second/third patch |
| Kernel Memory | Cache keys include provider/model/options/content hash, not raw text | `src/Core/Embeddings/Cache/EmbeddingCacheKey.cs` | PBL5 can hash section excerpt + source id/updated time + model profile + prompt version to avoid storing raw prompt text as a key. | Yes |
| Kernel Memory | Two-phase queued write with best-effort superseded-operation cancellation | `src/Core/Storage/ContentStorageService.cs` | Useful idea for future cancel/debounce: mark older jobs superseded rather than relying only on frontend. Too large for first patch. | Later |
| Kernel Memory | Retry policy with per-attempt timeout and transient-status filtering | `src/Core/Http/HttpRetryPolicy.cs` | PBL5 Ollama calls would benefit from explicit timeouts/retry metadata, but adding this to Ollama service is broader than current slide patch. | Later |
| Open WebUI | Local-model connection/model status APIs and explicit errors | `src/lib/apis/ollama/index.ts` / `verifyOllamaConnection`, `getOllamaVersion`, `getOllamaModels`; `Messages/Error.svelte` | PBL5 can make slide progress/errors mention local model availability or long-running Ollama, instead of generic "failed/poll failed". | Yes, UI/error copy only |
| Open WebUI | Status history items with shimmer while action is running | `src/lib/components/chat/Messages/ResponseMessage/StatusHistory/StatusItem.svelte` | PBL5 can show current phase/counter confidently and de-emphasize brittle ETA during active AI phases. | Yes |
| Open WebUI | Streaming parser treats errors, sources, selected model, usage as separate events | `src/lib/apis/streaming/index.ts` | PBL5 polling payload already has fields; pattern suggests keeping stage/detail/error separated rather than packing all text into message. | Yes, no schema change needed |
| LlamaIndex | Transformation cache keyed by content + transformation config hash | `llama-index-core/llama_index/core/ingestion/pipeline.py` / `get_transformation_hash`, `run_transformations` | Directly maps to section summary cache: cache the output of "summarize this section with prompt version X" for unchanged chunks. | Yes, with small local implementation |
| LlamaIndex | Persistable ingestion cache abstraction | `llama-index-core/llama_index/core/ingestion/cache.py` | PBL5 can start with an in-memory cache or existing JSON field, then later move to persisted cache if repeated jobs matter. | Maybe |
| LlamaIndex | Docstore hash skip for unchanged documents | `docs/.../ingestion_pipeline/index.md`; `pipeline.py` `_handle_upserts` | Good principle for skipping old processing. For PBL5, source `UpdatedAt`/coverage hash can gate cached summary reuse. | Later |
| LlamaIndex | Parallel workers are explicit and bounded by `num_workers` | `pipeline.py` `num_workers` branches | Supports a conservative PBL5 design: bounded section summaries with `MaxDegreeOfParallelism = 2`, not unbounded tasks. | Later |

## C. Patterns not suitable now

| Repo | Pattern | Why not now |
|---|---|---|
| Presenton | Full async task persistence table for presentation generation | PBL5 already has job store and endpoint contract. Adding a table/migration would exceed the requested 1-3 small patches. |
| Presenton | Batch size 10 concurrent slide LLM calls | PBL5 uses local Ollama; high concurrency can overload the model and make latency worse. If adopted, cap at 2 and measure. |
| Presenton | Full template/layout engine and export pipeline | PBL5 already has slide editor, import image, actions, export, autosave, and canvas components. Replacing this would violate scope. |
| Kernel Memory | General content operation queue with superseded operation recovery | Useful architecture, but too broad for slide generation and would require persisted operation records. |
| Kernel Memory | SQLite cache backend | PBL5 source of truth is PostgreSQL + EF Core. Adding SQLite would be another runtime dependency and storage surface. |
| Kernel Memory | Embedding-specific cache metadata | The provider/model/hash idea is useful; vector dimensions/normalization are not relevant to section summaries. |
| Open WebUI | SSE/chat streaming UX for slide generation | PBL5 already polls `/slides/generate/progress/{jobId}`. Switching to SSE would change endpoint behavior and frontend flow. |
| Open WebUI | Model download/pull management UI | PBL5 needs generation UX, not admin model management. |
| LlamaIndex | Full ingestion pipeline abstraction | PBL5 already has document processing and slide generation services. Importing a generic transformation pipeline would be a refactor. |
| LlamaIndex | Multiprocessing/process-pool parallel execution | Not appropriate for ASP.NET request/job service; use `SemaphoreSlim`/bounded tasks if parallelizing. |

## D. Recommended patch order

1. ETA/progress UX patch
   - Muc tieu: hide or soften ETA during low-confidence AI phases (`section-summaries`, early `generating-slides`) and emphasize stage/counter/detail instead.
   - File can sua: `client/src/components/FolderStudio.js`, maybe `client/src/services/progress.js`.
   - Rui ro: low; UI-only and no endpoint/schema change.
   - Acceptance test: mocked/real progress with `stage=section-summaries`, `current=5`, `total=10`, large `estimatedRemainingSeconds` shows counter/stage and "dang uoc tinh" style copy instead of a huge ETA; completed/failed still displays terminal state.
   - Co can chay AI that khong: no.

2. Folder primary source id observability patch
   - Muc tieu: keep primary source/document id in logs and progress fallback for folder runs without changing DB schema or endpoint shape.
   - File can sua: `src/ELearnGamePlatform.API/Controllers/SlidesController.cs`.
   - Rui ro: low-medium; must avoid changing folder deck ownership semantics.
   - Acceptance test: folder run logs include `FolderId` and `PrimarySourceDocumentId`; progress payload still reloads by folder id; existing folder deck `DocumentId` behavior is unchanged unless explicitly chosen.
   - Co can chay AI that khong: no; can validate with controller/unit-level static build.

3. Section summary cache sketch implementation
   - Muc tieu: avoid repeating section-summary Ollama calls for unchanged section excerpt + mode/prompt/model profile.
   - File can sua: `SlideGeneratorService.cs`; possibly a small service/helper in `src/ELearnGamePlatform.Services/AI`; no schema in first version if using bounded in-memory cache.
   - Rui ro: medium; cache key must include enough inputs to avoid stale summaries.
   - Acceptance test: two same-process runs over the same selected sections log cache misses first, hits second; changed excerpt or prompt version misses.
   - Co can chay AI that khong: yes for final proof, but unit tests can cover keying without Ollama.

4. Real Fast mode
   - Muc tieu: add a speed-oriented branch that skips per-section AI summaries and uses existing coverage-map summaries/key facts directly, while preserving grounded outline generation.
   - File can sua: `FolderStudio.js`, `SlidesController.cs`, `SlideGeneratorService.cs`.
   - Rui ro: medium; must not overload existing `summary` mode semantics unless copy clearly says "Fast".
   - Acceptance test: Fast run over 10 sections emits zero `section-ai-completed` logs, still creates outline/placeholders/slides, and deck quality is acceptable for preview/export.
   - Co can chay AI that khong: yes.

5. Bounded section-summary parallelism
   - Muc tieu: summarize independent sections with a small max concurrency, probably 2 for Ollama.
   - File can sua: `SlideGeneratorService.cs`.
   - Rui ro: medium-high; local model contention, ordering, progress updates, and error handling must be controlled.
   - Acceptance test: 10 sections produce the same ordered `SlideSectionPlan` list; no more than 2 active AI calls; progress still reaches 22%; local Ollama remains responsive.
   - Co can chay AI that khong: yes.

6. Backend cancellation/supersede
   - Muc tieu: allow explicit cancel/supersede of older slide jobs for the same folder/document.
   - File can sua: `SlidesController.cs`, job store class, `FolderStudio.js`.
   - Rui ro: medium-high; needs careful job state, cancellation token, and UI terminal handling.
   - Acceptance test: starting/cancelling a job transitions progress to cancelled/failed-like terminal state, stops polling, and does not save a partial deck as completed.
   - Co can chay AI that khong: useful but can be simulated with a fake slow service.

## E. First patch proposal

First patch nen lam ngay: ETA/progress UX patch.

Ly do: day la patch nho nhat, rui ro thap nhat, va giai quyet cam giac "treo" ngay ca khi backend van dang xu ly dung. Reference phu hop nhat la Open WebUI status-history pattern: khi local model dang lam viec lau, UI nen hien hanh dong hien tai, counter, shimmer/active state, va loi ro rang; khong nen dua nguoi dung vao mot ETA phut qua lon duoc tinh tu percent som. PBL5 da co `stage`, `stageLabel`, `message`, `detail`, `current`, `total`, `estimatedRemainingSeconds`, nen khong can doi endpoint/schema.

De xuat chi tiet:

- Trong `client/src/services/progress.js`, them helper nho de xac dinh ETA co dang tin hay khong, vi du low-confidence neu stage la `section-summaries` hoac percent duoi 25 va status dang active.
- Trong `WorkspaceDeckProgressCard` va topbar progress cua `FolderStudio.js`, neu ETA low-confidence thi hien copy dang uoc tinh/counter thay vi `122m`.
- Giu nguyen percent bar, poll interval, completion reload, error handling, export/editor/autosave/presentation/image flows.
- Khong can backend change.

Acceptance test:

- Tao/mock progress `stage=section-summaries`, `percent=17`, `current=5`, `total=10`, `estimatedRemainingSeconds=7320`; UI khong hien `122m`, ma hien phase/counter va trang thai dang uoc tinh.
- Progress `stage=generating-slides`, `percent=70`, ETA hop ly van duoc hien.
- Progress failed/completed van dung copy terminal va polling stop nhu hien tai.

No code changed.

Files read:

- `AGENTS.md`
- `.agents/skills/testing-checklist/SKILL.md`
- `docs/working-notes/SLIDE_GENERATION_BOTTLENECK_REPORT.md`
- `client/src/components/FolderStudio.js`
- `client/src/components/slide-studio/SlideCanvas.js`
- `client/src/components/slide-studio/useSlideEditorAutosave.js`
- `client/src/components/slide-studio/useSlideEditorRealtime.js`
- `client/src/components/slide-studio/*` file list
- `client/src/services/api.js`
- `client/src/services/progress.js`
- `src/ELearnGamePlatform.API/Controllers/SlidesController.cs`
- `src/ELearnGamePlatform.Services/AI/SlideGeneratorService.cs`
- `src/ELearnGamePlatform.Core/Entities/SlideDeck.cs`
- `src/ELearnGamePlatform.Core/Entities/Document.cs`
- `src/ELearnGamePlatform.Core/Entities/FolderProject.cs`
- `src/ELearnGamePlatform.Core/Entities/SlideGenerationProgressUpdate.cs` via search
- `src/ELearnGamePlatform.Core/Entities/DocumentCoverageChunk.cs` via search
- `src/ELearnGamePlatform.Infrastructure/Data/ApplicationDbContext.cs` via search
- `src/ELearnGamePlatform.Infrastructure/Repositories/SlideDeckRepository.cs`
- `src/ELearnGamePlatform.Infrastructure/Repositories/DocumentRepository.cs` via search
- `src/ELearnGamePlatform.Infrastructure/Repositories/FolderProjectRepository.cs` via search
- `H:\refs\presenton\servers\fastapi\api\v1\ppt\endpoints\presentation.py`
- `H:\refs\presenton\servers\fastapi\models\sql\async_presentation_generation_status.py`
- `H:\refs\presenton\servers\fastapi\utils\process_slides.py`
- `H:\refs\presenton\servers\fastapi\utils\llm_calls\generate_presentation_outlines.py` via search
- `H:\refs\presenton\servers\fastapi\utils\llm_calls\generate_presentation_structure.py` via search
- `H:\refs\presenton\servers\fastapi\utils\llm_calls\generate_slide_content.py` via search
- `H:\refs\kernel-memory\src\Core\Embeddings\CachedEmbeddingGenerator.cs`
- `H:\refs\kernel-memory\src\Core\Embeddings\Cache\EmbeddingCacheKey.cs`
- `H:\refs\kernel-memory\src\Core\Embeddings\Cache\IEmbeddingCache.cs` via search
- `H:\refs\kernel-memory\src\Core\Embeddings\Cache\SqliteEmbeddingCache.cs` via search
- `H:\refs\kernel-memory\src\Core\Http\HttpRetryPolicy.cs`
- `H:\refs\kernel-memory\src\Core\Storage\ContentStorageService.cs`
- `H:\refs\open-webui\src\lib\apis\ollama\index.ts`
- `H:\refs\open-webui\src\lib\apis\streaming\index.ts`
- `H:\refs\open-webui\src\lib\components\chat\Messages\Error.svelte`
- `H:\refs\open-webui\src\lib\components\chat\Messages\ResponseMessage.svelte` via search
- `H:\refs\open-webui\src\lib\components\chat\Messages\ResponseMessage\StatusHistory\StatusItem.svelte`
- `H:\refs\open-webui\src\lib\components\admin\Settings\Models\Manage\ManageOllama.svelte` via search
- `H:\refs\llama_index\llama-index-core\llama_index\core\ingestion\pipeline.py`
- `H:\refs\llama_index\llama-index-core\llama_index\core\ingestion\cache.py`
- `H:\refs\llama_index\docs\src\content\docs\framework\module_guides\loading\ingestion_pipeline\index.md` via search
- `H:\refs\llama_index\docs\src\content\docs\framework\module_guides\loading\ingestion_pipeline\transformations.md` via search
