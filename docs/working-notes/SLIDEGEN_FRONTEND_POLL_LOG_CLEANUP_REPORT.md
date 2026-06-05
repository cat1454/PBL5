# SlideGen Frontend Poll Log Cleanup Report

## Changed files

| File | Change | Risk |
|---|---|---|
| `client/src/components/FolderStudio.js` | Added a progress debug signature and gated only `[SlideGen]` `poll-progress` logs when job/status/percent/stage/message/detail/current/total/speedMode are unchanged. | Low: polling requests, state updates, terminal handling, duplicate-start guard, UI copy, layout, API, backend, and schema are unchanged. |
| `docs/working-notes/SLIDEGEN_FRONTEND_POLL_LOG_CLEANUP_REPORT.md` | Documented the frontend poll log cleanup. | Low: docs only. |
| `.local-agent-rules/CHANGELOG.md` | Added local changelog entry for the cleanup. | Low: local-only documentation. |

## Behavior before

Workspace Slide Generation logged `[SlideGen]` `poll-progress` on every polling tick, even when the normalized progress payload had the same job/status/percent/stage/message/detail/current/total values as the previous response.

This made normal polling look noisy in DevTools and could make a healthy in-progress job appear stuck in a loop.

## Behavior after

Polling still runs on the same interval and still updates frontend progress state on every response.

Only `poll-progress` debug logging is deduplicated. A new `poll-progress` log appears when any tracked field changes:

- `jobId`
- `status`
- `percent`
- `stage`
- `message`
- `detail`
- `current`
- `total`
- `speedMode`

Non-progress lifecycle logs are unchanged: generate click payload, job receipt, poll-start, poll-stop, completed, failed, superseded/cancelled, and job-not-found terminal handling still log through their existing paths.

## Acceptance tests

- Network vẫn poll đều: polling interval and `slideService.getGenerateProgress(jobId)` call path unchanged.
- Console không spam khi progress không đổi: `poll-progress` is skipped when the tracked signature matches the previous response.
- Khi percent/stage/status đổi vẫn log: signature includes `percent`, `stage`, and `status`.
- Terminal completed/failed/superseded vẫn log: terminal status changes are included in the signature, and existing `poll-stop` lifecycle logging remains unchanged.
- Frontend build pass: `cd H:\pbl5\client && npm run build`.
