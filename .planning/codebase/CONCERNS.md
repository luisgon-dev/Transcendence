# Codebase Concerns

**Analysis Date:** 2026-01-31

## Tech Debt

**Inefficient Match Fetch Strategy:**
- Issue: `FetchLatestMatchInformation.cs` fetches matches for ALL summoners in the database every execution without intelligent filtering
- Files: `Transcendence.Service.Core/Services/Jobs/FetchLatestMatchInformation.cs`
- Impact: As the summoner database grows, this job will perform increasingly expensive full-table scans and redundant API calls for inactive/low-elo summoners. Will hit Riot API rate limits at scale.
- Fix approach: Implement last-updated tracking (e.g., `LastMatchFetchedAt` field on `Summoner` model). Prioritize summoners based on (a) time since last update, (b) ELO/rank tier, (c) activity status. Add migration to add this field.

**SaveChanges Called Multiple Times in Tight Loops:**
- Issue: `FetchLatestMatchInformation.cs` calls `context.SaveChangesAsync(stoppingToken)` inside a per-match loop (line 69), and `SummonerRefreshJob.cs` does the same (line 56)
- Files: `Transcendence.Service.Core/Services/Jobs/FetchLatestMatchInformation.cs` (lines 66-69), `Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs` (line 56)
- Impact: Each match persistence creates a separate database round-trip. For 20 matches, this is 20 separate transactions instead of one batch. Performance degrades linearly with match count; poor scalability.
- Fix approach: Accumulate matches into a collection and persist once after all matches are added to context. Use `context.SaveChangesAsync()` once per summoner refresh, not per match.

**Race Condition in RefreshLock Acquisition:**
- Issue: `RefreshLockRepository.TryAcquireAsync()` uses DbUpdateException to detect concurrent lock attempts, but concurrent requests could still slip through between FirstOrDefaultAsync check and Add operation
- Files: `Transcendence.Data/Repositories/Implementations/RefreshLockRepository.cs` (lines 15-36)
- Impact: Under very high concurrency (many simultaneous refresh requests for same summoner), multiple jobs could acquire the lock simultaneously, leading to duplicate API calls and wasted quota.
- Fix approach: Use database-level locking (SELECT FOR UPDATE in raw SQL) or implement optimistic concurrency with row version fields. Current approach relies on index violation catching exceptions, which works but is fragile.

**Match Participant Query Loops with Individual Queries:**
- Issue: `SummonerRefreshJob.RefreshByRiotId()` and `FetchLatestMatchInformation.Execute()` check each match ID individually via `matchRepository.GetMatchByIdAsync(id)` in a loop (line 50 in FetchLatestMatchInformation, line 40 in SummonerRefreshJob)
- Files: `Transcendence.Service.Core/Services/Jobs/FetchLatestMatchInformation.cs` (lines 48-52), `Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs` (lines 38-42)
- Impact: For 20 match IDs, this performs 20 individual database queries. Should use a single `IN` clause query to batch check existence.
- Fix approach: Change to: `var existingIds = await matchRepository.GetMatchIdsByAsync(matchIds, ct)` returning a HashSet, then filter totalMatchList once.

**No Validation on Critical API Responses:**
- Issue: `StaticDataService.FetchItemsForPatchAsync()` and `FetchRunesForPatchAsync()` deserialize JSON without null checks on the result before using it (lines 95, 115-119)
- Files: `Transcendence.Service.Core/Services/StaticData/Implementations/StaticDataService.cs` (lines 95-104, 115-122)
- Impact: Malformed responses from community dragon endpoints (network corruption, API changes) could result in NullReferenceException or silent data loss. No error logging or recovery.
- Fix approach: Add explicit null checks and log warnings if deserialization returns null or empty collections. Consider retry logic for external API calls.

**String Parsing Without Error Handling:**
- Issue: `FetchLatestMatchInformation.Execute()` parses platform region via `Enum.Parse()` without try-catch (line 32)
- Files: `Transcendence.Service.Core/Services/Jobs/FetchLatestMatchInformation.cs` (line 32)
- Impact: Corrupted data in `Summoner.PlatformRegion` field could crash the entire job instead of logging and skipping that summoner.
- Fix approach: Wrap in try-catch and log errors. Use Enum.TryParse() instead for graceful degradation.

