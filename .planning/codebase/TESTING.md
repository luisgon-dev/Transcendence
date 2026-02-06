# Testing Patterns

**Analysis Date:** 2026-01-31

## Test Framework

**Status:** No automated testing framework configured

**Finding:**
- No test projects found in the solution
- No xUnit, NUnit, MSTest, Moq, or FluentAssertions dependencies detected
- No `.csproj` files for tests exist (searched for `*Test*.csproj`, `*test*.csproj`)

**Implication:**
- Codebase currently relies on manual/integration testing
- No unit test infrastructure to support test-driven development
- All code paths must be tested manually or via external integration tests

## Manual Testing Approach

**Current Method:**
- Controllers designed with clear endpoints that accept structured input
- Background jobs (Hangfire) encapsulate business logic for testing via job execution
- Dependency injection enables manual test scenarios by providing mock implementations

**Examples:**
- `SummonersController.cs` (lines 33-69): HTTP endpoints with detailed XML documentation for manual testing
- `SummonerRefreshJob.cs` (lines 20-85): Background job with structured error handling allowing step-by-step execution validation
- Service interfaces (`ISummonerService`, `IMatchRepository`) enable test doubles via constructor injection

## Error Handling for Testing

**Observable Behavior:**
- Controllers return specific HTTP status codes (202 Accepted, 400 Bad Request, 200 OK) for different scenarios
- Logging output includes context prefixes and structured parameters, making test assertions possible
- Lock-based coordination (`RefreshLockRepository`) provides observable state for testing concurrent behavior

**Example from SummonersController.cs:**
```csharp
// Testable via HTTP response inspection
if (!TryParsePlatformRoute(region, out var platform))
    return BadRequest($"Unsupported region '{region}'...");

var summoner = await summonerRepository.FindByRiotIdAsync(...);
if (summoner != null) return Ok(summoner);
```

## Dependency Injection for Testability

**Design Pattern:**
- All service dependencies injected via constructor (primary constructor pattern)
- Repositories expose interfaces (`ISummonerRepository`, `IMatchRepository`) for mockable data access
- Services accept `ILogger<T>` for logging without tight coupling

**Example from SummonerRefreshJob.cs:**
```csharp
public class SummonerRefreshJob(
    ISummonerService summonerService,
    ISummonerRepository summonerRepository,
    IMatchRepository matchRepository,
    IMatchService matchService,
    TranscendenceContext db,
    IRefreshLockRepository refreshLockRepository,
    ILogger<SummonerRefreshJob> logger,
    RiotGamesApi riotGamesApi) : ISummonerRefreshJob
```

Each dependency can be substituted with a test double.

## Async Testing Patterns

**Async Support:**
- All I/O operations use `async`/`await` with `CancellationToken` support
- Repository queries return `Task<T>?` for nullable results
- Services return `Task<IReadOnlyList<T>>` for collection results

**Observable State:**
```csharp
// Service method signature
public async Task<SummonerOverviewStats> GetSummonerOverviewAsync(
  Guid summonerId,
  int recentGamesCount,
  CancellationToken ct)
```

Test can assert on returned `SummonerOverviewStats` record with typed fields (TotalMatches, WinRate, etc.).

## Validation Patterns

**Input Validation:**
- Controllers validate incoming route/query parameters before service calls
- Early return pattern for invalid input

**Example from SummonersController.cs (lines 36-37):**
```csharp
if (!TryParsePlatformRoute(region, out var platform))
    return BadRequest($"Unsupported region '{region}'...");
```

**Service-level Validation:**
- Parameter constraints enforced in service methods
- Example from SummonerStatsService.cs (lines 13, 91, 153):
  ```csharp
  if (recentGamesCount <= 0) recentGamesCount = 20;
  if (pageSize <= 0 || pageSize > 100) pageSize = 20;
  ```

## Query Testing

**EF Core Queries:**
- Repositories use `IQueryable<T>` with LINQ for composable queries
- Query customization via `Func<IQueryable<T>, IQueryable<T>>` includes parameter

**Example from SummonerRepository.cs (lines 22-33):**
```csharp
public async Task<Summoner?> FindByRiotIdAsync(
    string platformRegion,
    string gameName,
    string tagLine,
    Func<IQueryable<Summoner>, IQueryable<Summoner>>? includes = null,
    CancellationToken cancellationToken = default)
{
    IQueryable<Summoner> query = context.Summoners;
    if (includes != null) query = includes(query);
    return await query.FirstOrDefaultAsync(
        x => x.PlatformRegion == platformRegion && x.GameName == gameName && x.TagLine == tagLine,
        cancellationToken);
}
```

Tests can verify:
- Query executes with correct filters
- Includes parameter allows eager-loading association verification (e.g., `.Include(s => s.Ranks)`)

