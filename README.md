# Transcendence

[![CI](https://github.com/luisgon-dev/Transcendence/actions/workflows/ci-web-backend.yml/badge.svg)](https://github.com/luisgon-dev/Transcendence/actions/workflows/ci-web-backend.yml)
[![Docker Images](https://github.com/luisgon-dev/Transcendence/actions/workflows/docker-images.yml/badge.svg)](https://github.com/luisgon-dev/Transcendence/actions/workflows/docker-images.yml)
[![License](https://img.shields.io/github/license/luisgon-dev/Transcendence)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](./global.json)
[![Next.js](https://img.shields.io/badge/Next.js-16-000000?logo=nextdotjs&logoColor=white)](./apps/web)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1?logo=postgresql&logoColor=white)](./docker-compose.yml)
[![Redis](https://img.shields.io/badge/Redis-7-DC382D?logo=redis&logoColor=white)](./docker-compose.yml)
[![pnpm](https://img.shields.io/badge/pnpm-10.22.0-F69220?logo=pnpm&logoColor=white)](./package.json)

Transcendence is a full-stack League of Legends analytics monorepo. It combines an ASP.NET Core API, a Hangfire-backed worker, a Next.js App Router frontend, a committed OpenAPI contract, and a generated TypeScript client in one repository.

## Overview

- `Transcendence.WebAPI` serves authenticated and app-key protected REST endpoints.
- `Transcendence.Service` runs Hangfire jobs for ingestion, refresh, backfills, and analytics maintenance.
- `apps/web` is the SSR-first frontend and BFF layer for browser clients.
- `Transcendence.Data` and `Transcendence.Service.Core` contain the EF Core and application-domain layers.
- `openapi/transcendence.v1.json` is the committed API contract used for TS client generation.
- `packages/api-client` contains the generated TypeScript schema and client artifacts used by the web app.

## What The Repository Covers

- Summoner profile lookup, refresh orchestration, autosuggest, and match history
- Champion analytics including tier list, win rates, builds, matchups, and pro builds
- Auth, session flows, API key management, and admin-only operational tooling
- Background ingestion and adaptive refresh pipelines backed by Hangfire
- Web BFF routes under `apps/web/app/api/*` that keep backend credentials server-side
- Docker-based local development and GHCR-backed production images

## Stack

| Area | Technology |
| --- | --- |
| Backend | .NET 10, ASP.NET Core, Hangfire, EF Core |
| Frontend | Next.js 16, React 19, Tailwind CSS |
| Data | PostgreSQL 16, Redis 7 |
| API Contract | OpenAPI, `openapi-typescript`, `openapi-fetch` |
| Tooling | pnpm workspace, GitHub Actions, Docker Compose |

## Repository Layout

| Path | Purpose |
| --- | --- |
| `Transcendence.WebAPI` | REST API host |
| `Transcendence.Service` | Worker host and Hangfire server |
| `Transcendence.Service.Core` | Application and domain services |
| `Transcendence.Data` | EF Core DbContext, entities, and repositories |
| `apps/web` | Next.js frontend and BFF route handlers |
| `packages/api-client` | Generated TypeScript API client |
| `openapi` | Committed OpenAPI specification |
| `tests` | Backend and API test projects |
| `docs` | Development, API, and architecture docs |

## Local Development

### Prerequisites

- .NET SDK `10.0.102` or compatible with [`global.json`](./global.json)
- Node.js `22` from [`.nvmrc`](./.nvmrc)
- Corepack-enabled `pnpm`
- Docker Desktop or equivalent local Docker runtime

### Quick Start

1. Start Postgres, Redis, pgAdmin, the Web API, and the worker:

```bash
docker compose up --build
```

2. Install JavaScript dependencies:

```bash
corepack pnpm install
```

3. Install the repository Git hooks:

```bash
corepack pnpm hooks:install
```

4. Configure the web app:

```bash
cp apps/web/.env.example apps/web/.env.local
```

Minimum local web settings:

```bash
TRN_BACKEND_BASE_URL=http://localhost:8080
TRN_BACKEND_API_KEY=trn_bootstrap_dev_key
```

Optional admin bootstrap before starting compose:

```bash
ADMIN_BOOTSTRAP_EMAIL_0=you@example.com
```

5. Run the frontend:

```bash
corepack pnpm web:dev
```

### Local Endpoints

| Surface | URL |
| --- | --- |
| Web app | `http://localhost:3000` |
| Web API | `http://localhost:8080` |
| Liveness probe | `http://localhost:8080/health/live` |
| Readiness probe | `http://localhost:8080/health/ready` |
| Admin UI | `http://localhost:3000/admin` (admin role required) |
| pgAdmin | `http://localhost:5050` |

## Common Commands

From the repository root:

```bash
corepack pnpm web:dev
corepack pnpm web:build
corepack pnpm web:lint
corepack pnpm web:test
corepack pnpm backend:test
corepack pnpm api:gen
corepack pnpm api:check
corepack pnpm hooks:install
corepack pnpm precommit:check
```

Direct backend test entry points:

```bash
dotnet test tests/Transcendence.Service.Core.Tests
dotnet test tests/Transcendence.WebAPI.Tests
```

## API Contract And Client Generation

The repository commits its OpenAPI contract and derives the TypeScript client from it.

- OpenAPI source of truth: [`openapi/transcendence.v1.json`](./openapi/transcendence.v1.json)
- Spec export script: `scripts/openapi/export.sh`
- Generated client package: [`packages/api-client`](./packages/api-client)

Relevant commands:

```bash
corepack pnpm api:gen
corepack pnpm api:check
```

If Git hooks are installed, the pre-commit hook regenerates and stages OpenAPI artifacts when API-relevant files change.

## Runtime Architecture

- The web app uses Next.js route handlers as a BFF under `/api/session/*` and `/api/trn/*`.
- Browser clients never receive backend tokens directly; credentials stay in HttpOnly cookies or server-side app-key configuration.
- The Web API handles reads, auth, admin endpoints, and refresh requests.
- The worker processes ingestion, analytics refresh, patch/bootstrap flows, and maintenance jobs.
- PostgreSQL stores canonical match and summoner data; Redis backs caching and coordination.

## CI And Delivery

- `CI (Web + Backend)` runs backend tests plus web OpenAPI checks, linting, tests, and production build validation.
- `Docker Images` builds component images for `webapi`, `service`, and `web`, then publishes GHCR images on `main` and version tags.
- Production compose uses GHCR images:
  - `ghcr.io/luisgon-dev/transcendence-web:main`
  - `ghcr.io/luisgon-dev/transcendence-webapi:main`
  - `ghcr.io/luisgon-dev/transcendence-service:main`

## Documentation

- [`docs/DEVELOPMENT.md`](./docs/DEVELOPMENT.md): setup, secrets, run modes, and operational settings
- [`docs/API.md`](./docs/API.md): auth model, endpoint map, and OpenAPI workflow
- [`docs/ARCHITECTURE.md`](./docs/ARCHITECTURE.md): component boundaries, data flow, and background processing
- [`AGENTS.md`](./AGENTS.md): repository instructions for coding agents

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).
