# Transcendence

[![CI](https://github.com/luisgon-dev/Transcendence/actions/workflows/ci-web-backend.yml/badge.svg)](https://github.com/luisgon-dev/Transcendence/actions/workflows/ci-web-backend.yml)
[![Docker Images](https://github.com/luisgon-dev/Transcendence/actions/workflows/docker-images.yml/badge.svg)](https://github.com/luisgon-dev/Transcendence/actions/workflows/docker-images.yml)
[![License](https://img.shields.io/github/license/luisgon-dev/Transcendence)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](global.json)
[![Next.js](https://img.shields.io/badge/Next.js-16-000000?logo=nextdotjs&logoColor=white)](apps/web)
[![pnpm](https://img.shields.io/badge/pnpm-10.22.0-F69220?logo=pnpm&logoColor=white)](package.json)

Transcendence is a Riot analytics monorepo for League of Legends and Teamfight Tactics. It combines a .NET Web API, a Hangfire-powered background worker, a Next.js web frontend, PostgreSQL, Redis, and a generated TypeScript API client in one repository.

## What This Project Does

Transcendence delivers two game surfaces, `/lol/*` and `/tft/*`, on top of a shared platform:

- A public/authenticated Web API for reads, auth, and refresh requests
- A background worker that fetches Riot data, ingests matches, and refreshes analytics
- A Next.js web app that renders SSR pages and proxies backend requests through BFF route handlers
- A committed OpenAPI contract with a generated TypeScript client for frontend integrations

At a high level, the user-facing refresh flow works like this:

1. A client requests a summoner profile.
2. If data already exists, the API returns it immediately.
3. If data is missing, the API returns `202 Accepted` and enqueues a Hangfire job.
4. The worker fetches Riot data, stores it in PostgreSQL, and the client polls until the profile is ready.

## Why It Is Useful

- LoL and TFT stay isolated at the data and job level while sharing auth, infrastructure, and deployment workflows.
- The web app keeps tokens in HttpOnly cookies and uses BFF proxy routes instead of exposing backend credentials to browser JavaScript.
- Docker-based local development gives contributors a repeatable stack with PostgreSQL, Redis, Web API, worker, and optional tooling.
- The committed OpenAPI spec and generated client reduce frontend/backend drift.
- Admin routes and APIs provide queue visibility, cache controls, metrics, and operational logs for maintainers.
- The production worker now prioritizes multi-region ranked solo coverage with high-value/high-elo roster seeding instead of relying on a single slow analytics ingestion loop.

## Repository Layout

| Path | Purpose |
| --- | --- |
| `Transcendence.WebAPI` | REST API, auth, health checks, Swagger/OpenAPI export, refresh job enqueueing |
| `Transcendence.Service` | Hangfire worker for Riot ingestion, refresh jobs, analytics, and maintenance work |
| `Transcendence.Service.Core` | Shared domain services, DTOs, jobs, integrations, and application logic |
| `Transcendence.Data` | EF Core models, repositories, DbContext, and database access |
| `apps/web` | Next.js App Router frontend and BFF route handlers |
| `packages/api-client` | Generated TypeScript client built from the committed OpenAPI spec |
| `openapi/transcendence.v1.json` | Source-of-truth API contract committed to the repo |
| `docs/` | Canonical developer, API, and architecture documentation |

## Getting Started

### Prerequisites

- Git
- Docker Desktop or another Docker runtime
- .NET SDK `10.0.102` from [global.json](global.json)
- Node.js `22` from [.nvmrc](.nvmrc)
- Corepack-enabled `pnpm`

### Recommended Local Setup

1. Clone the repository and install JavaScript dependencies:

```bash
git clone https://github.com/luisgon-dev/Transcendence.git
cd Transcendence
corepack enable
corepack pnpm install
corepack pnpm hooks:install
```

2. Copy the backend environment template:

```bash
cp .env.example .env
```

3. Set the values you need in `.env`.

At minimum, review these variables before a real local run:

- `JWT_SIGNING_KEY`
- `AUTH_BOOTSTRAP_API_KEY`
- `WEB_TRN_BACKEND_API_KEY`
- `RIOT_API_KEY_LOL`
- `RIOT_API_KEY_TFT`

Notes:

- The Web API can start without Riot API keys for basic reads and Swagger export, but refresh and ingestion flows need valid Riot keys in the worker.
- `WEB_TRN_BACKEND_API_KEY` must be a valid AppOnly key accepted by the backend. For local bootstrapping, contributors often use the bootstrap key until they create a dedicated key.

4. Copy the web environment template:

```bash
cp apps/web/.env.example apps/web/.env.local
```

The default local web settings are already suitable for a Compose-backed backend:

```env
TRN_BACKEND_BASE_URL=http://localhost:8080
TRN_BACKEND_API_KEY=trn_bootstrap_dev_key
```

5. Start the backend stack:

```bash
docker compose up --build
```

6. In a separate terminal, run the web app locally:

```bash
corepack pnpm web:dev
```

### Local URLs

- Web app: `http://localhost:3000`
- Web API: `http://localhost:8080`
- Live health: `http://localhost:8080/health/live`
- Ready health: `http://localhost:8080/health/ready`
- Admin UI: `http://localhost:3000/admin`
- pgAdmin: `http://localhost:5050` with `docker compose --profile local-tools up`

### Example Usage

Check API health:

```bash
curl http://localhost:8080/health/live
```

Queue a TFT summoner refresh:

```bash
curl -X POST http://localhost:8080/api/tft/summoners/na1/<gameName>/<tagLine>/refresh
```

Open the local app:

```text
http://localhost:3000
```

### Common Commands

```bash
corepack pnpm web:dev
corepack pnpm web:build
corepack pnpm web:lint
corepack pnpm web:test
corepack pnpm backend:test
corepack pnpm api:gen
corepack pnpm api:check
corepack pnpm e2e:stack
corepack pnpm e2e:local
dotnet test tests/Transcendence.Service.Core.Tests
dotnet test tests/Transcendence.WebAPI.Tests
```

### Running Without Docker

You can run `Transcendence.WebAPI` and `Transcendence.Service` directly with `dotnet run`, but local secrets, connection strings, and migration steps are documented in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md). Use that path if you need a non-Compose backend workflow.

## Help And Documentation

Start with the canonical docs:

- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) for setup, secrets, local run modes, testing, and OpenAPI/client generation
- [docs/API.md](docs/API.md) for endpoint areas, auth semantics, status-code expectations, and API contract notes
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for system boundaries, refresh flows, caching, ingestion, and BFF behavior
- [AGENTS.md](AGENTS.md) for repository-specific guidance used by coding agents

For repository support:

- Open an issue: <https://github.com/luisgon-dev/Transcendence/issues>
- Review CI workflows: [.github/workflows/ci-web-backend.yml](.github/workflows/ci-web-backend.yml) and [.github/workflows/docker-images.yml](.github/workflows/docker-images.yml)

## Maintainers And Contributing

This project is maintained by [luisgon-dev](https://github.com/luisgon-dev).

Contributions are welcome through issues and pull requests. Before opening a PR:

- Run the relevant checks locally: `corepack pnpm web:lint`, `corepack pnpm web:test`, `corepack pnpm backend:test`, and `corepack pnpm api:check` when API changes are involved.
- Update the canonical docs in the same PR when you change API behavior, environment variables, run commands, or architecture.
- Regenerate the OpenAPI artifacts when backend contract changes affect `openapi/transcendence.v1.json`.
- Do not hand-edit EF migration files. Update the EF model first, then generate migrations with `dotnet ef migrations add ...`.

If you are not sure where a change belongs, start with [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), then link your PR to the relevant issue or explain the change scope clearly in the description.

## License

Transcendence is licensed under the [GNU General Public License v3.0](LICENSE).
