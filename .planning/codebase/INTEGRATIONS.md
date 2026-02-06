# External Integrations

**Analysis Date:** 2026-01-31

## APIs & External Services

**Riot Games:**
- **Summoner V4 API** - Fetch summoner profile by PUUID
  - SDK/Client: Camille.RiotGames 3.0.0-nightly
  - Implementation: `Transcendence.Service.Core/Services/RiotApi/Implementations/SummonerService.cs`
  - Auth: Environment variable `RiotApi:ApiKey` (only loaded in `Transcendence.Service`)
  - Usage: `riotApi.SummonerV4().GetByPUUIDAsync(platform, puuid, ct)`

- **Account V1 API** - Fetch account by Riot ID (gameName#tagLine) and by PUUID
  - SDK/Client: Camille.RiotGames
  - Implementation: SummonerService uses account lookup before summoner lookup
  - Auth: Same as above (RiotApi:ApiKey)
  - Usage: `riotApi.AccountV1().GetByRiotIdAsync(regional, gameName, tagLine, ct)`

- **Match V5 API** - Fetch match history and match details
  - SDK/Client: Camille.RiotGames
  - Implementation: `Transcendence.Service.Core/Services/RiotApi/Implementations/MatchService.cs`
  - Auth: RiotApi:ApiKey
  - Usage: Match history pagination, match timeline data, participant statistics

- **Ranked API** - Fetch rank/LP data by summoner
  - SDK/Client: Camille.RiotGames
  - Implementation: `Transcendence.Service.Core/Services/RiotApi/Implementations/RankService.cs`
  - Auth: RiotApi:ApiKey
  - Usage: Solo/Duo queue rank, Flex queue rank, LP, tier progression

**Data Dragon / Community Dragon:**
- **Data Dragon API** - Static game data (items, runes, champions, patches)
  - Endpoint: `https://ddragon.leagueoflegends.com/api/versions.json`
  - SDK/Client: HttpClientFactory with System.Net.Http
  - Implementation: `Transcendence.Service.Core/Services/StaticData/Implementations/StaticDataService.cs`
  - Auth: None (public API)
  - Usage: Fetch current patch versions, item databases, rune data for analysis

- **Community Dragon CDN** - Champion icons, item icons, rune assets
  - Endpoint: `https://cdn.communitydragon.app/` (inferred from static data service)
  - Implementation: Data parsed and persisted locally
  - Auth: None

## Data Storage

**Databases:**
- **PostgreSQL 12+** (Production)
  - Connection: Via `ConnectionStrings:MainDatabase` environment variable
  - Client: Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0
  - ORM: Entity Framework Core 10.0.2
  - Schema: `TranscendenceContext` DbContext in `Transcendence.Data/TranscendenceContext.cs`
  - Tables:
    - Summoners - Player profiles (PUUID, account ID, game name, tag line, level, platform region)
    - Matches - Match records (match ID, timestamp, queue type, game version)
    - MatchParticipants - Individual player performance per match (KDA, CS, gold, damage, items)
    - MatchParticipantRunes - Selected runes per participant
    - MatchParticipantItems - Purchased items per participant
    - Ranks - Current rank/LP/tier for each summoner per queue
    - HistoricalRanks - Historical rank snapshots for LP tracking
    - CurrentChampionLoadouts - Best items/runes per champion
    - RuneVersions, ItemVersions, Patches - Static data versions

- **SQL Server 2019+** (Development only)
  - Connection: Via local dev configuration
  - Client: Microsoft.EntityFrameworkCore.SqlServer 10.0.2
  - Same schema as production PostgreSQL

**File Storage:**
- Not used - All data persists to relational database

**Caching:**
- None configured - Redis or in-memory caching not integrated
- Static data fetched on-demand from Data Dragon API and cached in database
- No HTTP response caching headers specified

## Background Job Processing

**Job Queue:**
- **Hangfire 1.8.22** - Background job orchestration and persistence
  - Storage: PostgreSQL via Hangfire.PostgreSql 1.20.13
  - Server: Runs in `Transcendence.Service` (Worker Service)
  - Client: WebAPI enqueues jobs for Hangfire server to process
  - Configuration: `Program.cs` in both WebAPI and Service projects

**Jobs Defined:**
- **SummonerRefreshJob** - Refresh summoner profile and match history
  - Implementation: `Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs`
  - Trigger: Endpoint POST `/api/summoners/{region}/{name}/{tag}/refresh`
  - Behavior: Fetches summoner data, recent ranked matches, deduplicates

- **UpdateStaticDataJob** - Fetch latest patch, items, runes from Data Dragon
  - Implementation: `Transcendence.Service.Core/Services/Jobs/UpdateStaticDataJob.cs`
  - Trigger: Scheduled or manual (depends on configuration)

- **AnalyzeData** - Compute champion loadouts and role statistics
  - Implementation: `Transcendence.Service.Core/Services/Jobs/AnalyzeData.cs`

- **AddOrUpdateHighEloProfiles** - Bulk import high-elo players
  - Implementation: `Transcendence.Service.Core/Services/Jobs/AddOrUpdateHighEloProfiles.cs`

- **FetchLatestMatchInformation** - Incremental match history updates
  - Implementation: `Transcendence.Service.Core/Services/Jobs/FetchLatestMatchInformation.cs`

**Retry Policy:**
- Development: No automatic retries (AutomaticRetryAttribute.Attempts = 0)
- Production: TBD (same configuration applied)
- See: `Transcendence.Service/Program.cs` lines 16-25

## Authentication & Identity

**API Key Auth:**
- **Riot Games API Key:**
  - Source: `RiotApi:ApiKey` configuration key
  - Storage: User Secrets (dev), Environment Variable (prod)
  - Scope: Only `Transcendence.Service` project receives the key (WebAPI has no Riot API access)
  - Registration: `ServiceCollectionExtensions.AddTranscendenceRiot()` in `Transcendence.Service.Core/Services/Extensions/ServiceCollectionExtensions.cs`
  - Implementation: Passed to `RiotGamesApi.NewInstance(apiKey)` from Camille SDK

**Application Auth:**
- Not implemented - No authentication/authorization on API endpoints
- AllowedHosts: "*" in appsettings.json (open to any origin)
- No Bearer tokens, API keys, or user authentication on REST endpoints

## Webhooks & Callbacks

**Incoming:**
- None implemented - Riot Games does not provide webhooks for match events

**Outgoing:**
- None implemented - Application does not push data to external services

## Monitoring & Observability

**Error Tracking:**
- None configured - No integration with Sentry, AppInsights, or error tracking services
- Logging via Microsoft.Extensions.Logging abstraction

**Logs:**
- Approach: ASP.NET Core built-in logging
- Configuration: `appsettings.json` with log levels
  - Default: "Information"
  - Microsoft.AspNetCore: "Warning"
- Sink: Console output (no external log aggregation configured)

**Health Checks:**
- Not configured - No /health endpoint defined

## Environment Configuration

**Required Environment Variables (Production):**
- `ConnectionStrings:MainDatabase` - PostgreSQL connection string (format: `Host=...;Database=...;Username=...;Password=...`)
- `RiotApi:ApiKey` - Riot Games API key for `Transcendence.Service` project only

**Required Secrets (Development):**
- Same as above, managed via `dotnet user-secrets set` command
- User Secrets ID per project:
  - WebAPI: 4f8b5993-bfbd-4070-941c-b73b591743ee
  - Service: dotnet-ProjectSyndraBackend.Service-7020057A-78AB-49AA-A01A-00BDB9E404F0
  - Data: fcb80467-e989-468a-8b30-6da0d94bbbec
  - WebAdminPortal: b4d1ea52-6fae-4ee6-8950-f8ad6ca7b490

**Optional Configuration:**
- ASPNETCORE_ENVIRONMENT - Set to "Development" or "Production"
- ASPNETCORE_URLS - Override HTTP/HTTPS binding addresses

## Service Registration Pattern

**WebAPI Startup** (`Transcendence.WebAPI/Program.cs`):
```csharp
// Line 18-20: Database context with PostgreSQL
builder.Services.AddDbContext<TranscendenceContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("MainDatabase"),
        b => b.MigrationsAssembly("Transcendence.Service")));

// Line 25-26: Core services (analysis) + repositories
builder.Services.AddTranscendenceCore();
builder.Services.AddProjectSyndraRepositories();

// Line 29-34: Hangfire client (enqueue jobs only, no server)
builder.Services.AddHangfire(config =>
    config.UsePostgreSqlStorage(builder.Configuration.GetConnectionString("MainDatabase")));
```

**Service Startup** (`Transcendence.Service/Program.cs`):
```csharp
// Same database setup, but also:
// Line 26: Start Hangfire server for job processing
builder.Services.AddHangfireServer();

// Line 39: Register Riot API client (Service has API key access)
builder.Services.AddTranscendenceRiot(builder.Configuration);
```

---

*Integration audit: 2026-01-31*
