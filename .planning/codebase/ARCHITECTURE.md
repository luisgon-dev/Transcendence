# Codebase Architecture

## 1. System Topology
- This repo is a monorepo with three .NET hosts plus one Next.js web app and a generated TypeScript client.
- API host: `Transcendence.WebAPI/Program.cs`.
- Background worker host: `Transcendence.Service/Program.cs`.
- Hangfire dashboard host: `Transcendence.WebAdminPortal/Program.cs`.
- Web app (SSR + BFF): `apps/web/app` and `apps/web/lib`.
- Shared domain/service layer: `Transcendence.Service.Core/Services`.
- Shared persistence layer: `Transcendence.Data/TranscendenceContext.cs` + `Transcendence.Data/Repositories`.

## 2. Compile-Time Layering
- `Transcendence.WebAPI/Transcendence.WebAPI.csproj` references `Transcendence.Service.Core` and `Transcendence.Data`.
- `Transcendence.Service/Transcendence.Service.csproj` references `Transcendence.Service.Core`.
- `Transcendence.Service.Core/Transcendence.Service.Core.csproj` references `Transcendence.Data`.
- `Transcendence.WebAdminPortal/Transcendence.WebAdminPortal.csproj` references `Transcendence.Service.Core`.
- Layer direction is effectively: Hosts -> `Service.Core` -> `Data`.

## 3. Runtime Components
- WebAPI configures HTTP concerns (controllers, auth, rate limiting, exception handling) in `Transcendence.WebAPI/Program.cs`.
- Worker configures Hangfire server and recurring scheduling in `Transcendence.Service/Program.cs`, `Transcendence.Service/Workers/ProductionWorker.cs`, and `Transcendence.Service/Workers/DevelopmentWorker.cs`.
- Admin portal exposes only Hangfire dashboard at `/hangfire` in `Transcendence.WebAdminPortal/Program.cs`.
- EF model and indexes are centralized in `Transcendence.Data/TranscendenceContext.cs`; migrations live in `Transcendence.Service/Migrations`.
- Cross-cutting DI registration is centralized in `Transcendence.Service.Core/Services/Extensions/ServiceCollectionExtensions.cs` and `Transcendence.Data/Extensions/ServiceCollectionExtensions.cs`.

## 4. External Dependencies
- PostgreSQL is primary persistence for app data and Hangfire storage (`Transcendence.WebAPI/Program.cs`, `Transcendence.Service/Program.cs`).
- Redis backs distributed/hybrid cache (`Transcendence.WebAPI/Program.cs`, `Transcendence.Service/Program.cs`).
- Riot API access goes through Camille SDK in Riot services (`Transcendence.Service.Core/Services/RiotApi/Implementations/*`).
- Static game assets are fetched by web server-side helpers (`apps/web/lib/staticData.ts`).
- Local/prod service composition is declared in `docker-compose.yml` and `docker-compose.production.yml`.

## 5. Authentication and Authorization Boundaries
- API supports custom API-key scheme + JWT bearer in `Transcendence.WebAPI/Program.cs` and `Transcendence.WebAPI/Security/ApiKeyAuthenticationHandler.cs`.
- Policy names are centralized in `Transcendence.WebAPI/Security/AuthPolicies.cs`: `AppOnly`, `UserOnly`, `AppOrUser`, `AdminOnly`.
- Admin APIs are JWT admin role-gated (`Transcendence.WebAPI/Controllers/AdminOperationsController.cs`, `Transcendence.WebAPI/Controllers/ProSummonersController.cs`, `Transcendence.WebAPI/Controllers/ApiKeysController.cs`).
- Web app stores tokens in HttpOnly cookies and refreshes server-side (`apps/web/lib/authCookies.ts`, `apps/web/lib/session.ts`, `apps/web/lib/sessionToken.ts`).
- BFF proxy strips browser cookies before forwarding upstream (`apps/web/lib/trnProxy.ts`, `apps/web/app/api/trn/user/[...path]/route.ts`).

