# Coding Conventions

**Analysis Date:** 2026-01-31

## Naming Patterns

**Files:**
- PascalCase for all `.cs` files (e.g., `SummonerRepository.cs`, `SummonersController.cs`)
- File name matches the primary class/record defined within
- Interface files prefixed with `I` (e.g., `ISummonerRepository.cs`)

**Functions/Methods:**
- PascalCase for all public methods (e.g., `GetSummonerByPuuidAsync`, `AddOrUpdateSummonerAsync`)
- Async methods consistently suffixed with `Async` (e.g., `RefreshByRiotIdAsync`, `GetRecentMatchesAsync`)
- Private methods use same PascalCase convention
- Helper/utility functions lowercase and explicitly named (e.g., `TryParsePlatformRoute`, `NormalizePatch`)

**Variables:**
- camelCase for local variables and parameters (e.g., `summonerId`, `platformRoute`, `cancellationToken`)
- camelCase for private fields (e.g., `lockKey`, `patch`)
- Null-coalescing operator commonly used for optional references

**Types:**
- PascalCase for classes and records (e.g., `Summoner`, `SummonerOverviewStats`)
- PascalCase for enums and enum values (e.g., `PlatformRoute`, `Queue.SUMMONERS_RIFT_5V5_RANKED_SOLO`)
- Records used extensively for immutable DTOs and data structures (e.g., `PagedResult<T>`, `ChampionStat`)
- Required fields in models marked with `required` keyword (e.g., `public required string? PlatformRegion`)

## Code Style

**Formatting:**
- Project uses standard C# conventions with `ImplicitUsings` and `Nullable` enabled globally
- No explicit code formatter configured (no .editorconfig found)
- Global usings defined in project files (e.g., `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`)

**Linting:**
- No explicit linting rules configured (no .eslintrc, .stylecop, or .ruleset files)
- Nullable reference types enabled (`<Nullable>enable</Nullable>`)
- Uses ReSharper/Rider for IDE-level code inspection

**Language Features:**
- Target framework: .NET 10.0 with default LangVersion (latest)
- Uses modern C# features: records, nullable reference types, implicit usings, primary constructors
- Constructor injection via primary constructors (e.g., `public class SummonerService(RiotGamesApi riotApi, IRankService rankService)`)

## Import Organization

**Order:**
1. External NuGet packages (`using Camille.Enums; using Hangfire;`)
2. Microsoft/framework libraries (`using Microsoft.EntityFrameworkCore;`)
3. Project internal namespaces (`using Transcendence.Data; using Transcendence.Service.Core;`)

**Aliases:**
- Type aliases used for disambiguation (e.g., `using DataMatch = Match;` in MatchService.cs to separate API Match from domain Match)
- Fully qualified names used when ambiguity cannot be resolved otherwise

## Error Handling

**Patterns:**
- **Try-Catch-Finally for resource cleanup:** Used in background jobs to ensure lock release
  ```csharp
  try {
    // main logic
  } catch (Exception ex) {
    logger.LogError(ex, "[Context] Error message {Param}", variable);
    throw;
  } finally {
    // cleanup
  }
  ```

- **Early returns for validation:** Controllers validate input before proceeding (e.g., `if (!TryParsePlatformRoute(...)) return BadRequest(...)`)

- **Null coalescing in queries:** Used in repository/service layers
  ```csharp
  var total = aggregate?.Total ?? 0;
  var wins = aggregate?.Wins ?? 0;
  ```

- **Null checks with logging:** Services log warnings for unexpected null returns from external APIs
  ```csharp
  if (matchDto == null) {
    logger.LogWarning("Riot API returned null for match {MatchId}", matchId);
    return null;
  }
  ```

- **Custom lock acquisition pattern:** RefreshLockRepository provides `TryAcquireAsync` and `ReleaseAsync` for distributed locking

**No exceptions thrown for normal flow:**
- Controllers return HTTP status codes (202 Accepted, 400 Bad Request) rather than throwing
- Jobs may rethrow after logging for Hangfire retry behavior

## Logging

**Framework:** ILogger<T> from Microsoft.Extensions.Logging (injected via DI)

