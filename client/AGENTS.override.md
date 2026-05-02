# AGENTS.override.md

Use this file for tasks primarily inside `client/`.

## Module Focus

- This project is a React 18 frontend built with `react-scripts`.
- Frontend API traffic is funneled through `src/services/api.js`.
- Dev proxy currently targets `http://127.0.0.1:5000` in `package.json`, so keep frontend/backend contract changes aligned.

## Key Files

- `src/services/api.js`: API base behavior and service methods.
- `src/components/`: main UI flows including document list, quiz/flashcards, and Slide Studio.
- `src/App.css`: shared styling; check for side effects before broad CSS edits.
- `package.json`: scripts, proxy, and frontend toolchain defaults.

## Local Rules

- Preserve the current MVP flow unless the task explicitly asks for redesign.
- When changing API usage, inspect the matching backend controller or payload shape first.
- Reuse existing service helpers in `src/services/api.js` instead of scattering fetch logic across components.
- Keep state updates and polling behavior easy to follow; this app already has several async document/question/slide flows.
- Avoid wide CSS changes unless you verify impacted screens such as upload, document list, and Slide Studio.

## Verify For Frontend Tasks

- `cd client && npm run build`
- If the task affects API coupling, also inspect `src/ELearnGamePlatform.API/Controllers` and `Program.cs`.

## Do Not Assume

- Do not assume auth or real user identity exists; the UI still uses `demo-user`.
- Do not assume backend responses are production-stable; several flows are still evolving.
- Do not change proxy, ports, or route behavior without checking both `package.json` and backend runtime settings.
