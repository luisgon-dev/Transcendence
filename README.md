# Transcendence Backend

Backend platform for League of Legends analytics. This repository contains a Web API, background worker, shared core/domain services, and an admin Hangfire dashboard.

## Current Status

- Project roadmap phases 1-4 are implemented (foundation, summoner profiles, champion analytics, live game, and authentication).
- Phase 5 (management/monitoring hardening) is not implemented yet.
- Primary stack target is `.NET 10` (`global.json` pins SDK `10.0.0`).

## Tech Stack

- ASP.NET Core Web API (`net10.0`)
- .NET Worker Service (`net10.0`)
- Entity Framework Core + PostgreSQL
- Hangfire + Hangfire.PostgreSql
- Redis + HybridCache (L1 in-memory + L2 Redis)
- Camille Riot API SDK
- Swagger/OpenAPI
- Docker + Docker Compose

## Solution Structure

| Project | Purpose |
|---|---|
| `Transcendence.WebAPI` | Public and authenticated REST API |
| `Transcendence.Service` | Background processing host (Hangfire server + recurring jobs) |
| `Transcendence.Service.Core` | Domain/application services (analytics, auth, live game, Riot integration, jobs) |
| `Transcendence.Data` | EF Core DbContext, entities, repositories |
| `Transcendence.WebAdminPortal` | Hangfire dashboard host |

## Implemented Capabilities

- Summoner lookup by Riot ID + platform route
- Background summoner refresh queueing with refresh-lock protection
- Match ingestion and deduplication
- Summoner stats endpoints (overview, champions, roles, recent matches, match detail)
- Champion analytics endpoints (tier list, win rates, builds, matchups)
- Live game lookup and participant/team analysis APIs
- API key auth (app identity) and JWT auth (user identity)
- User favorites and preference persistence
- Scheduled jobs for patch detection, retrying failed matches, analytics refresh/ingestion, and live-game polling

## Authentication Model

- `AppOnly` endpoints use header: `X-API-Key: <key>`
- `UserOnly` endpoints use bearer JWT: `Authorization: Bearer <token>`
- `AppOrUser` accepts either auth scheme

Bootstrap access for key management can be configured with:

- `Auth:BootstrapApiKey`

## API Endpoints (Current)

### Summoner + Stats (public)

- `GET /api/summoners/{region}/{name}/{tag}`
- `POST /api/summoners/{region}/{name}/{tag}/refresh`
- `GET /api/summoners/{summonerId}/stats/overview`
- `GET /api/summoners/{summonerId}/stats/champions`
- `GET /api/summoners/{summonerId}/stats/roles`
- `GET /api/summoners/{summonerId}/matches/recent`
- `GET /api/summoners/{summonerId}/matches/{matchId}`

### Analytics

- `GET /api/analytics/tierlist` (public)
- `GET /api/analytics/champions/{championId}/winrates` (public)
- `GET /api/analytics/champions/{championId}/builds` (public)
- `GET /api/analytics/champions/{championId}/matchups` (public)
- `POST /api/analytics/cache/invalidate` (`AppOnly`)
- `POST /api/analytics/champions/cache/invalidate` (`AppOnly`)

### Live Game (`AppOnly`)

- `GET /api/summoners/{region}/{gameName}/{tagLine}/live-game`

### Auth + Keys

- `POST /api/auth/register` (public)
- `POST /api/auth/login` (public)
- `POST /api/auth/refresh` (public)
- `POST /api/auth/password-reset` (public)
- `GET /api/auth/me` (`AppOrUser`)
- `GET /api/auth/keys` (`AppOnly`)
- `POST /api/auth/keys` (`AppOnly`)
- `POST /api/auth/keys/{id}/revoke` (`AppOnly`)
- `POST /api/auth/keys/{id}/rotate` (`AppOnly`)

### User Preferences (`UserOnly`)

- `GET /api/users/me/favorites`
- `POST /api/users/me/favorites`
- `DELETE /api/users/me/favorites/{favoriteId}`
- `GET /api/users/me/preferences`
- `PUT /api/users/me/preferences`

## Local Development

### Prerequisites

- .NET SDK 10
- PostgreSQL 16+
- Redis 7+
- Riot API key

### Recommended: Docker Compose

```bash
docker compose up --build
```

Compose services:

- PostgreSQL: `localhost:5432`
- Redis: `localhost:6379`
- pgAdmin: `http://localhost:5050`
- Web API: `http://localhost:8080`
- WebAdminPortal (Hangfire): `http://localhost:8081/hangfire`

## Production Docker (GHCR Images)

Use the production compose stack that pulls `:main` images from GHCR:

- `ghcr.io/luisgon-dev/transcendencebackend-webapi:main`
- `ghcr.io/luisgon-dev/transcendencebackend-service:main`
- `ghcr.io/luisgon-dev/transcendencebackend-webadminportal:main`

Setup:

```bash
cp .env.production.example .env.production
# edit .env.production and set secure values (JWT_SIGNING_KEY, AUTH_BOOTSTRAP_API_KEY, GHCR_TOKEN)
echo "$GHCR_TOKEN" | docker login ghcr.io -u "$GHCR_USERNAME" --password-stdin
docker compose --env-file .env.production -f docker-compose.production.yml up -d
```

Included services:

- PostgreSQL
- Redis
- Web API
- Worker service
- WebAdminPortal (Hangfire dashboard)
- Dozzle logs UI (`http://localhost:9999`)
- Watchtower auto-updater for app containers

### Run Without Docker

Set secrets/config first.

`Transcendence.WebAPI`:

```bash
dotnet user-secrets set "ConnectionStrings:MainDatabase" "Host=localhost;Port=5432;Database=transcendence;Username=postgres;Password=postgres" --project Transcendence.WebAPI
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" --project Transcendence.WebAPI
dotnet user-secrets set "ConnectionStrings:RiotApi" "RGAPI-your-key" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:Jwt:Key" "CHANGE_THIS_TO_A_REAL_32+_CHAR_SECRET" --project Transcendence.WebAPI
# Optional bootstrap key for first API key management access
dotnet user-secrets set "Auth:BootstrapApiKey" "trn_bootstrap_dev_key" --project Transcendence.WebAPI
```

`Transcendence.Service`:

```bash
dotnet user-secrets set "ConnectionStrings:MainDatabase" "Host=localhost;Port=5432;Database=transcendence;Username=postgres;Password=postgres" --project Transcendence.Service
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" --project Transcendence.Service
dotnet user-secrets set "ConnectionStrings:RiotApi" "RGAPI-your-key" --project Transcendence.Service
```

`Transcendence.WebAdminPortal`:

```bash
dotnet user-secrets set "ConnectionStrings:MainDatabase" "Host=localhost;Port=5432;Database=transcendence;Username=postgres;Password=postgres" --project Transcendence.WebAdminPortal
```

Apply DB migrations:

```bash
dotnet ef database update --project Transcendence.Service --startup-project Transcendence.Service
```

Run services:

```bash
# Terminal 1
dotnet run --project Transcendence.WebAPI

# Terminal 2
dotnet run --project Transcendence.Service

# Optional terminal 3 (Hangfire dashboard)
dotnet run --project Transcendence.WebAdminPortal
```

Default dev URLs from launch profiles:

- Web API: `https://localhost:7053` (also `http://localhost:5092`)
- WebAdminPortal: `https://localhost:7206` (also `http://localhost:5033`)

## Known Gaps

- No automated unit/integration test projects yet.
- Phase 5 management goals pending (health endpoints, OpenTelemetry metrics, hardened admin controls).
