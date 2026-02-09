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

