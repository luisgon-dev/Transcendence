# Transcendence

[![CI](https://github.com/luisgon-dev/Transcendence/actions/workflows/ci-web-backend.yml/badge.svg)](https://github.com/luisgon-dev/Transcendence/actions/workflows/ci-web-backend.yml)
[![Docker Images](https://github.com/luisgon-dev/Transcendence/actions/workflows/docker-images.yml/badge.svg)](https://github.com/luisgon-dev/Transcendence/actions/workflows/docker-images.yml)
[![License](https://img.shields.io/github/license/luisgon-dev/Transcendence)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](./global.json)
[![Next.js](https://img.shields.io/badge/Next.js-16-000000?logo=nextdotjs&logoColor=white)](./apps/web)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1?logo=postgresql&logoColor=white)](./compose.yml)
[![Redis](https://img.shields.io/badge/Redis-7-DC382D?logo=redis&logoColor=white)](./compose.yml)
[![pnpm](https://img.shields.io/badge/pnpm-10.22.0-F69220?logo=pnpm&logoColor=white)](./package.json)

Transcendence is a Riot analytics monorepo with separate League of Legends and Teamfight Tactics surfaces.

The repo contains:

- `Transcendence.WebAPI` for REST reads, auth, and refresh requests
- `Transcendence.Service` for Hangfire jobs, ingestion, and maintenance work
- `apps/web` for the Next.js frontend and BFF routes
- [`openapi/transcendence.v1.json`](./openapi/transcendence.v1.json) and [`packages/api-client`](./packages/api-client) for the committed API contract

## Quick Start

### Requirements

- .NET SDK `10.0.102` or whatever matches [`global.json`](./global.json)
- Node.js `22` from [`.nvmrc`](./.nvmrc)
- Corepack-enabled `pnpm`
- Docker Desktop or another local Docker runtime

### Local Setup

1. Copy the root environment template:

```bash
cp .env.example .env
```

2. Start the backend stack:

```bash
docker compose up --build
```

3. Install JavaScript dependencies:

```bash
corepack pnpm install
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

5. Run the frontend:

```bash
corepack pnpm web:dev
```

Local URLs:

- Web app: `http://localhost:3000`
- Web API: `http://localhost:8080`
- Health: `http://localhost:8080/health/live`
- Admin UI: `http://localhost:3000/admin`
- pgAdmin: `http://localhost:5050` with `docker compose --profile local-tools up`

Notes:

- Shared backend defaults live in [`config/backend.shared.json`](./config/backend.shared.json).
- The WebAPI host is keyless; Riot API keys are only required for the worker and TFT/LoL refresh flows.
- TFT catalog pages (`/tft/champions`, `/tft/items`, `/tft/traits`, `/tft/augments`) reflect the active set only.

## What You Get

- LoL summoner profiles, champion analytics, matchups, builds, and pro builds
- TFT comps, champions, items, traits, augments, and stored summoner history
- Shared auth, admin tooling, and background refresh pipelines
- Docker-based local development and GHCR-based deploy images

## Common Commands

```bash
corepack pnpm web:dev
corepack pnpm web:build
corepack pnpm web:test
corepack pnpm backend:test
corepack pnpm api:gen
corepack pnpm api:check
dotnet test tests/Transcendence.Service.Core.Tests
dotnet test tests/Transcendence.WebAPI.Tests
```

## Docs

- [`docs/DEVELOPMENT.md`](./docs/DEVELOPMENT.md) for setup, secrets, and local run modes
- [`docs/API.md`](./docs/API.md) for routes, auth, and OpenAPI expectations
- [`docs/ARCHITECTURE.md`](./docs/ARCHITECTURE.md) for component boundaries and background flows
- [`AGENTS.md`](./AGENTS.md) for repository-specific agent guidance

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).
