# Transcendence (Backend + Web)

League of Legends analytics platform (backend + website). This repository contains:

- Web API (`Transcendence.WebAPI`)
- Background worker (`Transcendence.Service` + Hangfire)
- Core domain/services (`Transcendence.Service.Core`)
- Data layer (`Transcendence.Data`)
- Admin portal for Hangfire (`Transcendence.WebAdminPortal`)
- Next.js web frontend (`apps/web`)

## Documentation Map (Source of Truth)

- Development setup and runbooks: `docs/DEVELOPMENT.md`
- API overview and contract workflow: `docs/API.md`
- Architecture overview: `docs/ARCHITECTURE.md`
- Agent instructions: `CLAUDE.md` and `AGENTS.md`

## Documentation Policy (Contributor Requirement)

If a change affects behavior, API contracts, or configuration, update documentation in the same PR.

Examples:
- API/auth/env changes: update `docs/API.md` and/or `docs/DEVELOPMENT.md`
- Architectural or data-flow changes: update `docs/ARCHITECTURE.md`
- Command changes (pnpm/dotnet/docker): update `docs/DEVELOPMENT.md`

## Tech Stack

- ASP.NET Core Web API (`net10.0`)
- .NET Worker Service (`net10.0`)
- EF Core + PostgreSQL
- Hangfire + Hangfire.PostgreSql
- Redis + HybridCache
- Swagger/OpenAPI (spec committed under `openapi/`)
- Docker + Docker Compose
- Next.js (App Router) + Tailwind CSS
- pnpm workspaces
- OpenAPI TypeScript client generation (`openapi-typescript` + `openapi-fetch`)

## Repo Structure

| Project | Purpose |
|---|---|
| `Transcendence.WebAPI` | Public and authenticated REST API |
| `Transcendence.Service` | Background processing host (Hangfire server + recurring jobs) |
| `Transcendence.Service.Core` | Domain/application services (analytics, auth, live game, Riot integration, jobs) |
| `Transcendence.Data` | EF Core DbContext, entities, repositories |
| `Transcendence.WebAdminPortal` | Hangfire dashboard host |
| `apps/web` | Next.js website (SSR + route handlers BFF) |
| `packages/api-client` | Generated OpenAPI TypeScript client (`openapi/transcendence.v1.json` -> `src/schema.ts`) |
| `openapi` | Committed OpenAPI spec export |
| `scripts/openapi` | Spec export script |

## Local Development (Quick Start)

```bash
docker compose up --build
corepack pnpm install
corepack pnpm web:dev
```

For full setup instructions (env vars, secrets, migrations, keys), see `docs/DEVELOPMENT.md`.

## OpenAPI Spec + Client Generation

```bash
corepack pnpm api:gen
corepack pnpm api:check
```

See `docs/API.md` for details.
