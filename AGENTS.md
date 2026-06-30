# AGENTS.md

Instructions for coding agents working in this repository.

## Quick Reference Commands

```bash
# Frontend (Next.js)
pnpm web:dev          # dev server
pnpm web:build        # production build
pnpm web:test         # Vitest
pnpm web:lint         # ESLint

# Backend (.NET)
pnpm backend:test     # run all .NET test projects
dotnet test tests/Transcendence.Service.Core.Tests   # single project
dotnet test tests/Transcendence.WebAPI.Tests          # single project

# API client generation
pnpm api:gen          # generate TS client from OpenAPI spec
pnpm api:check        # verify spec is in sync

# Docker
docker compose up --build      # full stack (API + worker + web + Postgres + Redis)

# E2E
pnpm e2e:stack        # start stack for E2E tests
pnpm e2e:local        # run Playwright E2E tests locally

# Git hooks
pnpm hooks:install    # configure git core.hooksPath to .githooks

# EF Migrations (run from repo root)
dotnet ef migrations add <Name> --project Transcendence.Service --startup-project Transcendence.Service
dotnet ef migrations remove    --project Transcendence.Service --startup-project Transcendence.Service
dotnet ef database update      --project Transcendence.Service --startup-project Transcendence.Service
```

## Architecture Overview

Monorepo with a **.NET backend** (WebAPI + Hangfire Worker) and a **Next.js frontend** (`apps/web`), plus a generated TypeScript API client (`packages/api-client`).

**Key projects:**

| Project | Role |
|---|---|
| `Transcendence.WebAPI` | HTTP API — serves reads, enqueues background jobs |
| `Transcendence.Service` | Hangfire worker — calls Riot API, writes data |
| `Transcendence.Service.Core` | Shared domain logic, DTOs, interfaces |
| `Transcendence.Data` | EF Core DbContext, entities, migrations |

**Patterns:**

- **BFF proxy** — Next.js proxies API requests to the backend; auth tokens live in HttpOnly cookies.
- **Summoner refresh flow** — client gets `202 Accepted` → backend enqueues refresh job → worker fetches from Riot API → client polls until `200 OK`.
- **Tech stack** — PostgreSQL 16, Redis 7, Hangfire (job processing), HybridCache (L1 in-memory + L2 Redis).

For deeper context see `docs/ARCHITECTURE.md`, `docs/DEVELOPMENT.md`, and `docs/API.md`.

## Canonical Docs (Keep These Correct)

- `README.md`
- `docs/DEVELOPMENT.md`
- `docs/API.md`
- `docs/ARCHITECTURE.md`
- `AGENTS.md`

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
- Live site: `https://transcend.kronic.one`
- Live API: `https://api.kronic.one`
- Local dev server: `http://localhost:3000` (reflects local changes, use this for testing)
- The live site updates once changes are merged into `main` and pushed to GitHub
- This avoids needing a local backend running

Test summoner for verification: `Kronic#NA1` (region: NA)
- LoL profile: `https://transcend.kronic.one/lol/summoners/na/Kronic-NA1`

## EF Migration Policy (Required)

- Never hand-author or hand-edit EF migration files.
- Always create/remove migrations via EF CLI (`dotnet ef migrations add ...`, `dotnet ef migrations remove`).
- Always apply schema changes by updating EF model code first, then generating migrations with EF tools.
