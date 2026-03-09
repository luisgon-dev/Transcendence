# AGENTS.md

Instructions for coding agents working in this repository.

## Canonical Docs (Keep These Correct)

- `README.md`
- `docs/DEVELOPMENT.md`
- `docs/API.md`
- `docs/ARCHITECTURE.md`
- `CLAUDE.md`

## Required Documentation Hygiene

Any PR that changes one of the following must update docs in the same PR:

- API surface, auth requirements, request/response shapes, status codes
  - Update `docs/API.md`
  - Update the OpenAPI spec (`openapi/transcendence.v1.json`) when applicable
- Environment variables, secrets, docker compose, or run/build/test commands
  - Update `docs/DEVELOPMENT.md` and/or `README.md`
- System design, background job flows, caching strategy, BFF boundaries
  - Update `docs/ARCHITECTURE.md`

If you are not sure which doc to update, add a short note to the PR explaining what’s missing and why.

## Repo Notes

- Backend is .NET (SDK pinned in `global.json`)
- Web frontend lives in `apps/web` (Next.js App Router)
- OpenAPI spec is committed under `openapi/`
- TS client generation lives in `packages/api-client`

## Frontend Debugging with Playwright

Agents can use `playwright-cli` to take screenshots and interact with the live site for frontend debugging:

- Use `playwright-cli open https://kronic.one` to open the live site
- Use `playwright-cli screenshot` to capture current state
- Use `playwright-cli snapshot` to get a DOM snapshot with element refs

When working on **frontend-only changes** (no backend modifications), use the live API:
- Live site: `https://kronic.one`
- Live API: `https://api.kronic.one`
- This avoids needing a local backend running

Test summoner for verification: `Kronic#NA1` (region: NA)
- LoL profile: `https://kronic.one/lol/summoners/na/Kronic-NA1`
- TFT profile: `https://kronic.one/tft/summoners/na/Kronic-NA1`

## EF Migration Policy (Required)

- Never hand-author or hand-edit EF migration files.
- Always create/remove migrations via EF CLI (`dotnet ef migrations add ...`, `dotnet ef migrations remove`).
- Always apply schema changes by updating EF model code first, then generating migrations with EF tools.

