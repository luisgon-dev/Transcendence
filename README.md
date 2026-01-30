# Transcendence Backend

A .NET 9.0 backend service for aggregating and analyzing League of Legends summoner data and match history. Integrates with Riot Games' official APIs to provide player statistics, match history, and performance analytics.

## Tech Stack

- **.NET 9.0** - ASP.NET Core Web API and Worker Service
- **Entity Framework Core 9.0** - ORM with PostgreSQL (production) / SQL Server (development)
- **Hangfire** - Background job processing with PostgreSQL storage
- **Camille** - Riot Games API SDK
- **Docker** - Multi-stage containerized deployment
- **GitHub Actions** - CI/CD with container image signing via Cosign

## Features

- **Summoner Lookup** - Fetch player profiles by Riot ID (gameName#tagLine) across all platform regions (NA, EUW, KR, etc.)
- **Match History Ingestion** - Background job system that fetches and persists ranked match data from Riot API with deduplication
- **Player Statistics** - Aggregated stats including KDA, win rates, CS/min, vision score, and damage metrics
- **Champion and Role Analytics** - Per-champion performance breakdown and role/position statistics
- **Concurrency Control** - Refresh lock system prevents duplicate API calls for the same summoner

## Running Locally

### Prerequisites

- .NET 9.0 SDK
- PostgreSQL (or SQL Server for development)
- Riot Games API key ([developer.riotgames.com](https://developer.riotgames.com))

### Configuration

Set up user secrets for the WebAPI project:

```bash
dotnet user-secrets set "ConnectionStrings:MainDatabase" "Host=localhost;Database=transcendence;Username=postgres;Password=yourpassword" --project Transcendence.WebAPI
dotnet user-secrets set "ConnectionStrings:RiotApi" "RGAPI-your-api-key" --project Transcendence.WebAPI
```

And for the Service project:

```bash
dotnet user-secrets set "ConnectionStrings:MainDatabase" "Host=localhost;Database=transcendence;Username=postgres;Password=yourpassword" --project Transcendence.Service
dotnet user-secrets set "ConnectionStrings:RiotApi" "RGAPI-your-api-key" --project Transcendence.Service
```

### Database Setup

```bash
dotnet ef database update --project Transcendence.Service
```

### Running

Run both the API and background service:

```bash
# Terminal 1 - Web API
dotnet run --project Transcendence.WebAPI

# Terminal 2 - Background Service
dotnet run --project Transcendence.Service
```

Or use Docker Compose:

```bash
docker-compose up --build
```

The API will be available at `https://localhost:5001` with Swagger UI at `/swagger`.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                   Transcendence.WebAPI                       │
│  Controllers (REST endpoints) + Swagger/OpenAPI              │
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│                  Transcendence.Service                       │
│  ┌─────────────────┐  ┌─────────────────┐  ┌──────────────┐ │
│  │ Analysis        │  │ RiotApi         │  │ Jobs         │ │
│  │ - Stats         │  │ - Summoner      │  │ - Refresh    │ │
│  │ - Champions     │  │ - Match         │  │ - StaticData │ │
│  │ - Roles         │  │ - Rank          │  │              │ │
│  └─────────────────┘  └─────────────────┘  └──────────────┘ │
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│                   Transcendence.Data                         │
│  DbContext + Domain Models + Repositories                    │
│  (Summoner, Match, MatchParticipant, Rank, RefreshLock)     │
└──────────────────────────┬──────────────────────────────────┘
                           │
                           ▼
                     PostgreSQL
```

**Request Flow:**
1. Client requests summoner by Riot ID via REST API
2. If not cached, client triggers refresh endpoint
3. Hangfire job fetches data from Riot API (summoner + recent ranked matches)
4. Data is persisted to PostgreSQL with deduplication
5. Stats endpoints aggregate stored match data on demand

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/summoners/{region}/{name}/{tag}` | Get summoner by Riot ID |
| POST | `/api/summoners/{region}/{name}/{tag}/refresh` | Queue background data refresh |
| GET | `/api/summoners/{summonerId}/stats/overview` | Overall player statistics |
| GET | `/api/summoners/{summonerId}/stats/champions` | Top champion performance |
| GET | `/api/summoners/{summonerId}/stats/roles` | Role/position breakdown |
| GET | `/api/summoners/{summonerId}/matches/recent` | Paginated match history |

## Roadmap

- [ ] Add unit and integration test projects
- [ ] Implement real-time rank tracking and LP history
- [ ] Add match timeline analysis (gold/XP graphs, objective control)
- [ ] Build out WebAdminPortal for data management
- [ ] Add rate limiting and caching layers for production scale
- [ ] Support additional queue types (Flex, ARAM)
