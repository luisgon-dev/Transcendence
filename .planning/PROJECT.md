# Transcendence Backend

## What This Is

A complete backend API for a League of Legends analytics service similar to op.gg or u.gg. Powers both a desktop application (which interacts with the League client) and a web interface. Provides summoner profiles, match history, champion analytics, player insights, and live game analysis across all regions.

## Core Value

Summoner profiles with comprehensive stats — the foundation that enables the desktop app to be built against this API.

## Requirements

### Validated

- ✓ Summoner lookup by Riot ID (gameName#tagLine) — existing
- ✓ Match history fetching and storage — existing
- ✓ Match participant data (KDA, CS, gold, damage, items, runes) — existing
- ✓ Rank/LP tracking with historical snapshots — existing
- ✓ Background job processing via Hangfire — existing
- ✓ Static data management (patches, runes, items) — existing
- ✓ Multi-region support via platform routing — existing
- ✓ Riot API integration (Summoner V4, Account V1, Match V5, Ranked) — existing

### Active

- [ ] Complete summoner profile API with comprehensive stats
- [ ] Champion performance analytics (win rates, matchups, builds, skill orders)
- [ ] Player insights and trends over time
- [ ] Live game analysis via Spectator API
- [ ] API key authentication for client apps
- [ ] User accounts with Riot account linking
- [ ] Management dashboard (data controls, refresh management, system health)
- [ ] Data layer polish (fill gaps, improve performance, ensure reliability)
- [ ] All 16 regions fully supported

### Out of Scope

- Desktop application — separate repo, consumes this API
- Web frontend — implemented in this repo under `apps/web` (monorepo for dev ergonomics)
- Mobile app — web-first approach
- Real-time push notifications — polling model for v1
- Social features (friends, sharing) — not core to analytics

## Context

**Existing Codebase:**
- .NET 10 with ASP.NET Core WebAPI and Worker Service
- Entity Framework Core with PostgreSQL
- Hangfire for background job processing
- Camille SDK for Riot API integration
- Clean layered architecture: WebAPI → Service.Core → Data

**Current State:**
- Basic summoner refresh flow works but needs polish
- Match data captured but analysis features incomplete
- No authentication on API endpoints
- No management dashboard or health monitoring
- Data reliability issues (gaps, refresh failures, inconsistent state)

**Clients This API Must Support:**
1. Desktop app — needs live game data during champ select, summoner lookups
2. Web interface — needs full profile views, match history, analytics

## Constraints

- **Tech Stack**: .NET 10, PostgreSQL, Hangfire — established, don't change
- **Riot API**: Rate limits apply, must handle gracefully
- **Regions**: All 16 regions (NA, EUW, EUNE, KR, JP, BR, LAN, LAS, OCE, TR, RU, PH, SG, TH, TW, VN)
- **Auth Model**: API keys for apps + user accounts for personalization

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Poll Spectator API for live game | Simpler than desktop push, works for web too | — Pending |
| Summoner profiles first priority | Foundation for desktop app development | — Pending |
| Dual auth (API keys + user accounts) | Apps need simple auth, users need personalization | — Pending |

---
*Last updated: 2026-01-31 after initialization*
