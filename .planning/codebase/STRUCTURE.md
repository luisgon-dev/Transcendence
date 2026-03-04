# Codebase Structure

## 1. Repository Root (Top-Level Map)
- Solution and backend root: `Transcendence.sln`, `global.json`.
- Backend host projects: `Transcendence.WebAPI`, `Transcendence.Service`, `Transcendence.WebAdminPortal`.
- Shared backend libraries: `Transcendence.Service.Core`, `Transcendence.Data`.
- Frontend app: `apps/web`.
- Generated API client package: `packages/api-client`.
- API contract: `openapi/transcendence.v1.json`.
- Environment/deployment configs: `docker-compose.yml`, `docker-compose.production.yml`, `.env`, `.env.production.example`.
- Team docs: `docs/DEVELOPMENT.md`, `docs/API.md`, `docs/ARCHITECTURE.md`.

## 2. .NET Solution Composition
- Registered projects are declared in `Transcendence.sln`.
- Data project: `Transcendence.Data/Transcendence.Data.csproj`.
- Core services project: `Transcendence.Service.Core/Transcendence.Service.Core.csproj`.
- API host: `Transcendence.WebAPI/Transcendence.WebAPI.csproj`.
- Worker host: `Transcendence.Service/Transcendence.Service.csproj`.
- Dashboard host: `Transcendence.WebAdminPortal/Transcendence.WebAdminPortal.csproj`.
- Test projects: `tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj`, `tests/Transcendence.WebAPI.Tests/Transcendence.WebAPI.Tests.csproj`.

## 3. Backend Folder Layout
- `Transcendence.Data/Models`
  - `Auth/` contains user/accounts/roles/tokens/audit entities.
  - `LoL/Account` and `LoL/Match` contain summoner/match domain entities.
  - `LiveGame/` and `Service/` contain live polling and operational tables.
- `Transcendence.Data/Repositories`
  - `Interfaces/` defines repository contracts.
  - `Implementations/` holds EF/SQL data access implementations.
- `Transcendence.Service.Core/Services`
  - `Analysis/` for per-summoner stats and multi-search.
  - `Analytics/` for champion-tier/build/matchup/pro-build computations.
  - `Auth/` for JWT, API keys, user auth, admin audit/bootstrap.
  - `Jobs/` for Hangfire job logic and scheduling option DTOs.
  - `LiveGame/`, `RiotApi/`, `StaticData/`, `Cache/`, and `Extensions/`.
- `Transcendence.WebAPI`
  - `Controllers/` owns HTTP endpoints by domain.
  - `Security/` owns auth schemes/policies/OpenAPI auth operation filter.
  - `Errors/ApiExceptionHandler.cs` centralizes ProblemDetails exception mapping.
- `Transcendence.Service`
  - `Workers/ProductionWorker.cs` and `Workers/DevelopmentWorker.cs` schedule recurring/startup jobs.
  - `Migrations/` contains EF migration history.

## 4. Frontend Folder Layout (`apps/web`)
- App Router pages and route handlers live in `apps/web/app`.
- BFF routes are segmented by auth mode in `apps/web/app/api/trn/*`.
- Session endpoints are in `apps/web/app/api/session/*`.
- Static-game-data routes are in `apps/web/app/api/static/*`.
- Admin pages and server actions are under `apps/web/app/admin/*`.
- Shared server/browser helpers live in `apps/web/lib`.
- Reusable UI components live in `apps/web/components` and `apps/web/components/ui`.
- Framework config lives in `apps/web/next.config.mjs`, `apps/web/tsconfig.json`, `apps/web/vitest.config.ts`, `apps/web/eslint.config.mjs`.

## 5. API Contract and Client Generation
- Canonical spec file: `openapi/transcendence.v1.json`.
- Export script: `scripts/openapi/export.sh`.
- Generated schema source: `packages/api-client/src/schema.ts`.
- Typed client entrypoint: `packages/api-client/src/index.ts`.
- Package build config: `packages/api-client/package.json`.

## 6. Tests and Quality Layout
- Backend service tests: `tests/Transcendence.Service.Core.Tests/*`.
- API controller/exception tests: `tests/Transcendence.WebAPI.Tests/*`.
- Frontend unit tests are colocated in `apps/web/lib/*.test.ts`.
- CI pipeline definitions live in `.github/workflows/ci-web-backend.yml` and `.github/workflows/docker-images.yml`.

## 7. Configuration Surfaces
- API runtime defaults: `Transcendence.WebAPI/appsettings.json` and `Transcendence.WebAPI/appsettings.Development.json`.
- Worker schedules/tuning: `Transcendence.Service/appsettings.json` and `Transcendence.Service/appsettings.Development.json`.
- Admin portal settings: `Transcendence.WebAdminPortal/appsettings.json`.
- JS workspace config: `package.json`, `pnpm-workspace.yaml`, `pnpm-lock.yaml`, `.nvmrc`.

## 8. Important Generated/Build Artifacts (Do Not Plan Against)
- .NET build outputs: `**/bin/**`, `**/obj/**`.
- Next build outputs: `apps/web/.next/**`.
- Workspace dependencies: `node_modules/**`.
- Planning should target source folders listed above, not generated artifact paths.

## 9. Structural Entry Points for Future Phases
- New backend endpoints: start in `Transcendence.WebAPI/Controllers`, then `Transcendence.Service.Core/Services`, then `Transcendence.Data/Repositories`.
- New background workflows: start in `Transcendence.Service.Core/Services/Jobs`, then schedule in `Transcendence.Service/Workers/*.cs` and `Transcendence.Service/appsettings*.json`.
- New web data interactions: start in `apps/web/app/api/trn/*` + `apps/web/lib/*`, then page/component in `apps/web/app` and `apps/web/components`.
- Contract changes: update `openapi/transcendence.v1.json` then regenerate `packages/api-client/src/schema.ts`.
