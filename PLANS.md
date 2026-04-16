# PLANS.md

Use this file only for tasks that are ambiguous, risky, or span multiple steps. Keep plans short and execution-oriented.

## When To Write A Plan

- The request touches both backend and frontend.
- The task changes API contracts, database schema, or AI/OCR pipeline behavior.
- The task is unclear enough that coding immediately would risk rework.
- The task may require migrations, config changes, or a staged rollout.

## Plan Template

```md
# Task
Short description of the requested change.

## Goal
- What outcome should exist when the task is done?

## Scope
- What files/modules are expected to change?
- What is explicitly out of scope?

## Constraints
- Runtime constraints, compatibility needs, or safety limits.
- Existing user changes that must not be overwritten.

## Steps
1. Inspect the current implementation and identify the real source of truth.
2. Make the smallest change that satisfies the task.
3. Update any coupled surfaces such as API clients, config, or docs.
4. Verify using the smallest relevant build/test flow.

## Risks
- Behavior regressions
- Contract mismatches
- Config or environment drift

## Verification
- `dotnet build ELearnGamePlatform.sln`
- `dotnet run --project src/ELearnGamePlatform.API`
- `cd client && npm run build`

## Notes
- Record assumptions or follow-up work only if they matter to the current task.
```

## Repo-Specific Reminders

- Prefer code over docs when they disagree.
- Backend runtime source of truth is `src/ELearnGamePlatform.API/Program.cs` and `appsettings*.json`.
- Frontend API coupling usually flows through `client/src/services/api.js`.
- If a task changes persisted models, inspect EF Core migrations and `ApplicationDbContext`.
- If a task touches slide/image generation, inspect both backend pipeline config and Slide Studio UI.
