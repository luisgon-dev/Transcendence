# STACK

## Scope
- Focus: technology stack inventory for this repository (backend, web, infra, tooling).
- Primary solution roots: `Transcendence.sln`, `apps/web`, `packages/api-client`.

## Monorepo Composition
- .NET services and libraries: `Transcendence.WebAPI`, `Transcendence.Service`, `Transcendence.Service.Core`, `Transcendence.Data`, `Transcendence.WebAdminPortal` (`Transcendence.sln`).
- Frontend app: `apps/web` (Next.js App Router) (`apps/web/package.json`).
- Generated TS API client package: `packages/api-client` (`packages/api-client/package.json`).
- OpenAPI contract committed in-repo: `openapi/transcendence.v1.json`.

## Runtime Languages and SDKs
- C# / .NET 10 target framework across backend projects (`Transcendence.WebAPI/Transcendence.WebAPI.csproj`, `Transcendence.Service/Transcendence.Service.csproj`, `Transcendence.Service.Core/Transcendence.Service.Core.csproj`, `Transcendence.Data/Transcendence.Data.csproj`).
- .NET SDK pinned via `global.json` (`10.0.102`, roll-forward latest major).
- TypeScript + React stack on web side (`apps/web/package.json`, `packages/api-client/package.json`).
- Node.js 22 container runtime for web image (`apps/web/Dockerfile`), `.nvmrc` used by CI (`.github/workflows/ci-web-backend.yml`).

## Backend Framework and Libraries
- ASP.NET Core Web API host (`Transcendence.WebAPI/Program.cs`).
- Worker host using .NET Worker SDK (`Transcendence.Service/Transcendence.Service.csproj`, `Transcendence.Service/Program.cs`).
- Hangfire for background job orchestration and storage (`Transcendence.Service/Program.cs`, `Transcendence.WebAPI/Program.cs`, `Transcendence.WebAdminPortal/Program.cs`).
- EF Core + Npgsql provider for PostgreSQL persistence (`Transcendence.Data/Transcendence.Data.csproj`, `Transcendence.WebAPI/Program.cs`, `Transcendence.Service/Program.cs`).
- Auth stack: JWT Bearer + custom API key scheme/policies (`Transcendence.WebAPI/Program.cs`, `Transcendence.WebAPI/Security/*`).
- API docs stack: Swashbuckle + Microsoft.OpenApi (`Transcendence.WebAPI/Transcendence.WebAPI.csproj`, `Transcendence.WebAPI/Program.cs`).
- Caching stack: HybridCache + StackExchange Redis distributed cache (`Transcendence.WebAPI/Program.cs`, `Transcendence.Service/Program.cs`, `Transcendence.Service.Core/Transcendence.Service.Core.csproj`).

## Frontend Framework and Libraries
- Next.js App Router server-rendered web app (`apps/web/package.json`, `apps/web/app/*`).
- React 19 + React DOM 19 (`apps/web/package.json`).
- Styling/utilities: Tailwind CSS, PostCSS, clsx, tailwind-merge (`apps/web/package.json`, `apps/web/lib/cn.ts`).
- Motion/UI tooling: framer-motion, cmdk (`apps/web/package.json`).
- BFF/proxy layer implemented with route handlers (`apps/web/app/api/trn/*`, `apps/web/lib/trnProxy.ts`).

## Data, Cache, and Messaging Substrate
- Primary OLTP store: PostgreSQL 16 in dev compose (`docker-compose.yml`) and production compose (`docker-compose.production.yml`).
- Cache store: Redis 7 (`docker-compose.yml`, `docker-compose.production.yml`).
- Hangfire persistence on PostgreSQL via `Hangfire.PostgreSql` (`Transcendence.Service/Transcendence.Service.csproj`, `Transcendence.WebAPI/Transcendence.WebAPI.csproj`).
- No separate event bus/broker detected; job dispatch is Hangfire queue-based (`Transcendence.Service/Program.cs`, `Transcendence.Service/Workers/*`).

## API Contract and Client Generation
- OpenAPI source-of-truth committed file: `openapi/transcendence.v1.json`.
- Export pipeline script boots WebAPI and downloads swagger JSON (`scripts/openapi/export.sh`).
- TS schema/client generation via `openapi-typescript`, `openapi-fetch`, `tsup` (`packages/api-client/package.json`, `packages/api-client/src/index.ts`).
- Root scripts wire full generation/check workflow (`package.json` scripts: `api:spec`, `api:client`, `api:gen`, `api:check`).

## Build, Test, and CI Stack
- Backend test framework stack: xUnit + Moq + FluentAssertions + coverlet (`tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj`, `tests/Transcendence.WebAPI.Tests/Transcendence.WebAPI.Tests.csproj`).
- Web test/lint/build stack: Vitest + ESLint + Next build (`apps/web/package.json`).
- Monorepo package manager: pnpm workspace (`package.json`, `pnpm-workspace.yaml`).
- CI workflow runs backend tests + OpenAPI/client consistency + web lint/test/build (`.github/workflows/ci-web-backend.yml`).

## Containerization and Delivery
- .NET services use multi-stage Docker builds on `mcr.microsoft.com/dotnet/*:10.0` (`Transcendence.WebAPI/Dockerfile`, `Transcendence.Service/Dockerfile`, `Transcendence.WebAdminPortal/Dockerfile`).
- Web app uses multi-stage Node 22 slim image (`apps/web/Dockerfile`).
- Docker image automation and GHCR publishing/signing in CI (`.github/workflows/docker-images.yml`).
- Production compose deploy references GHCR images for web/webapi/service/webadmin (`docker-compose.production.yml`).

## Planning Notes
- Stack is intentionally split into API host, worker host, and web BFF, which enables independent scaling by concern (`Transcendence.WebAPI/Program.cs`, `Transcendence.Service/Program.cs`, `apps/web/app/api/trn/*`).
- Contract-first discipline is present (committed OpenAPI + generated client + CI check), reducing frontend/backend drift (`openapi/transcendence.v1.json`, `package.json`, `.github/workflows/ci-web-backend.yml`).