**Orphaned WebAdminPortal Project:**
- Issue: `Transcendence.WebAdminPortal` project exists but is mentioned in README roadmap as "Build out WebAdminPortal for data management" (not yet implemented)
- Files: `Transcendence.WebAdminPortal/` directory, `README.md` line 123
- Impact: Dead code in repository that may collect technical debt or confuse developers about project scope.
- Fix approach: Remove the project or clearly document its current status and purpose.

## Known Bugs

**JSON Deserialization Case Sensitivity:**
- Symptoms: Community Dragon item data may fail to deserialize correctly if property name casing differs
- Files: `Transcendence.Service.Core/Services/StaticData/Implementations/StaticDataService.cs` (line 115-119)
- Trigger: When Community Dragon API response for items has different property casing than expected
- Workaround: `PropertyNameCaseInsensitive = true` is already set for ItemVersion deserialization (line 118), but RuneVersion uses default case-sensitive parsing (line 95). Inconsistent handling.

**Premature Match Release on Fetch Failure:**
- Symptoms: If a match fails to fetch mid-job, the summoner still completes refresh but reports success despite incomplete data
- Files: `Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs` (lines 44-62)
- Trigger: External API error or timeout on any individual match during refresh
- Workaround: Job logs errors but continues. Client won't know a partial refresh occurred.

## Security Considerations

**Riot API Key Exposure via AppSettings:**
- Risk: `appsettings.json` contains placeholder for Riot API key in `ConnectionStrings:RiotApi` (line 11)
- Files: `Transcendence.Service/appsettings.json`, `Transcendence.WebAPI/appsettings.json`
- Current mitigation: Project uses user secrets for development (documented in README lines 32-44). Appsettings is a template. Deployment uses environment variables or secrets management.
- Recommendations: (1) Ensure CI/CD never exposes appsettings.Development.json in production builds (already configured line 34-36 in WebAPI csproj). (2) Add pre-commit hook to prevent committing populated connection strings. (3) Consider rotating Riot API keys periodically.

**No Input Validation on Region Parameter:**
- Risk: `SummonersController.GetByRiotId()` accepts region string but relies on `TryParsePlatformRoute()` for validation (lines 36-37)
- Files: `Transcendence.WebAPI/Controllers/SummonersController.cs` (lines 124-150)
- Current mitigation: TryParsePlatformRoute validates and returns false for invalid input. BadRequest response is returned.
- Recommendations: Ensure all route parameters are validated consistently. Consider using ASP.NET model binding with explicit enum types for stronger typing.

**No Rate Limiting on Public Endpoints:**
- Risk: `/api/summoners/{region}/{name}/{tag}/refresh` endpoint can be called repeatedly by any client without rate limiting
- Files: `Transcendence.WebAPI/Controllers/SummonersController.cs` (lines 74-115)
- Current mitigation: RefreshLock prevents duplicate concurrent refreshes for same summoner (5-minute TTL). But different summoners can hammer the API, queuing Hangfire jobs indefinitely.
- Recommendations: (1) Implement IP-based or API key-based rate limiting. (2) Add queue depth limits in Hangfire configuration. (3) Log and alert on unusual refresh patterns.

**Unvalidated External API Responses:**
- Risk: Community Dragon and Data Dragon responses are deserialized and stored without content validation
- Files: `Transcendence.Service.Core/Services/StaticData/Implementations/StaticDataService.cs` (lines 73-84, 86-104, 106-132)
- Current mitigation: Deserializer uses type safety (List<CommunityDragonRune>, etc.). Invalid JSON would fail to deserialize.
- Recommendations: (1) Add explicit validation of critical fields (e.g., non-zero IDs). (2) Hash/checksum responses to detect tampering. (3) Implement response size limits to prevent DoS via oversized payloads.

## Performance Bottlenecks

