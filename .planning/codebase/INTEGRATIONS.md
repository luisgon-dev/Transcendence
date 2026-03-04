# INTEGRATIONS

## Scope
- Focus: internal and external integration map for the current codebase.
- Emphasis on concrete call paths, auth boundaries, and runtime dependencies.

## External Integrations

### Riot Games API (core gameplay/account data)
- SDK/package: `Camille.RiotGames` (`Transcendence.Service.Core/Transcendence.Service.Core.csproj`).
- API key source: `ConnectionStrings:RiotApi` or `RiotApi:ApiKey` (`Transcendence.WebAPI/Program.cs`, `Transcendence.Service.Core/Services/Extensions/ServiceCollectionExtensions.cs`).
- WebAPI uses Riot account service directly for select endpoints (`Transcendence.WebAPI/Program.cs`, `Transcendence.WebAPI/Controllers/ProSummonersController.cs`).
- Worker and core jobs/services use Riot SDK for summoner, match, rank, and live game flows (`Transcendence.Service/Program.cs`, `Transcendence.Service.Core/Services/RiotApi/*`, `Transcendence.Service.Core/Services/Jobs/*`).

### Static game data CDNs (Data Dragon + CommunityDragon)
- Backend static-data ingestion fetches patch, rune, and item metadata from:
  - `https://ddragon.leagueoflegends.com/api/versions.json`
  - `https://raw.communitydragon.org/.../perks.json`
  - `https://raw.communitydragon.org/.../perkstyles.json`
  - `https://raw.communitydragon.org/.../items.json`
  (`Transcendence.Service.Core/Services/StaticData/Implementations/StaticDataService.cs`).
- Web layer independently fetches champion/item/spell/rune data from Data Dragon + CommunityDragon for UI static endpoints (`apps/web/lib/staticData.ts`, `apps/web/app/api/static/*/route.ts`).

### PostgreSQL
- Primary relational store and Hangfire backing store.
- Registered via EF Core Npgsql in API and worker hosts (`Transcendence.WebAPI/Program.cs`, `Transcendence.Service/Program.cs`).
- Local/prod infra wiring via compose (`docker-compose.yml`, `docker-compose.production.yml`).

### Redis
- Distributed cache backend for ASP.NET HybridCache and cache invalidation patterns.
- Registered in API and worker (`Transcendence.WebAPI/Program.cs`, `Transcendence.Service/Program.cs`).
- Service provisioning in compose files (`docker-compose.yml`, `docker-compose.production.yml`).

### Container Registry and CI Supply Chain
- Docker images built and pushed to GHCR in workflow (`.github/workflows/docker-images.yml`).
- Production deployment consumes GHCR images (`docker-compose.production.yml`).
- Cosign signing is integrated into image pipeline (`.github/workflows/docker-images.yml`).

## Internal Service-to-Service Integrations

### Web BFF to WebAPI
- Proxy handlers under `apps/web/app/api/trn/*` forward requests to backend URL from `TRN_BACKEND_BASE_URL` (`apps/web/lib/env.ts`, `apps/web/lib/trnProxy.ts`).
- Proxy strips browser cookies and forwards request body/headers with generated request ID (`apps/web/lib/trnProxy.ts`).
- AppOnly path is intentionally allowlisted to live-game GET route pattern (`apps/web/app/api/trn/app/[...path]/route.ts`).

### Session/Auth handoff between Web and WebAPI
- Web login/register/logout route handlers call backend `/api/auth/*` endpoints via generated API client (`apps/web/app/api/session/login/route.ts`, `apps/web/app/api/session/register/route.ts`, `apps/web/app/api/session/logout/route.ts`, `apps/web/lib/trnClient.ts`).
- Tokens stored in HttpOnly cookies by Next server code (`apps/web/lib/authCookies.ts`).
- User proxy refreshes access tokens with `/api/auth/refresh` before retrying backend calls (`apps/web/app/api/trn/user/[...path]/route.ts`).

### Admin integration path
- Admin UI/server calls enforce admin role and same-origin checks before backend proxying (`apps/web/app/api/trn/admin/[...path]/route.ts`, `apps/web/lib/authz.ts`, `apps/web/lib/adminSession.ts`).
- Direct admin helper (`adminBackend.ts`) calls backend with bearer tokens for server-side admin pages/actions (`apps/web/lib/adminBackend.ts`).

### API host to Worker via Hangfire
- WebAPI enqueues jobs using Hangfire client configuration (`Transcendence.WebAPI/Program.cs`, `Transcendence.WebAPI/Controllers/*`).
- Worker executes queues `refresh-high`, `default`, `refresh-low` (`Transcendence.Service/Program.cs`).
- Job scheduling/cleanup logic resides in hosted workers (`Transcendence.Service/Workers/DevelopmentWorker.cs`, `Transcendence.Service/Workers/ProductionWorker.cs`).

## Contract and Codegen Integration
- Backend OpenAPI published by Swagger middleware (`Transcendence.WebAPI/Program.cs`).
- Export script runs WebAPI and downloads spec to `openapi/transcendence.v1.json` (`scripts/openapi/export.sh`).
- TS client generation consumes committed spec (`packages/api-client/package.json`, `packages/api-client/src/index.ts`).
- CI enforces no drift (`package.json` script `api:check`, `.github/workflows/ci-web-backend.yml`).

## Security and Policy Integration Points
- Dual auth modes (API key + JWT) configured as named ASP.NET auth policies (`Transcendence.WebAPI/Program.cs`, `Transcendence.WebAPI/Security/AuthPolicies.cs`).
- Dedicated rate limit policies for auth endpoints and expensive reads (`Transcendence.WebAPI/Program.cs`).
- Web BFF blocks path traversal-like segments during proxy normalization (`apps/web/lib/proxyPath.ts`, `apps/web/lib/trnProxy.ts`).
- Backend bootstrap API key can be restricted to development-only via config (`Transcendence.WebAPI/Program.cs`, `Transcendence.WebAPI/appsettings.json`).

## Operational Integrations
- Break-glass Hangfire dashboard host with basic auth in non-dev (`Transcendence.WebAdminPortal/Program.cs`, `Transcendence.WebAdminPortal/Security/HangfireDashboardBasicAuthFilter.cs`).
- Dev tooling services include pgAdmin (`docker-compose.yml`).
- Prod compose includes Dozzle and WhatsUpDocker operational sidecars (`docker-compose.production.yml`).

## Planning Notes
- This codebase integrates with two distinct external game-data channels: Riot APIs (transactional/player state) and Dragon CDNs (static metadata), which should be versioned and failure-handled separately.
- Web BFF and backend are tightly coupled through OpenAPI and shared auth semantics; maintain `api:check` as a hard gate for any endpoint change.
