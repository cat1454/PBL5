# Frontend Handoff

## 1. Goal

Frontend should now focus on the main demo flow:

1. Upload document
2. Show document progress clearly
3. Generate questions
4. Play Quiz / Flashcards
5. Generate and edit slides

The current roadmap still puts UI/UX first.

## 2. Backend contract to use

Progress payload is now standardized across all 3 flows.

Common fields you can rely on:

- `status`
- `stage`
- `stageLabel`
- `message`
- `detail`
- `elapsedSeconds`
- `estimatedRemainingSeconds`
- `error`

Useful optional fields:

- `percent`
- `current`
- `total`
- `unitLabel`
- `stageIndex`
- `stageCount`
- `questionsGenerated`
- `slidesGenerated`

## 3. Endpoints to wire

### Document

- `POST /api/documents/upload`
- `GET /api/documents/{id}`
- `GET /api/documents/{id}/progress`
- `GET /api/documents/user/{userId}`

### Question

- `POST /api/questions/generate/start`
- `GET /api/questions/generate/progress/{jobId}`
- `GET /api/questions/document/{documentId}`

### Slide

- `POST /api/slides/generate/start`
- `GET /api/slides/generate/progress/{jobId}`
- `GET /api/slides/document/{documentId}`
- `PUT /api/slides/{deckId}/items/{itemId}`

## 4. Frontend next actions

1. Update `client/src/services/api.js`

- Add `documentService.getDocumentProgress(documentId)`
- Keep question/slide polling logic aligned with the new progress shape

2. Update `client/src/components/DocumentList.js`

- Use one progress renderer for document, question, and slide cards
- Surface `stageLabel`, `message`, `detail`, and `error`
- Prefer the document progress endpoint instead of guessing status from document entity only

3. Keep `client/src/components/SlideStudio.js` aligned with the same progress UI

- Reuse the same status language
- Show ETA only when available
- Show completed/failed states clearly

4. Do not invent frontend-only status names

- Render backend `status` and `stage` directly
- If you need a display label, map from `stageLabel` first

## 5. Good UI priority order

1. Dashboard / document list
2. Unified progress card
3. Quiz / flashcard polish
4. Slide Studio polish

## 6. Notes

- `demo-user` is still expected in the current frontend flow
- Backend model/config changes are already in place
- If a field is missing or unclear, sync with backend before adding frontend assumptions
