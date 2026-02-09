# Development

This repo contains a .NET backend (API + background worker) and a Next.js web frontend.

## Prerequisites

- .NET SDK (see `global.json`)
- Docker Desktop (recommended) or local:
  - PostgreSQL 16+
  - Redis 7+
- Node.js (recommended: Node 22)
- pnpm (repo pins `pnpm@10.22.0` in root `package.json`)

## Quick Start (Recommended)

1. Start infrastructure + backend:

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

Set:
- `TRN_BACKEND_BASE_URL=http://localhost:8080`
- `TRN_BACKEND_API_KEY=<api key for AppOnly endpoints>`

4. Run the web app:

```bash
corepack pnpm web:dev
```

Web: `http://localhost:3000`

## Run Without Docker (Backend)

### Secrets

`Transcendence.WebAPI`:

```bash
dotnet user-secrets set "ConnectionStrings:MainDatabase" "Host=localhost;Port=5432;Database=transcendence;Username=postgres;Password=postgres" --project Transcendence.WebAPI
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" --project Transcendence.WebAPI
dotnet user-secrets set "ConnectionStrings:RiotApi" "RGAPI-your-key" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:Jwt:Key" "CHANGE_THIS_TO_A_REAL_32+_CHAR_SECRET" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:BootstrapApiKey" "trn_bootstrap_dev_key" --project Transcendence.WebAPI
```

`Transcendence.Service`:

```bash
dotnet user-secrets set "ConnectionStrings:MainDatabase" "Host=localhost;Port=5432;Database=transcendence;Username=postgres;Password=postgres" --project Transcendence.Service
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" --project Transcendence.Service
dotnet user-secrets set "ConnectionStrings:RiotApi" "RGAPI-your-key" --project Transcendence.Service
```

### Database migrations

```bash
dotnet ef database update --project Transcendence.Service --startup-project Transcendence.Service
```

### Run services

```bash
dotnet run --project Transcendence.WebAPI
dotnet run --project Transcendence.Service
dotnet run --project Transcendence.WebAdminPortal
```

## Web Commands

From repo root:

```bash
corepack pnpm web:dev
corepack pnpm web:test
corepack pnpm web:lint
corepack pnpm web:build
```

## OpenAPI + TypeScript Client

Source of truth: `openapi/transcendence.v1.json`

```bash
corepack pnpm api:gen
corepack pnpm api:check
```

## Documentation Policy (Contributor Requirement)

If a change affects any of the following, update docs in the same PR:

- Runtime behavior, user flows, or UI routes: update `README.md` and/or `docs/ARCHITECTURE.md`
- API endpoints, payloads, auth, or status codes: update `docs/API.md` and ensure OpenAPI is up to date
- Environment variables, secrets, compose, or run commands: update `docs/DEVELOPMENT.md`

