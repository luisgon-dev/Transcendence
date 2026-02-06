# Codebase Structure

**Analysis Date:** 2026-01-31

## Directory Layout

```
transcendence_backend/
├── Transcendence.Data/              # Data access layer (EF Core, repositories, models)
│   ├── Models/                      # EF Core entity models
│   │   ├── LoL/
│   │   │   ├── Account/             # Summoner, Rank, HistoricalRank
│   │   │   ├── Match/               # Match, MatchParticipant, MatchParticipantRune, MatchParticipantItem
│   │   │   └── Static/              # Patch, RuneVersion, ItemVersion (versioned game data)
│   │   └── Service/                 # RefreshLock, CurrentDataParameters, CurrentChampionLoadout
│   ├── Repositories/                # Data access abstraction
│   │   ├── Interfaces/              # ISummonerRepository, IMatchRepository, IRankRepository, IRefreshLockRepository
│   │   └── Implementations/         # Concrete repositories with async repository pattern
│   ├── Extensions/                  # DI registration
│   │   └── ServiceCollectionExtensions.cs  # AddProjectSyndraRepositories()
│   └── TranscendenceContext.cs      # EF DbContext with OnModelCreating config
│
├── Transcendence.Service.Core/      # Shared business logic and Riot API integration
│   └── Services/
│       ├── Analysis/                # Stats computation from match data
│       │   ├── Interfaces/          # ISummonerStatsService, IChampionLoadoutAnalysisService
│       │   ├── Implementations/     # SummonerStatsService, ChampionLoadoutAnalysisService
│       │   └── Models/              # StatsModels (DTOs for stats results)
│       ├── RiotApi/                 # Riot API client wrappers
│       │   ├── Interfaces/          # ISummonerService, IRankService, IMatchService
│       │   └── Implementations/     # Concrete services using Camille SDK
│       ├── StaticData/              # Game data (champions, items, runes, patches)
│       │   ├── Interfaces/          # IStaticDataService
│       │   ├── Implementations/     # StaticDataService
│       │   └── DTOs/                # CommunityDragonRune, CommunityDragonItem, DataDragonPatch
│       ├── Jobs/                    # Hangfire background job definitions
│       │   ├── Interfaces/          # ISummonerRefreshJob
│       │   ├── SummonerRefreshJob.cs   # On-demand summoner & match refresh
│       │   └── UpdateStaticDataJob.cs  # Scheduled daily patch/rune/item updates
│       └── Extensions/              # DI registration
│           ├── ServiceCollectionExtensions.cs  # AddTranscendenceCore(), AddTranscendenceRiot()
│           └── HangfireExtensions.cs           # Hangfire filter configs
│
├── Transcendence.Service/           # Background job worker host
│   ├── Program.cs                   # Hangfire server configuration, worker selection
│   ├── Workers/
│   │   ├── DevelopmentWorker.cs     # Dev-mode worker (immediate job execution, cleanup)
│   │   └── ProductionWorker.cs      # Prod-mode worker (schedules UpdateStaticDataJob daily)
│   ├── Migrations/                  # EF Core migrations (applies to this project only)
│   └── appsettings.json             # DB connection, Riot API key, Hangfire config
│
├── Transcendence.WebAPI/            # REST API for clients
│   ├── Program.cs                   # Swagger, controllers, Hangfire client config
│   ├── Controllers/
│   │   ├── SummonersController.cs   # GET /api/summoners/{region}/{name}/{tag}, POST /refresh
│   │   └── SummonerStatsController.cs  # GET /api/summoners/{id}/stats/*, /matches/recent
│   ├── Models/                      # Response DTOs (SummonerOverviewDto, ChampionStatDto, etc.)
│   └── appsettings.json             # DB connection, Hangfire client config
│
├── Transcendence.WebAdminPortal/    # Hangfire dashboard host
│   ├── Program.cs                   # Hangfire dashboard middleware only
│   └── appsettings.json             # DB connection for Hangfire storage
│
├── Transcendence.sln                # Solution file (references all projects)
├── global.json                      # .NET version specification
├── docker-compose.yml               # PostgreSQL + services orchestration
├── .github/workflows/               # CI/CD pipelines
└── docs/                            # API documentation
```

