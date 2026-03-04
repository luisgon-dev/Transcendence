# Coding Conventions (Quality Focus)

## Scope and evidence
This document captures conventions observed in implementation files, not aspirational style guides.
Primary references: `Transcendence.WebAPI/Program.cs`, `Transcendence.Service/Program.cs`, `Transcendence.Service.Core/Services/Extensions/ServiceCollectionExtensions.cs`, `Transcendence.Data/Repositories/Implementations/SummonerRepository.cs`, `apps/web/eslint.config.mjs`, `apps/web/tsconfig.json`.

## Monorepo and layering conventions
- Backend is split by responsibility: host apps in `Transcendence.WebAPI` and `Transcendence.Service`, core domain logic in `Transcendence.Service.Core`, persistence in `Transcendence.Data`.
- Dependency direction is enforced through project references: hosts -> core/data, core -> data (see `Transcendence.WebAPI/Transcendence.WebAPI.csproj`, `Transcendence.Service/Transcendence.Service.csproj`, `Transcendence.Service.Core/Transcendence.Service.Core.csproj`).
- DI registration is centralized in extension methods (`Transcendence.Data/Extensions/ServiceCollectionExtensions.cs`, `Transcendence.Service.Core/Services/Extensions/ServiceCollectionExtensions.cs`) instead of ad-hoc per feature.

## C# language and structure conventions
- Projects consistently enable nullable reference types and implicit usings (`Transcendence.WebAPI/Transcendence.WebAPI.csproj`, `Transcendence.Service.Core/Transcendence.Service.Core.csproj`, `Transcendence.Data/Transcendence.Data.csproj`).
- File-scoped namespaces are standard (`namespace Transcendence.WebAPI.Controllers;`, `namespace Transcendence.Data.Repositories.Implementations;`).
- Primary constructors are widely used for DI classes and controllers (`Transcendence.WebAPI/Controllers/AuthController.cs`, `Transcendence.Data/Repositories/Implementations/MatchRepository.cs`, `Transcendence.Service.Core/Services/Auth/Implementations/UserAuthService.cs`).
- Async naming is consistent (`*Async` suffix) and cancellation tokens are threaded through method signatures (`CancellationToken ct` / `cancellationToken = default`).

## API/controller conventions
- Controllers use `[ApiController]` + explicit route prefixes (`Transcendence.WebAPI/Controllers/AuthController.cs`, `Transcendence.WebAPI/Controllers/ChampionAnalyticsController.cs`).
- Endpoints typically declare response contracts via `[ProducesResponseType]` and return typed DTOs from `Transcendence.Service.Core.Services.*.Models` namespaces.
- Authorization and rate limiting are declarative and policy-based (`Transcendence.WebAPI/Security/AuthPolicies.cs`, `Transcendence.WebAPI/Program.cs`, `EnableRateLimiting(...)` attributes in controllers).
- Input guard clauses are localized at endpoint/service boundaries (example: champion/role validation in `Transcendence.WebAPI/Controllers/ChampionAnalyticsController.cs`).

## Error handling and observability conventions
- Unhandled exception mapping is centralized through `IExceptionHandler` (`Transcendence.WebAPI/Errors/ApiExceptionHandler.cs`) and wired in startup (`Transcendence.WebAPI/Program.cs`).
- API error payloads are `ProblemDetails`-based, with `traceId` attached for debugging correlation.
- Structured logging uses template messages and contextual fields (request id/path in `ApiExceptionHandler`, auth flow logs in `UserAuthService`).

## Data access and EF conventions
- `Transcendence.Data/TranscendenceContext.cs` is the schema authority: indexes, unique constraints, query filters, and relationships are configured in `OnModelCreating`.
- Read queries prefer `AsNoTracking()` where mutation is not needed (`SummonerRepository.SearchByPrefixAsync`, `MatchRepository.GetExistingMatchIdsAsync`).
- Repository methods normalize user input before lookups (`NormalizeForLookup`/`NormalizeValue` in `SummonerRepository`).
- Performance-sensitive upsert path uses explicit SQL with parameterization, not string interpolation (`SummonerRepository.UpsertSummonerAsync`).

## Web (Next.js/TS) conventions
- TypeScript strict mode is enabled (`apps/web/tsconfig.json` -> `"strict": true`).
- ESM + import alias `@/*` are standard (`apps/web/tsconfig.json`, `apps/web/vitest.config.ts`).
- Server-only helpers are explicitly marked (`apps/web/lib/backendCall.ts` uses `import "server-only"`).
- ESLint uses flat config with Next/React/TS integration, and minimal targeted rule suppressions (`apps/web/eslint.config.mjs`).

## Planning implications for quality work
- Preserve layering boundaries when adding features: controller -> core service -> repository (`Transcendence.WebAPI/Controllers/*`, `Transcendence.Service.Core/Services/*`, `Transcendence.Data/Repositories/*`).
- Extend DI extension methods rather than inlining registrations into `Program.cs`.
- Keep nullability and token propagation intact in new APIs to avoid regressions.
- For schema or query-shape changes, update EF model config first (`Transcendence.Data/TranscendenceContext.cs`) and generate migrations via CLI per repo policy.
