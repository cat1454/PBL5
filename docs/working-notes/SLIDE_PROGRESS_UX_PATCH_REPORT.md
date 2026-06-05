# Slide Progress UX Patch Report

## Changed files

| File | Change | Risk |
|---|---|---|
| `client/src/services/progress.js` | Added elapsed-time formatting and ETA confidence logic for active progress payloads. | Low; helper-only change, no endpoint or schema impact. |
| `client/src/components/FolderStudio.js` | Updated Workspace deck progress card and topbar text to show phase, section counter, elapsed time, and low-confidence ETA copy. | Low; render-only change, polling/job/reload logic unchanged. |

## Behavior before

Workspace slide generation displayed backend ETA directly. During early `section-summaries`, a slow local Ollama call could produce a large ETA such as `122m`, even though the job was still progressing through sections.

## Behavior after

Workspace slide generation keeps the backend percent bar, but early low-confidence phases now emphasize the current phase, section counter, elapsed time, and safe estimating copy. A `section-summaries` payload such as section 5 of 10 now reads close to:

- `Dang tom tat noi dung`
- `Section 5/10`
- `Da chay: 2 phut 14 giay`
- `Dang uoc tinh thoi gian con lai...`

Later reliable phases can still show normal ETA.

## ETA rules

ETA is hidden or replaced with `Dang uoc tinh thoi gian con lai...` when:

- Progress is active and `stage` is `section-summaries`.
- Progress is active and `percent < 25`.
- `estimatedRemainingSeconds` is missing or invalid.
- Progress is active, `percent < 35`, and ETA is larger than 45 minutes.

ETA is shown normally when it is considered reliable, for example `generating-slides` at a later percent with a finite ETA.

## Acceptance tests

| Case | Result |
|---|---|
| `section-summaries`, `percent=17`, `current=5`, `total=10`, `estimatedRemainingSeconds=7320` | Does not render `122m`; shows section counter, elapsed time if available, and estimating copy. |
| `generating-slides`, `percent=70`, reasonable ETA | Keeps normal ETA display. |
| Completed deck generation | Reload behavior unchanged; no polling/reload code was modified. |
| Failed deck generation | Backend error handling unchanged; no failure-path code was modified. |
| Job-not-found terminal handling | Stop-polling behavior unchanged; no polling code was modified. |
| Frontend build | Passed. |

## Commands run

| Command | Result |
|---|---|
| `node --check client/src/components/FolderStudio.js` | Passed. |
| `node --check client/src/services/progress.js` | Passed. |
| `cd H:\pbl5\client; npm run build` | Passed; React production build compiled successfully. |