## Directory Purposes

**Transcendence.Data:**
- Purpose: Entity Framework models, repositories, and database schema
- Contains: Entity models (Summoner, Match, Rank, etc.), repository interfaces/implementations, DbContext
- Key files: `TranscendenceContext.cs`, `Repositories/Interfaces/*`, `Models/LoL/*`

**Transcendence.Service.Core:**
- Purpose: Reusable business logic and external API integration
- Contains: Riot API client wrappers, analytics/stats services, background job definitions
- Key files: `Services/RiotApi/Implementations/*`, `Services/Analysis/Implementations/*`, `Services/Jobs/*`

**Transcendence.Service:**
- Purpose: Background job worker host process
- Contains: Hangfire server startup, environment-specific worker bootstrapping
- Key files: `Program.cs`, `Workers/*`

**Transcendence.WebAPI:**
- Purpose: REST API endpoints for external clients
- Contains: ASP.NET Core controllers, response DTOs
- Key files: `Controllers/SummonersController.cs`, `Controllers/SummonerStatsController.cs`

**Transcendence.WebAdminPortal:**
- Purpose: Administrative monitoring and job management
- Contains: Hangfire dashboard configuration only
- Key files: `Program.cs` (minimal; just dashboard setup)

## Key File Locations

**Entry Points:**
- `Transcendence.WebAPI/Program.cs`: HTTP API bootstrap (Swagger, controllers, Hangfire client)
- `Transcendence.Service/Program.cs`: Worker bootstrap (Hangfire server, job host)
- `Transcendence.WebAdminPortal/Program.cs`: Dashboard bootstrap (Hangfire middleware)

**Configuration:**
- `Transcendence.Service/appsettings.json`: RiotApi:ApiKey, MainDatabase connection
- `Transcendence.WebAPI/appsettings.json`: MainDatabase connection (Hangfire client only)
- `Transcendence.WebAdminPortal/appsettings.json`: MainDatabase connection (Hangfire storage)
- `global.json`: .NET target version (net9.0)

**Core Logic:**
- `Transcendence.Data/TranscendenceContext.cs`: EF DbContext with model mappings
- `Transcendence.Service.Core/Services/RiotApi/Implementations/SummonerService.cs`: Riot API summoner lookups
- `Transcendence.Service.Core/Services/Analysis/Implementations/SummonerStatsService.cs`: Statistics aggregation
- `Transcendence.Service.Core/Services/Jobs/SummonerRefreshJob.cs`: Background refresh orchestration

**Models:**
- `Transcendence.Data/Models/LoL/Account/Summoner.cs`: Summoner aggregate root
- `Transcendence.Data/Models/LoL/Match/Match.cs`: Match aggregate with participants
- `Transcendence.Data/Models/LoL/Match/MatchParticipant.cs`: Individual player performance in match

**Repositories:**
- `Transcendence.Data/Repositories/Interfaces/ISummonerRepository.cs`: Summoner persistence contract
- `Transcendence.Data/Repositories/Implementations/SummonerRepository.cs`: Summoner upsert logic

## Naming Conventions

**Projects:**
- Pattern: `Transcendence.[Domain]` (e.g., `Transcendence.Data`, `Transcendence.Service.Core`)
- Convention: Namespace matches directory structure

**Classes:**
- Pattern: PascalCase with suffix indicating role (e.g., `SummonerRepository`, `SummonerService`, `SummonerRefreshJob`)
- Interfaces: `ISummonerRepository`, `ISummonerService` (I-prefix convention)
- Models: Plain `Summoner`, `Match`, `MatchParticipant` (no suffix)
- Controllers: `SummonersController` (plural)
- DTOs: Suffix `Dto` (e.g., `SummonerOverviewDto`)

**Methods:**
- Pattern: PascalCase, async methods suffixed with `Async` (e.g., `GetSummonerByRiotIdAsync`, `AddOrUpdateSummonerAsync`)
- Repository methods: Generic verbs (Get, Add, Update, Delete, Find, Upsert)

