# Architecture

Transcendence is a backend + web monorepo:

- A .NET Web API that serves reads and queues refresh jobs
- A .NET background worker that executes Hangfire jobs (refresh, ingestion, analytics, etc.)
- A Next.js web frontend that renders pages (SSR) and proxies to the backend via route handlers (BFF)

## Components

### `Transcendence.WebAPI`
- Public/authenticated REST API
- Enqueues background work (Hangfire) for expensive refresh operations
- Exposes OpenAPI/Swagger (spec is exported and committed under `openapi/`)

### `Transcendence.Service`
- Background host that runs Hangfire server and recurring jobs
- Executes ingestion/refresh/analytics workflows

### `Transcendence.Service.Core`
- Domain/application services (analysis, analytics compute, auth, live game, Riot API integration, jobs)
- Called from WebAPI controllers and the worker host

### `Transcendence.Data`
- EF Core DbContext + entities + repositories
- PostgreSQL is the intended runtime database

### `Transcendence.WebAdminPortal`
- Hangfire dashboard host (admin/ops)

### `apps/web` (Next.js)
- App Router pages + route handlers used as a BFF:
  - `/api/session/*` for browser auth/session interactions
  - `/api/trn/*` as proxy endpoints to backend (adds auth headers server-side)
- Tailwind styling, SSR-first pages where possible

### `packages/api-client`
- Generated OpenAPI TypeScript client artifacts
- Schema generation uses `openapi-typescript` + `openapi-fetch`

## Data Flow: Summoner Refresh

1. Client requests a summoner by Riot ID:
   - If data exists in DB: return immediately
   - If missing: return `202 Accepted` indicating refresh is needed
2. Client triggers refresh:
   - WebAPI acquires a refresh lock (prevents concurrent refreshes)
   - WebAPI enqueues Hangfire job
3. Worker performs refresh:
   - Calls Riot APIs
   - Upserts summoner/rank/match records
4. Client polls GET endpoint until data is ready (200 OK)

## Web Auth Boundary (BFF)

The web app never exposes backend tokens to browser JS:

- User tokens are stored as HttpOnly cookies in Next.js domain
- Next route handlers forward requests to backend with:
  - `Authorization: Bearer ...` (UserOnly) when needed
  - `X-API-Key` (AppOnly) when needed
- Backend never receives browser cookies (explicitly stripped in proxy)

## Caching

Backend uses a layered approach (see source and README):

- HybridCache (L1 in-memory + L2 Redis) for derived stats/analytics
- Persistent storage (PostgreSQL) for canonical match/summoner data