## Record Testing

**Immutable Data Structures:**
- DTOs and response models use `record` keyword (immutable by default)
- Testing assertions can verify record field values

**Example from StatsModels.cs:**
```csharp
public record SummonerOverviewStats(
    Guid SummonerId,
    int TotalMatches,
    int Wins,
    int Losses,
    double WinRate,
    double AvgKills,
    double AvgDeaths,
    double AvgAssists,
    double KdaRatio,
    double AvgCsPerMin,
    double AvgVisionScore,
    double AvgDamageToChamps,
    double AvgGameDurationMin,
    IReadOnlyList<RecentPerformancePoint> RecentPerformance
);
```

Test assertion example (pseudocode):
```csharp
Assert.Equal(10, result.Wins);
Assert.Equal(20, result.TotalMatches);
Assert.Equal(50.0, result.WinRate);
```

## Background Job Testing

**Hangfire Job Structure:**
- Jobs implement interfaces (e.g., `ISummonerRefreshJob`)
- Jobs accept all dependencies via constructor, making them testable
- Job methods can be invoked directly without Hangfire for unit testing

**Example from SummonerRefreshJob.cs (lines 20-85):**
```csharp
public async Task RefreshByRiotId(string gameName, string tagLine, PlatformRoute platformRoute, string lockKey,
    CancellationToken ct = default)
{
    try {
        // Testable: each step can be mocked
        var summoner = await summonerService.GetSummonerByRiotIdAsync(...);
        await summonerRepository.AddOrUpdateSummonerAsync(...);
        // ... more steps
    } catch (Exception ex) {
        logger.LogError(ex, "[Refresh] Error...");
        throw;
    } finally {
        await refreshLockRepository.ReleaseAsync(lockKey, ct);
    }
}
```

Tests can:
1. Mock `ISummonerService` to return test data
2. Mock `IRefreshLockRepository` to verify lock release
3. Capture logged errors via `ILogger<T>` test double
4. Assert exception behavior on service failures

## Logging Assertion

**Structured Logging:**
- `ILogger<T>` injected in all service/job classes
- Logging calls include context prefix and structured parameters
- Can be captured in tests via mock/test double

**Example from SummonerRefreshJob.cs:**
```csharp
logger.LogInformation("[Refresh] Completed refresh for {GameName}#{Tag} on {Platform}", gameName, tagLine, platformRoute);
logger.LogError(ex, "[Refresh] Error refreshing {GameName}#{Tag} on {Platform}", gameName, tagLine, platformRoute);
```

**Test Pattern:** Mock `ILogger<SummonerRefreshJob>` and verify:
- Correct log level called (Info vs Error)
- Parameters match expected values
- Exception is passed in error logs

## Test Data Considerations

**Model Design for Testing:**
- Models use nullable properties for optional fields (e.g., `public string? RiotSummonerId`)
- Records provide immutable test data with positional constructors
- Collections initialized with empty lists (e.g., `public List<Match.Match> Matches { get; } = [];`)

**Example from Summoner.cs:**
```csharp
public Guid Id { get; set; }
public string? RiotSummonerId { get; set; }
public string? SummonerName { get; set; }
public List<Match.Match> Matches { get; } = [];
public ICollection<MatchParticipant> MatchParticipants { get; } = [];
```

Test setup can create: `new Summoner { Id = Guid.NewGuid(), RiotSummonerId = "test123", ... }`

## Where to Add Tests

**When Test Framework Added:**
- Unit tests: `Transcendence.Service.Tests`, `Transcendence.Data.Tests` (alongside implementation projects)
- Test naming: `[ClassName]Tests.cs` (e.g., `SummonerRepositoryTests.cs`)
- Test location: Mirror source structure (e.g., `Transcendence.Data.Tests/Repositories/SummonerRepositoryTests.cs`)

**Priority Areas for Testing:**
1. Repository query logic (`SummonerRepository`, `MatchRepository` in `Transcendence.Data/Repositories/Implementations/`)
2. Business logic services (`SummonerStatsService`, `MatchService` in `Transcendence.Service.Core/Services/`)
3. Background job flow (`SummonerRefreshJob` in `Transcendence.Service.Core/Services/Jobs/`)
4. Controller endpoints (minimal - mostly integration; `SummonersController.cs` in `Transcendence.WebAPI/Controllers/`)

## Coverage Gaps

**Currently Untested:**
- All service logic (no unit tests)
- All repository queries (no data layer tests)
- All background job execution (no job tests)
- All controller endpoints (no integration tests)
- Error handling branches (no exception testing)

**Risk:** Any code change carries risk of runtime failure in production since no automated tests catch regressions.

---

*Testing analysis: 2026-01-31*
