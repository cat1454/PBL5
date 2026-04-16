# AGENTS.override.md

Use this file for tasks primarily inside `src/ELearnGamePlatform.API`.

## Module Focus

- This project is the API entrypoint and runtime composition root.
- Treat `Program.cs`, controllers, configuration binding, and startup behavior as the source of truth for runtime behavior.
- Changes here often affect `client/src/services/api.js` and frontend polling/edit flows.

## Key Files

- `Program.cs`: DI registration, CORS, startup flow, migrations, runtime URL.
- `Controllers/`: API contracts and request/response behavior.
- `Configuration/`: strongly typed settings classes.
- `appsettings.json` and `appsettings.Development.json`: runtime defaults and local overrides.

## Local Rules

- Preserve current API shape unless the task explicitly allows breaking contract changes.
- If you change request or response payloads, inspect the matching frontend service and screen.
- If you add config, bind it in `Program.cs` and provide safe defaults.
- If you change persistence-facing behavior, inspect EF Core migrations and schema assumptions before coding.
- Keep controller actions thin; prefer pushing business logic into Services or Infrastructure layers when possible.

## Verify For API Tasks

- `dotnet build ELearnGamePlatform.sln`
- If startup behavior changed: `dotnet run --project src/ELearnGamePlatform.API`
- If entity/schema shape changed: inspect migrations and note whether a new migration is required.

## Do Not Assume

- Do not assume docs are current if they disagree with `Program.cs` or `appsettings*.json`.
- Do not assume auth exists beyond the current demo flow.
- Do not assume background job state is durable; several flows are still in-memory.