**N+1 Query Pattern in Stats Calculation:**
- Problem: `SummonerStatsService.GetSummonerOverviewAsync()` loads all match participants for a summoner, then calculates aggregates via LINQ-to-Objects
- Files: `Transcendence.Service.Core/Services/Analysis/Implementations/SummonerStatsService.cs` (lines 15-87)
- Cause: The second query at line 52-69 uses `.Take(recentGamesCount)` which executes a separate query. For large match counts, this could load thousands of rows just to get recent 20.
- Improvement path: (1) Combine the two queries into a single LINQ query that computes both aggregate and recent stats simultaneously. (2) Add database-level indexes on (SummonerId, MatchDate) to speed ordering and filtering. (3) Consider materializing materialized view for frequently-accessed summoner stats.

**Champion Stats Query Materializes Large Result Sets:**
- Problem: `GetChampionStatsAsync()` groups all matches by champion, then projects and sorts in memory before taking top N
- Files: `Transcendence.Service.Core/Services/Analysis/Implementations/SummonerStatsService.cs` (lines 89-128)
- Cause: GroupBy happens in SQL, but selection of all 10 average metrics per champion loads entire grouped dataset before top-10 filtering.
- Improvement path: (1) Move OrderByDescending(x => x.Games).Take(top) earlier to SQL layer. (2) Use DENSE_RANK window function in SQL if summoner has 1000+ champions (unlikely but safer). (3) Cache champion stats if query frequency is high.

**Synchronous SaveChangesAsync Blocking:**
- Problem: Jobs call `await context.SaveChangesAsync()` sequentially; no parallelization of independent operations
- Files: `Transcendence.Service.Core/Services/Jobs/FetchLatestMatchInformation.cs` (line 69), `Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs` (line 56)
- Cause: Database transactions serialize writes. Cannot parallelize match inserts.
- Improvement path: (1) Batch inserts into chunks (e.g., 5 matches per SaveChangesAsync) to reduce round-trips while staying within memory limits. (2) Use parallel match fetches from Riot API with sequential persistence (fetch in parallel, persist sequentially to avoid transaction conflicts).

**Inefficient Lock Polling:**
- Problem: `DevelopmentWorker.ExecuteAsync()` completes immediately after enqueueing startup job, but `ProductionWorker.ExecuteAsync()` sleeps for 1 minute in a no-op loop (line 21)
- Files: `Transcendence.Service/Workers/ProductionWorker.cs` (lines 17-22)
- Cause: The while loop serves no purpose other than keeping the host alive; the actual recurring job is managed by Hangfire.
- Improvement path: Use `Task.Delay(Timeout.Infinite)` to sleep indefinitely, or remove the loop entirely and let Hangfire manage all scheduling.

## Fragile Areas

**Enum Casting from User-Controlled Data:**
- Files: `Transcendence.Service.Core/Services/Jobs/FetchLatestMatchInformation.cs` (line 32)
- Why fragile: `Enum.Parse(typeof(PlatformRoute), summoner.PlatformRegion!)` assumes data in database is always valid. If migrations or external data corruption introduce invalid values, this crashes.
- Safe modification: (1) Use Enum.TryParse() with fallback logging. (2) Add database constraint to ensure PlatformRegion only contains valid enum values. (3) Unit test with invalid enum values.
- Test coverage: No unit tests found for Jobs namespace. Untested.

**Hard-Coded Patch Normalization Logic:**
- Files: `Transcendence.Service.Core/Services/RiotApi/Implementations/MatchService.cs` (lines 108-114)
- Why fragile: `NormalizePatch()` assumes all version strings follow "X.Y.Z..." format. Future API changes or edge cases (e.g., "2024-01") could break.
- Safe modification: (1) Add regex validation before parsing. (2) Log all patches that fail normalization. (3) Add unit tests for edge cases (null, empty, non-numeric, etc.).
- Test coverage: No unit tests for this utility function.

**Unguarded Community Dragon HTTP Calls:**
- Files: `Transcendence.Service.Core/Services/StaticData/Implementations/StaticDataService.cs` (lines 76, 90-91, 110-111)
- Why fragile: All HTTP calls use `response.EnsureSuccessStatusCode()` which throws on 4xx/5xx. No retry logic, timeout handling, or graceful degradation. If CDG is down, entire static data refresh fails.
- Safe modification: (1) Add HttpClient timeout configuration. (2) Implement exponential backoff retry policy (Polly). (3) Log warnings but continue if CDG calls fail; use cached data as fallback. (4) Add circuit breaker to avoid hammering failed endpoint.
- Test coverage: No unit tests for StaticDataService; cannot mock external calls.

