# Testing Patterns (Quality Focus)

## Scope and evidence
This document summarizes current testing practice from committed tests and CI configuration.
Primary references: `tests/Transcendence.Service.Core.Tests/*`, `tests/Transcendence.WebAPI.Tests/*`, `apps/web/lib/*.test.ts`, `.github/workflows/ci-web-backend.yml`, `package.json`, `docs/DEVELOPMENT.md`.

## Test inventory by area
- Backend domain/service tests: `tests/Transcendence.Service.Core.Tests` (job orchestration, analytics logic, auth service behavior, stats computation).
- Backend API tests: `tests/Transcendence.WebAPI.Tests` (controller behavior and exception-to-ProblemDetails mapping).
- Web tests: utility/unit tests in `apps/web/lib/*.test.ts` (formatting, parsing, proxy path normalization, polling math, analytics sample normalization).
- No dedicated E2E/browser suite is present (`Playwright`/`Cypress` not found in repo scripts/config).

## Frameworks and tooling
- .NET tests use xUnit + FluentAssertions + Moq (`tests/Transcendence.Service.Core.Tests/Transcendence.Service.Core.Tests.csproj`, `tests/Transcendence.WebAPI.Tests/Transcendence.WebAPI.Tests.csproj`).
- Coverlet collector is installed in both .NET test projects, but no enforced coverage threshold is configured.
- Web tests use Vitest in `node` environment (`apps/web/vitest.config.ts`, `apps/web/package.json`).

## Execution paths used by contributors and CI
- Local backend run: `dotnet test tests/Transcendence.Service.Core.Tests` and `dotnet test tests/Transcendence.WebAPI.Tests` (`docs/DEVELOPMENT.md`).
- Monorepo backend run: `pnpm backend:test` executes `dotnet test Transcendence.sln -v minimal` (`package.json`).
- Web run: `pnpm --filter web test` / `pnpm web:test` (`package.json`, `apps/web/package.json`).
- CI gate (`.github/workflows/ci-web-backend.yml`) runs backend tests, `pnpm api:check`, web lint, web tests, and web build.

## Established backend test patterns
- Naming pattern is behavior-focused: `Method_WhenCondition_ExpectedResult` (examples in `tests/Transcendence.WebAPI.Tests/AuthControllerTests.cs` and `tests/Transcendence.Service.Core.Tests/UserAuthServiceTests.cs`).
- Predominant AAA shape with direct assertions on HTTP/action results and domain return values.
- Interaction verification uses Moq `Verify` for side effects (example: refresh token revocation in `UserAuthServiceTests`, lock release in `SummonerRefreshJobTests`).
- Error-path assertions are explicit (`ThrowAsync<T>()` and ProblemDetails body checks in `ApiExceptionHandlerTests`).

## In-memory database harness pattern
- Complex service/job tests use in-memory SQLite, not EF InMemory provider (`Microsoft.Data.Sqlite` + `UseSqlite` in `ChampionAnalyticsServiceTests`, `ChampionAnalyticsIngestionJobRampTests`, `SummonerStatsServiceTests`, `SummonerRefreshJobTests`).
- Each harness opens a connection, calls `EnsureCreatedAsync`, seeds entities, and disposes via `IAsyncDisposable`.
- Tests introduce `TestSqliteTranscendenceContext` overrides for provider-specific defaults (array defaults in `ItemVersion` mapping), keeping runtime behavior close to production schema.

## API/controller test strategy
- Controller tests instantiate controllers directly with mocked dependencies rather than full `WebApplicationFactory` host boot (`tests/Transcendence.WebAPI.Tests/SummonersControllerTests.cs`, `SummonerStatsControllerTests.cs`).
- HTTP surface validation focuses on status/result type and payload mapping, not middleware pipeline integration.
- Exception middleware behavior is tested independently at handler level (`ApiExceptionHandlerTests.cs`).

## Web test strategy
- Test files colocate with utilities (`apps/web/lib/*.test.ts`) and prioritize deterministic pure logic.
- Assertions validate normalization, parsing safety, bounds/clamping, and defensive behavior (e.g., malformed URI decode in `riotid.test.ts`, path traversal rejection in `proxyPath.test.ts`).
- Current suite avoids DOM/render tests and route-handler integration tests; Vitest remains in `node` mode only.

## Quality risks and gaps relevant to planning
- Limited integration coverage between API startup/middleware/auth policies and controllers (unit-style tests dominate).
- No browser/E2E coverage for App Router pages and auth/session flows in `apps/web/app/*`.
- No explicit minimum coverage threshold despite coverlet presence.
- Contract drift is partially mitigated by `pnpm api:check`, but endpoint behavior regressions still rely on targeted unit tests.

## Planning checklist for new work
- Add or update focused unit tests in the matching project (`tests/Transcendence.Service.Core.Tests`, `tests/Transcendence.WebAPI.Tests`, `apps/web/lib/*.test.ts`).
- Prefer SQLite harness for query-heavy/service behavior changes touching EF semantics.
- Include at least one failure-path assertion for new API/service logic.
- Run `pnpm backend:test`, `pnpm web:test`, and `pnpm api:check` before PR finalization.