**Patterns:**
- Structured logging with named parameters using format strings
- Log level conventions:
  - `LogInformation`: Successful operations and important milestones (e.g., "Completed refresh for {GameName}#{Tag}")
  - `LogWarning`: Unexpected but recoverable conditions (e.g., "Riot API returned null for match {MatchId}")
  - `LogError`: Failures including full exception (e.g., `logger.LogError(ex, "[Context] Message", params)`)

- **Context prefix convention:** Log messages prefixed with context in brackets (e.g., `[Refresh]`, `[Summoner]`) for easy filtering

- **Example:**
  ```csharp
  logger.LogInformation("[Refresh] Completed refresh for {GameName}#{Tag} on {Platform}", gameName, tagLine, platformRoute);
  logger.LogError(ex, "[Refresh] Error refreshing {GameName}#{Tag} on {Platform}", gameName, tagLine, platformRoute);
  ```

## Comments

**When to Comment:**
- File header comments with class name (e.g., `// SummonerRepository.cs`)
- Inline comments for non-obvious logic or workarounds
- Comments explaining why, not what code does

**JSDoc/Documentation Comments:**
- XML documentation (`///`) used on public API methods and classes
- Swagger/OpenAPI attributes on controller methods with `<summary>`, `<param>`, `<remarks>`
- Example from `SummonersController.cs`:
  ```csharp
  /// <summary>
  ///     Get summoner information by Riot ID (gameName and tagLine) and platform region (e.g., NA1, EUW1).
  ///     This endpoint reads from the database only. If the summoner is not found, a background refresh will be required.
  /// </summary>
  /// <param name="region">Platform route like NA1, EUW1, etc.</param>
  /// <param name="name">Riot game name (without #tag)</param>
  /// <param name="tag">Riot tag (without #)</param>
  ```

## Function Design

**Size:** Functions vary from small helpers (5-10 lines) to moderate services (50-100 lines). Most service methods stay focused on a single responsibility.

**Parameters:**
- Explicit individual parameters for simple inputs
- Func<> delegates for EF Core query customization (e.g., `Func<IQueryable<T>, IQueryable<T>>? includes = null`)
- CancellationToken consistently placed as last parameter in async methods
- Primary constructors used for dependency injection (all dependencies visible in constructor)

**Return Values:**
- Async methods return `Task<T>` for operations that produce values
- Nullable return types used where queries might not find results (e.g., `Task<Summoner?>`)
- Records and DTOs immutable by design (using `record` types)
- Collections return `IReadOnlyList<T>` or `List<T>` depending on mutability needs
- HTTP controllers return `Task<IActionResult>` for flexible response handling

**Example from SummonerStatsService.cs:**
```csharp
public async Task<SummonerOverviewStats> GetSummonerOverviewAsync(
  Guid summonerId,
  int recentGamesCount,
  CancellationToken ct)
{
  // Parameter validation
  if (recentGamesCount <= 0) recentGamesCount = 20;

  // Single responsibility: query + aggregate + calculate + return
}
```

## Module Design

**Exports:**
- Services exposed via interfaces (e.g., `ISummonerRepository`, `ISummonerService`)
- Implementation classes have single interface contract
- Repositories implement thin data access layers with query builders

**Barrel Files:**
- No barrel/index files used (no `index.cs` pattern)
- Each file has single primary type
- Namespaces directly reflect folder structure (e.g., `Transcendence.Data.Repositories.Interfaces`)

**Dependency Injection:**
- Extension methods on `IServiceCollection` for service registration
- `AddTranscendenceCore()` and `AddTranscendenceRiot()` extension methods in `ServiceCollectionExtensions.cs`
- Scoped lifetime default for repository/service classes
- Singleton for external API client (RiotGamesApi)

**Example:**
```csharp
// In Transcendence.Service.Core/Services/Extensions/ServiceCollectionExtensions.cs
public static IServiceCollection AddTranscendenceCore(this IServiceCollection services)
{
  services.AddScoped<IChampionLoadoutAnalysisService, ChampionLoadoutAnalysisService>();
  services.AddScoped<ISummonerStatsService, SummonerStatsService>();
  return services;
}
```

---

*Convention analysis: 2026-01-31*