**Loose Concurrency Control in RefreshLock:**
- Files: `Transcendence.Data/Repositories/Implementations/RefreshLockRepository.cs`
- Why fragile: Exception-based concurrency control is implicit and hard to reason about. Future developers may not understand that DbUpdateException is intentional.
- Safe modification: (1) Switch to database-level locking (SERIALIZABLE isolation or row-level locks). (2) Add inline comments explaining the concurrency strategy. (3) Add integration tests that verify lock behavior under concurrent requests.
- Test coverage: No unit/integration tests for RefreshLockRepository.

## Scaling Limits

**Single Hangfire Server:**
- Current capacity: Processes jobs sequentially. One FetchLatestMatchInformation job blocks other jobs (even unrelated summoner refreshes).
- Limit: If there are N summoners, a full match fetch takes N * (20 API calls + DB writes). With Riot API rate limiting (20 requests/second), this could take 10+ minutes for 1000 summoners. During that time, other jobs queue.
- Scaling path: (1) Shard summoners across multiple Hangfire servers. (2) Split FetchLatestMatchInformation into per-summoner jobs enqueued in parallel. (3) Implement job priority tiers (high-elo summoners first).

**Database Connection Pool Saturation:**
- Current capacity: Npgsql connection pool default is 30 connections.
- Limit: If parallel jobs all hit SaveChangesAsync simultaneously, pool could exhaust, causing request timeouts.
- Scaling path: (1) Monitor connection pool usage (add Datadog/AppInsights telemetry). (2) Increase pool size conservatively (benchmarking needed). (3) Use connection pooling middleware (PgBouncer) for shared pool across services.

**Match Table Growth Without Archiving:**
- Current capacity: Match table grows unbounded. No retention policy or archiving strategy documented.
- Limit: After 1 year at 20 matches/summoner/day with 10k summoners: 73M+ match records. Queries slow down, index fragmentation increases.
- Scaling path: (1) Implement time-based partitioning on Match.MatchDate. (2) Archive matches older than 1 year to cold storage (S3). (3) Add scheduled maintenance jobs to rebuild indexes and analyze table stats.

**API Rate Limiting Not Implemented:**
- Current capacity: Any client can queue unlimited refresh requests.
- Limit: Malicious actor could queue 1000s of jobs, exhausting Hangfire queue and Riot API quota.
- Scaling path: (1) Implement sliding window rate limiting per IP/API key (e.g., 10 requests/minute). (2) Use Hangfire job throttling to limit concurrent match fetches. (3) Monitor Riot API quota usage and alert when approaching limits.

## Dependencies at Risk

**Camille Library (Riot API SDK):**
- Risk: Camille is a community-maintained SDK for Riot API. If maintainer stops updating, it may become incompatible with future API versions.
- Impact: If Riot API changes and Camille is not updated, match/summoner fetch jobs break silently (or with obscure deserialization errors).
- Migration plan: (1) Monitor Camille GitHub issues/releases quarterly. (2) If dormant >6 months, evaluate alternative SDKs (e.g., Orianna, official SDKs if available). (3) Maintain abstraction layer in `Transcendence.Service.Core/Services/RiotApi/` so SDK swap is isolated.

**Community Dragon Data Source:**
- Risk: Community Dragon (CDG) is fan-maintained, no SLA. Could go offline permanently.
- Impact: Static data (runes, items) fetch fails, breaking match enrichment. No fallback data source.
- Migration plan: (1) Implement local copy of static data as fallback (pre-populate database on first run). (2) Add health checks for CDG endpoint; alert if unreachable. (3) Consider scraping official Riot CDG or maintaining own static data mirror.