## 6. Primary Request/Data Flows
- Summoner profile flow: `Transcendence.WebAPI/Controllers/SummonersController.cs` reads DB-first and returns `202` when refresh is needed.
- Refresh enqueue flow: same controller acquires DB-backed refresh locks then enqueues `ISummonerRefreshJob.RefreshByRiotId`.
- Refresh execution flow: `Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs` fetches Riot data, upserts records, and releases locks.
- Analytics read flow: `Transcendence.WebAPI/Controllers/AnalyticsController.cs` and `ChampionAnalyticsController.cs` call cached analytics service.
- Analytics compute/cache flow: `Transcendence.Service.Core/Services/Analytics/Implementations/ChampionAnalyticsService.cs` delegates compute to `ChampionAnalyticsComputeService` and tags HybridCache entries.
- Admin operations flow: `Transcendence.WebAPI/Controllers/AdminOperationsController.cs` manipulates Hangfire state and writes audit entries through `IAdminAuditService`.

## 7. Background Processing Architecture
- Production schedules patch detection, retries, analytics refresh/ingestion, maintenance, timeline/rune backfills, and live polling (`Transcendence.Service/Workers/ProductionWorker.cs`).
- Development worker intentionally limits recurring scope to analytics-focused jobs (`Transcendence.Service/Workers/DevelopmentWorker.cs`).
- Queue prioritization is explicit via Hangfire queue attributes (`refresh-high` and `refresh-low` in `SummonerRefreshJob.cs`) plus worker server queue order in `Transcendence.Service/Program.cs`.
- Ingestion pauses on active API-priority locks (`Transcendence.Service.Core/Services/Jobs/ChampionAnalyticsIngestionJob.cs`, `LiveGamePollingJob.cs`).

## 8. Persistence and Data Modeling
- Domain model spans auth, summoners, matches, analytics support tables, live game snapshots, and static patch/rune/item versions (`Transcendence.Data/Models/**`).
- Query filters hide permanently unfetchable matches across dependent entities (`Transcendence.Data/TranscendenceContext.cs`).
- Refresh lock semantics are lease-style with atomic SQL upsert and timed release (`Transcendence.Data/Repositories/Implementations/RefreshLockRepository.cs`).
- Summoner upsert is SQL-based for deterministic conflict handling on `Puuid` (`Transcendence.Data/Repositories/Implementations/SummonerRepository.cs`).

## 9. Contract and BFF Boundary
- OpenAPI contract is committed at `openapi/transcendence.v1.json`.
- Contract export pipeline is `scripts/openapi/export.sh`.
- Generated TS schema/client are in `packages/api-client/src/schema.ts` and `packages/api-client/src/index.ts`.
- Next BFF route handlers map web calls to backend auth modes:
  - public passthrough: `apps/web/app/api/trn/public/[...path]/route.ts`
  - user bearer passthrough: `apps/web/app/api/trn/user/[...path]/route.ts`
  - admin bearer passthrough with role checks: `apps/web/app/api/trn/admin/[...path]/route.ts`
  - app-key allowlisted passthrough: `apps/web/app/api/trn/app/[...path]/route.ts`

## 10. Operational and CI Architecture
- CI validates backend tests and web lint/test/build + OpenAPI drift (`.github/workflows/ci-web-backend.yml`).
- Container build/publish is matrix-driven per component (`.github/workflows/docker-images.yml`).
- Service runtime defaults and job tuning are concentrated in `Transcendence.Service/appsettings.json`.

## 11. Planning Hotspots (High-Impact Areas)
- Authentication changes: `Transcendence.WebAPI/Program.cs`, `Transcendence.WebAPI/Security/*`, `Transcendence.Service.Core/Services/Auth/*`, `apps/web/lib/session*.ts`.
- Ingestion/analytics changes: `Transcendence.Service.Core/Services/Jobs/*`, `Transcendence.Service.Core/Services/Analytics/*`, `Transcendence.Service/appsettings*.json`.
- Data model changes: `Transcendence.Data/Models/*`, `Transcendence.Data/TranscendenceContext.cs`, `Transcendence.Service/Migrations/*`.
- API surface changes: `Transcendence.WebAPI/Controllers/*`, `openapi/transcendence.v1.json`, `packages/api-client/src/schema.ts`.
