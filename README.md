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

### Local E2E

Use full Compose when you want the closest local match to production, including the worker:

```bash
cp .env.example .env
corepack pnpm e2e:stack
```

That command starts the full stack in Docker, waits for the API and web app to come up, then runs the Playwright suite against `http://localhost:3000`.

For faster frontend iteration, keep the backend in Compose and run the web app locally:

```bash
docker compose up --build -d postgres redis webapi service
cp apps/web/.env.example apps/web/.env.local
corepack pnpm web:dev
corepack pnpm e2e:local
```

### Local Data Slice

For realistic local LoL/TFT testing, you can copy a game-only slice from another Postgres database into a disposable local database.

Set connection strings in your shell, keeping the source credentials out of repo files:

```bash
export TRN_SOURCE_DB='Host=192.168.0.221;Port=5432;Database=transcendence;Username=postgres;Password=testpassword123!'
export TRN_TARGET_DB='Host=localhost;Port=5432;Database=transcendence_slice;Username=postgres;Password=changme'
```

Run a sized import:

```bash
corepack pnpm data:slice:sync -- \
  --regions NA1,EUW1,KR \
  --patch-depth 2 \
  --lol-max-matches-per-region 2000 \
  --lol-sample-percent 20 \
  --tft-max-matches-per-region 1000 \
  --tft-sample-percent 15
```

That workflow:

- verifies source and target schema versions match
- truncates only the game-slice tables in the target database
- copies LoL/TFT data needed for local profiles, search, matches, analytics, and static catalogs
- lets you size the import by per-region row caps and sample percentages

Validate identifier safety after import:

```bash
corepack pnpm data:validate-identifiers -- --sample-size 10
```

If Riot API keys were rotated and you want fresh encrypted identifiers with the current keys:

```bash
corepack pnpm data:rehydrate-riot-ids -- --games all --limit 250 --only-missing
```

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
corepack pnpm data:slice:sync -- --help
corepack pnpm data:validate-identifiers -- --skip-live
corepack pnpm data:rehydrate-riot-ids -- --games tft --limit 100
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
