# Transcendence

Transcendence is a monorepo for a League of Legends analytics stack:

- `Transcendence.WebAPI`: ASP.NET Core Web API
- `Transcendence.Service`: .NET worker running Hangfire jobs
- `apps/web`: Next.js App Router frontend (SSR + BFF route handlers)
- `Transcendence.Data` + `Transcendence.Service.Core`: data + domain/service layers
- `openapi/transcendence.v1.json`: committed API contract
- `packages/api-client`: generated TypeScript client/schema

## What Exists Today

### Backend

- Mixed auth policies in API: `AppOnly`, `UserOnly`, `AppOrUser`, `AdminOnly`
- Endpoint areas for:
  - summoner profile, refresh, and match history
  - champion analytics (tier list, win rates, builds, matchups, pro builds)
  - auth/session and API key management
  - admin operations (jobs, failed-job detail, service logs, cache invalidate, audit log, pro roster CRUD)
  - live game lookup and health checks
- Hangfire recurring and queued job execution for ingestion, refresh, analytics, and backfills

### Web (`apps/web`)

Current App Router pages include:

- Global command/search supports DB-backed summoner autosuggest
- `/`
- `/tierlist`
- `/champions` and `/champions/[championId]`
- `/matchups` and `/matchups/[championId]`
- `/pro-builds` and `/pro-builds/[championId]`
- `/summoners/[region]/[riotId]` (with legacy `/matches*` redirects)
- `/account/login`, `/account/register`, `/account/favorites`
- `/admin/*` pages for operational controls (requires admin role)

The web app proxies backend calls through BFF-style route handlers under `apps/web/app/api/*`.

## Tech Stack

- .NET 10 (`global.json` pins SDK `10.0.102`)
- ASP.NET Core + Hangfire + EF Core
- PostgreSQL 16 + Redis 7 (via Docker Compose for local dev)
- Next.js 16 + React 19 + Tailwind CSS
- pnpm workspace (`pnpm@10.22.0`)

## Repository Layout

| Path | Purpose |
|---|---|
| `Transcendence.WebAPI` | REST API host |
| `Transcendence.Service` | Worker host + Hangfire server |
| `Transcendence.Service.Core` | Domain/application services |
| `Transcendence.Data` | EF Core DbContext, entities, repositories |
| `apps/web` | Next.js frontend + BFF routes |
| `packages/api-client` | Generated TS API client artifacts |
| `openapi` | Committed OpenAPI spec |
| `docs` | Development/API/architecture docs |

## Quick Start (Docker + Local Web)

1. Start infrastructure and backend services:

```bash
docker compose up --build
```

2. Install JS dependencies:

```bash
corepack pnpm install
```

3. Configure the web app:

```bash
cp apps/web/.env.example apps/web/.env.local
```

At minimum set:

- `TRN_BACKEND_BASE_URL=http://localhost:8080`
- `TRN_BACKEND_API_KEY=trn_bootstrap_dev_key` (or another valid `AppOnly` key)
  - `/api/trn/app/*` is path-allowlisted and not a generic AppOnly passthrough.

Optional for local admin bootstrap:

- Set `ADMIN_BOOTSTRAP_EMAIL_0=<your-email>` before `docker compose up`
- Register/login that same email in the web app, then open `/admin`

4. Run the web app:

```bash
corepack pnpm web:dev
```

Local endpoints:

- Web: `http://localhost:3000`
- API: `http://localhost:8080`
- API health: `http://localhost:8080/health/live`, `http://localhost:8080/health/ready`
- Web admin UI: `http://localhost:3000/admin` (admin role required)
- Service log tooling (prod compose): Dozzle on `${DOZZLE_PORT}`
- pgAdmin: `http://localhost:5050`

## Common Commands

From repo root:

```bash
corepack pnpm backend:test
corepack pnpm web:dev
corepack pnpm web:test
corepack pnpm web:lint
corepack pnpm web:build
corepack pnpm api:gen
corepack pnpm api:check
```

## Auth Security Notes

- `POST /api/auth/logout` revokes the active refresh token server-side and is used by web logout.
- Auth endpoints use dedicated rate limits (`login`, `register`, `refresh`, `logout`).
- Web/API startup fails outside `Development` if `Auth:Jwt:Key` is missing or the known development placeholder is used.
- `Auth:BootstrapApiKeyEnabledInDevelopmentOnly=true` blocks bootstrap API key auth outside `Development`.

`api:gen` updates:

- `openapi/transcendence.v1.json`
- `packages/api-client/src/schema.ts`

## Documentation

- `docs/DEVELOPMENT.md`: local setup, env vars, run modes, job tuning
- `docs/API.md`: auth model, endpoint map, OpenAPI workflow
- `docs/ARCHITECTURE.md`: component boundaries, data flow, worker orchestration
- `docs/BACKEND_TASKS_FRONTEND_OVERHAUL.md`: tracked backend follow-ups
- `AGENTS.md`: repository-specific instructions for coding agents

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).