**Hangfire PostgreSQL Storage:**
- Risk: Hangfire.PostgreSql is community-maintained. Database schema could change incompatibly.
- Impact: Job persistence breaks; queued jobs lost on upgrade.
- Migration plan: (1) Always test Hangfire/PostgreSQL versions together before upgrading production. (2) Implement job replay mechanism: log all enqueued jobs to separate audit table, allow manual re-enqueue if Hangfire storage corrupts. (3) Monitor Hangfire release notes for breaking changes.

## Missing Critical Features

**No Observability/Monitoring:**
- Problem: No structured logging, tracing, or metrics collection. Errors are logged to console only. No visibility into job success rates, API latency, database performance.
- Blocks: Can't diagnose why refresh jobs fail. Can't detect memory leaks or connection pool exhaustion until production outage.
- Roadmap impact: Should add ELK stack (Elasticsearch/Logstash/Kibana) or cloud alternative (CloudWatch, Azure Monitor) before production.

**No Unit/Integration Tests:**
- Problem: README line 120 notes "Add unit and integration test projects" as incomplete roadmap item. No test projects exist.
- Blocks: Cannot safely refactor code. Regressions go undetected. Confidence in bug fixes is low.
- Roadmap impact: Critical. Add xUnit test projects for each service layer before scaling.

**No API Documentation Beyond Swagger:**
- Problem: Only Swagger/OpenAPI docs exist. No examples of request/response payloads for error cases.
- Blocks: Clients don't know how to handle 202 Accepted responses from refresh endpoint. Retry logic unclear.
- Roadmap impact: Document polling behavior and backoff strategy.

**No Data Retention Policy:**
- Problem: Match data accumulates indefinitely. No documented retention or archival strategy.
- Blocks: Can't meet compliance requirements if needed (e.g., GDPR: right to deletion). Storage costs grow unbounded.
- Roadmap impact: Define retention policy and implement automated archival before scaling beyond 1M matches.

**No Riot API Quota Management:**
- Problem: No tracking of API calls against Riot's rate limits. No alerting when approaching limits.
- Blocks: Can accidentally exceed quota and lock out service for duration.
- Roadmap impact: Implement quota tracking and graceful degradation (skip low-elo summoners if quota low).

## Test Coverage Gaps

**No Tests for Job Services:**
- What's not tested: `FetchLatestMatchInformation`, `SummonerRefreshJob`, `UpdateStaticDataJob` business logic
- Files: `Transcendence.Service.Core/Services/Jobs/`
- Risk: Enum parsing errors, null reference exceptions, API failures uncaught. Regressions in job logic go undetected until production.
- Priority: **High** - Jobs are core to data ingestion pipeline.

**No Tests for Repository Layer:**
- What's not tested: Concurrency handling in `RefreshLockRepository`, match deduplication logic in `MatchRepository`
- Files: `Transcendence.Data/Repositories/Implementations/`
- Risk: Race conditions in lock acquisition, false negatives in duplicate detection. Hard to diagnose in production.
- Priority: **High** - Repository bugs cause data consistency issues.

**No Tests for StaticData Service:**
- What's not tested: JSON deserialization, patch normalization, external API failure handling
- Files: `Transcendence.Service.Core/Services/StaticData/Implementations/StaticDataService.cs`
- Risk: Corrupted or missing static data silently persisted. Enum parsing errors when loading matches. Cannot mock CDG/DDG responses.
- Priority: **High** - Static data is foundational for match enrichment.

**No Tests for Stats Aggregation:**
- What's not tested: KDA ratio calculation, CS/min normalization, percentile logic
- Files: `Transcendence.Service.Core/Services/Analysis/Implementations/SummonerStatsService.cs`
- Risk: Incorrect stats silently presented to API clients. Division by zero (deaths=0) handled, but other edge cases (matches=0, duration=0) may not be.
- Priority: **Medium** - Stats correctness important but secondary to data ingestion.

**No Tests for Controller Endpoints:**
- What's not tested: Region validation, lock acquisition flow, 202 Accepted response logic
- Files: `Transcendence.WebAPI/Controllers/`
- Risk: Invalid regions cause 400 BadRequest instead of 422 UnprocessableEntity (wrong HTTP status). Lock TTL/retry logic unclear.
- Priority: **Medium** - API contract should be tested.

---

*Concerns audit: 2026-01-31*