**Properties:**
- Pattern: PascalCase for public properties
- Boolean prefix: `Is`, `Has`, `Can` (e.g., `Win`)
- Collection suffix: Plural (e.g., `Summoners`, `Matches`, `Ranks`)

**Files:**
- Pattern: Match class/interface name exactly (e.g., `SummonerRepository.cs` for `SummonerRepository` class)
- Interfaces: Match interface name (e.g., `ISummonerRepository.cs` for `ISummonerRepository` interface)

## Where to Add New Code

**New Feature (e.g., Champion Winrate Endpoint):**
- Primary code: `Transcendence.Service.Core/Services/Analysis/Implementations/[NewService].cs`
- Interface: `Transcendence.Service.Core/Services/Analysis/Interfaces/I[NewService].cs`
- Controller: `Transcendence.WebAPI/Controllers/[NewController].cs`
- Response DTO: `Transcendence.WebAPI/Models/[Feature]/[Dto].cs`
- DI Registration: Update `Transcendence.Service.Core/Services/Extensions/ServiceCollectionExtensions.cs` in `AddTranscendenceCore()`

**New Entity/Repository (e.g., ChampionStats table):**
- Entity Model: `Transcendence.Data/Models/LoL/[Domain]/[Entity].cs`
- Repository Interface: `Transcendence.Data/Repositories/Interfaces/I[Entity]Repository.cs`
- Repository Implementation: `Transcendence.Data/Repositories/Implementations/[Entity]Repository.cs`
- DbSet: Add to `Transcendence.Data/TranscendenceContext.cs`
- Indexes/FK Config: Add to `TranscendenceContext.OnModelCreating()`
- DI Registration: Add to `Transcendence.Data/Extensions/ServiceCollectionExtensions.cs` in `AddProjectSyndraRepositories()`
- Migration: Run `dotnet ef migrations add [Name] --project Transcendence.Service`

**New Background Job (e.g., RankHistoryArchive):**
- Job Interface: `Transcendence.Service.Core/Services/Jobs/Interfaces/I[Job].cs`
- Job Implementation: `Transcendence.Service.Core/Services/Jobs/[Job].cs`
- DI Registration: Update `Transcendence.Service.Core/Services/Extensions/ServiceCollectionExtensions.cs` in `AddTranscendenceRiot()`
- Scheduling: If recurring, add to `ProductionWorker.StartAsync()` via `recurringJobManager.AddOrUpdate<[Job]>()`
- Enqueueing: If on-demand, call `backgroundJobClient.Enqueue<I[Job]>()` from controller

**New Riot API Integration (e.g., ChampionMastery service):**
- Service Interface: `Transcendence.Service.Core/Services/RiotApi/Interfaces/I[Service].cs`
- Service Implementation: `Transcendence.Service.Core/Services/RiotApi/Implementations/[Service].cs` (uses injected `RiotGamesApi`)
- DI Registration: Add to `AddTranscendenceRiot()` in `ServiceCollectionExtensions.cs`
- Note: Ensure `AddTranscendenceRiot()` is only called in `Transcendence.Service` (has Riot API key)

## Special Directories

**Transcendence.Data/Models/LoL:**
- Purpose: League of Legends domain models grouped by context (Account, Match, Static)
- Generated: No (hand-written)
- Committed: Yes

**Transcendence.Service/Migrations:**
- Purpose: EF Core migration scripts for schema changes
- Generated: Yes (via `dotnet ef migrations add`)
- Committed: Yes (track all migrations in version control)

**Transcendence.Service.Core/Services/Jobs/Interfaces:**
- Purpose: Job interface contracts (Hangfire-invokable methods)
- Pattern: Single async Task method per interface representing the background work unit
- Example: `ISummonerRefreshJob.RefreshByRiotId(gameName, tagLine, platformRoute, lockKey, ct)`

**Transcendence.WebAPI/Models:**
- Purpose: Response DTOs for API contracts
- Pattern: Immutable records or classes with readonly properties; map from service result types
- Grouping: Subdirectories by feature (Stats, Account, etc.)

---

*Structure analysis: 2026-01-31*
