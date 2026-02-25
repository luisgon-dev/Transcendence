# Transcendence

Transcendence is a full-stack League of Legends analytics platform built as a portfolio project.

[![Portfolio Project](https://img.shields.io/badge/Project-Portfolio-1f6feb)](https://transcend.kronic.one)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Next.js](https://img.shields.io/badge/Next.js-App%20Router-000000?logo=nextdotjs&logoColor=white)](https://nextjs.org/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![Redis](https://img.shields.io/badge/Redis-7-DC382D?logo=redis&logoColor=white)](https://redis.io/)

Live site: [transcend.kronic.one](https://transcend.kronic.one)

It combines:
- A .NET API for stats, analytics, auth, and live game data
- A .NET background worker for ingestion and analytics computation
- A Next.js web app for tier lists, champion analytics, and summoner breakdowns

## Table of Contents

- [Why This Exists](#why-this-exists)
- [Current Features](#current-features)
- [Tech Stack](#tech-stack)
- [Architecture Overview](#architecture-overview)
- [Repository Structure](#repository-structure)
- [Quick Start (Docker Recommended)](#quick-start-docker-recommended)
- [API and Client Contract](#api-and-client-contract)
- [Documentation Map](#documentation-map)
- [License](#license)

## Why This Exists

This project is meant to demonstrate practical backend and full-stack engineering work:
- Domain modeling for real game data (matches, runes, builds, patch-aware analytics)
- Asynchronous processing with queues and recurring jobs
- API contract discipline (OpenAPI + generated TypeScript schema)
- SSR/BFF frontend patterns with clear auth boundaries

## Current Features

### Web Experience

- Global command/search experience for champions, summoners, and tier list
- Tier list page with role and rank tier filters
- Champion index and champion detail pages
- Matchup analysis pages (`/matchups`, `/matchups/[championId]`)
- Pro builds pages (`/pro-builds`, `/pro-builds/[championId]`) backed by tracked pro/high-ELO roster data
- Champion detail includes:
  - win rates by role/tier
  - ban rate
  - role rank + role population metadata
  - top builds
  - matchup tables (`counters`, `favorable`, and full matchup universe)
  - full rune setups (primary, secondary, stat shards)
- Unified summoner profile + match history view with refresh workflow
- Paged match history with inline expandable match details and queue-aware filtering support
- Legacy `/summoners/*/matches` routes redirect to the unified summoner page state
- Account pages for registration, login, and favorites

### Backend + Data Pipeline

- REST API with mixed auth model (`AppOnly`, `UserOnly`, `AppOrUser`)
- Hangfire-backed background processing with queue prioritization
- Riot data ingestion and patch-aware static data updates
- Continuous analytics ingestion and adaptive refresh jobs
- Ranked-first + all-mode match ingestion orchestration with non-ranked backfill windows
- Cursor-based non-ranked backfill progression per summoner for monotonic historical ingestion
- Timeline-derived ranked @15 snapshots and retryable timeline fetch-state tracking
- Pro roster management endpoints for manual curation of pro/high-ELO tracked summoners
- Match queue metadata + match bans persisted for richer filtering and ban-rate analytics
- Rune selection hierarchy persisted per match participant:
  - tree (`Primary`, `Secondary`, `StatShards`)
  - slot index within each tree
  - style/path id metadata
- Rune integrity backfill job for older/legacy rows

## Tech Stack

- .NET 10 SDK 
- ASP.NET Core Web API (`Transcendence.WebAPI`)
- .NET Worker + Hangfire (`Transcendence.Service`)
- EF Core + PostgreSQL
- Redis + HybridCache
- Next.js App Router + Tailwind CSS (`apps/web`)
- OpenAPI spec + generated TypeScript client (`openapi-typescript`, `openapi-fetch`)
- Docker Compose for local environment orchestration

## Architecture Overview

```text
Browser
  -> Next.js app (SSR + BFF routes in apps/web/api/*)
  -> Transcendence.WebAPI (REST, auth, enqueue jobs)
  -> PostgreSQL / Redis

Transcendence.Service (Hangfire worker)
  -> pulls queued + recurring jobs
  -> calls Riot APIs
  -> updates PostgreSQL
  -> refreshes analytics/cache
```

Key behavior:
- API handles request/response and lightweight orchestration.
- Worker owns heavy and scheduled work (refresh, ingestion, analytics, backfills).
- Frontend uses route handlers as a BFF so browser JS does not directly handle backend auth tokens.

## Repository Structure

| Project | Purpose |
|---|---|
| `Transcendence.WebAPI` | Public and authenticated REST API |
| `Transcendence.Service` | Background worker host + Hangfire server |
| `Transcendence.Service.Core` | Core services (analytics, auth, Riot integration, jobs) |
| `Transcendence.Data` | EF Core `DbContext`, entities, repositories |
| `Transcendence.WebAdminPortal` | Hangfire dashboard host |
| `apps/web` | Next.js frontend (pages + BFF route handlers) |
| `packages/api-client` | Generated TypeScript schema/client artifacts |
| `openapi` | Committed OpenAPI spec |
| `docs` | Development, API, and architecture docs |

## Quick Start (Docker Recommended)

1. Start backend infrastructure and .NET services:

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

PowerShell equivalent:

```powershell
Copy-Item apps/web/.env.example apps/web/.env.local
```

Set at minimum:
- `TRN_BACKEND_BASE_URL=http://localhost:8080`
- `TRN_BACKEND_API_KEY=trn_bootstrap_dev_key` (or a generated app key)

4. Run the web app:

```bash
corepack pnpm web:dev
```

Local endpoints:
- Web: `http://localhost:3000`
- API: `http://localhost:8080`
- API health: `http://localhost:8080/health/live`, `http://localhost:8080/health/ready`
- Hangfire admin portal: `http://localhost:8081`
- pgAdmin: `http://localhost:5050`

## API and Client Contract

Contract source of truth:
- `openapi/transcendence.v1.json`

Key backend areas:
- Summoners and match stats
- Analytics and tier list endpoints
- Auth and API key management
- User preferences/favorites
- Live game lookup

Frontend typing:
- `packages/api-client/src/schema.ts` is generated from OpenAPI


## Documentation Map

- `docs/DEVELOPMENT.md`: setup, environment, jobs, and operational runbooks
- `docs/API.md`: auth model, endpoint map, and contract workflow
- `docs/ARCHITECTURE.md`: system boundaries, data flow, and job orchestration
- `docs/BACKEND_TASKS_FRONTEND_OVERHAUL.md`: backend follow-ups required to remove frontend placeholders
- `CLAUDE.md` / `AGENTS.md`: agent-specific workflow guidance


## License

A license file is not currently committed in this repository.
