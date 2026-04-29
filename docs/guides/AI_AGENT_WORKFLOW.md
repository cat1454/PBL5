# AI Agent Workflow

This repo uses a selective AI-agent workflow.

Do not treat external agent repos as drop-in dependencies for the application itself. They are workflow references, not runtime features.

## Default Position

- Keep the current repo-local Codex setup as the default:
  - `AGENTS.md`
  - `PLANS.md`
  - `.codex/config.toml`
  - module-local `AGENTS.override.md` files when needed
- Prefer small, explicit repo-local guidance over importing a large external harness.
- Avoid duplicate hooks, MCP definitions, or plugin settings unless there is a clear repo-specific benefit.

## Patterns Worth Reusing

- Planning for ambiguous or cross-surface tasks before coding.
- Review-first thinking for bug risk, contract drift, and missing verification.
- Debugging by tracing the real runtime path instead of trusting stale docs.
- Documentation lookup only when code or official sources are insufficient.
- Verification discipline: inspect, change narrowly, then run the smallest relevant build or runtime check.

## Repo-Specific Usage

### Backend and contracts

- Start from `src/ELearnGamePlatform.API/Program.cs`, controllers, and bound settings.
- If payloads change, inspect `client/src/services/api.js` and the affected screens in the same task.
- If persistence changes, inspect `ApplicationDbContext` and the relevant migration history before assuming schema shape.

### Frontend

- Keep the bilingual UI rule mandatory for user-facing changes.
- Reuse service helpers instead of introducing ad hoc API calls in components.
- Treat Slide Studio, document list previews, and slide HTML preview as a connected experience.

### AI, OCR, and slide generation

- Inspect prompt/config surfaces together with generation and verification code.
- Check whether a change affects status polling, confidence handling, or auto-repair expectations.
- Prefer guardrails and observability improvements over broad prompt churn when the current MVP behavior is already wired through multiple layers.

## What Not To Do

- Do not full-install a large external harness into this repo just because it supports Codex.
- Do not overwrite `.codex/config.toml` with external defaults without a deliberate diff review.
- Do not add repo-tracked hooks or MCP config copies that duplicate existing local behavior.
- Do not let workflow tooling changes silently alter how contributors build, review, or verify the MVP.

## Practical Adoption Checklist

- Add or update repo-local guidance first.
- Borrow only the smallest external pattern that solves a repeated workflow problem.
- Keep workflow changes in normal code review scope with clear before/after impact.
- Prefer documentation or local override files over global tooling changes when the need is repo-specific.
