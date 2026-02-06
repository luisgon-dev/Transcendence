# Roadmap: Transcendence Backend

**Created:** 2026-02-01
**Core Value:** Summoner profiles with comprehensive stats — the foundation that enables the desktop app to be built against this API
**Milestone:** v1.0 — Production API for desktop and web analytics clients

## Overview

This roadmap delivers a production-grade League of Legends analytics API across five phases. Foundation work addresses critical pitfalls (rate limiting, data retention, caching) that have no recovery if handled incorrectly. Subsequent phases build summoner profiles, champion analytics, live game features, and operational tooling—all designed to support both desktop and web clients at scale.

## Phases

### Phase 1: Foundation - Data Infrastructure & API Safety

**Goal:** Establish robust caching and rate limit safety that prevents API blacklisting and enables all downstream features.

**Requirements:**
- INFRA-01: Static data (champions, items, runes) auto-updates on patch releases
- INFRA-02: Two-tier caching with memory (L1) and Redis (L2)

**Success Criteria:**
1. Static data auto-updates within 6 hours of patch release with versioned cache keys
2. Redis two-tier caching operational with HybridCache (stampede protection)
3. Riot API rate limit headers read dynamically and respected (no hard-coded limits)
4. Match fetching checks retention windows (2 years matches, 1 year timelines) before requesting
5. Retry logic with exponential backoff handles eventual consistency (30s, 60s, 120s, 300s)

**Dependencies:** None (foundational)

**Directory:** `.planning/phases/01-foundation/`

**Plans:** 4 plans

Plans:
- [x] 01-01-PLAN.md — HybridCache + Redis setup with stampede protection
- [x] 01-02-PLAN.md — Patch detection with cache invalidation and scheduled refresh
- [x] 01-03-PLAN.md — Retry logic with exponential backoff and retention window checks
- [x] 01-04-PLAN.md — Data freshness metadata in API responses

---

### Phase 2: Summoner Profiles - Complete Profile API

**Goal:** Deliver comprehensive summoner profile endpoints that enable desktop and web clients to display player stats, match history, and performance breakdowns.

**Requirements:**
- PROF-01: User can look up any summoner by Riot ID (gameName#tagLine)
- PROF-02: User can view summoner's recent match history with full stats
- PROF-03: User can view summoner's current rank, LP, and win/loss record
- PROF-04: User can view summoner's performance breakdown by champion

**Success Criteria:**
1. Summoner lookup by Riot ID (gameName#tagLine) returns complete profile in <500ms
2. Match history displays participant stats (KDA, CS, gold, damage, items, runes) for last 20 games
3. Rank display shows current LP, tier, division, and win/loss record
4. Champion performance breakdown shows per-champion stats (games played, win rate, avg KDA)
5. PUUID used as primary key (not summoner name) for identity stability

**Dependencies:** Phase 1 (caching, rate limiting)

**Directory:** `.planning/phases/02-summoner-profiles/`

**Plans:** 4 plans in 2 waves

Plans:
- [x] 02-01-PLAN.md — Full match details endpoint with items/runes/spells (Wave 1)
- [x] 02-02-PLAN.md — Complete profile response in single API call (Wave 1)
- [x] 02-03-PLAN.md — HybridCache for stats queries (Wave 2)
- [x] 02-04-PLAN.md — Match history with loadout data (Wave 2)

---

### Phase 3: Champion Analytics - Meta Insights & Recommendations

**Goal:** Provide champion analytics (tier lists, builds, matchups, trends) that inform player decisions and differentiate the platform.

**Requirements:**
- CHAMP-01: User can view champion win rates by role and rank tier
- CHAMP-02: User can view popular builds (items, runes, skill orders) per champion
- CHAMP-03: User can view champion tier lists by role
- CHAMP-04: User can view champion matchup data (counters, synergies)

**Success Criteria:**
1. Champion win rates by role/tier refresh daily and cache for 24 hours
2. Build recommendations show top 3 builds by win rate for each champion/patch/rank
3. Tier lists rank all champions per role with S/A/B/C/D grades
4. Matchup matrix shows counters/synergies with statistical significance (min 100 games)
5. Analytics queries hit cache >80% of time (sub-500ms response)

**Dependencies:** Phase 1 (caching), Phase 2 (match data for aggregations)

**Directory:** `.planning/phases/03-champion-analytics/`

**Plans:** 5 plans in 3 waves

Plans:
- [x] 03-01-PLAN.md — Analytics foundation + win rates by role/tier (Wave 1)
- [x] 03-02-PLAN.md — Tier lists with S/A/B/C/D grades and movement indicators (Wave 2)
- [x] 03-03-PLAN.md — Build recommendations with core/situational items (Wave 2)
- [x] 03-04-PLAN.md — Matchup data (counters and synergies) (Wave 2)
- [x] 03-05-PLAN.md — Daily refresh job with cache pre-warming (Wave 3)

---

### Phase 4: Live Game & Authentication - Desktop App Enablement

**Goal:** Enable live game lookup with participant analysis and secure API access via authentication, unblocking desktop application development.

**Requirements:**
- LIVE-01: User can detect if a summoner is currently in game
- LIVE-02: User can view all participants' ranks and recent performance
- LIVE-03: User can see team composition analysis (strengths/weaknesses)
- LIVE-04: User can see estimated win probability for each team
- AUTH-01: Apps can authenticate via API keys
- AUTH-02: User can create account and log in
- AUTH-03: User can save favorites and preferences

**Success Criteria:**
1. Live game detection responds within 2 seconds of game start via Spectator API polling
2. Participant analysis enriches with champion stats, recent performance, rank from Phase 2/3
3. Team composition shows aggregate win probability based on matchup data
4. API key authentication works with X-API-Key header (desktop app ready)
5. User JWT authentication supports registration, login, password reset
6. Favorites/preferences persist across sessions for logged-in users
7. Spectator polling respects rate limits (adaptive intervals: 5min offline, 30s lobby, 60s in-game)

**Dependencies:** Phase 1 (caching), Phase 2 (summoner data), Phase 3 (matchup analytics)

**Directory:** `.planning/phases/04-live-game-auth/`

**Plans:** 6 plans in 3 waves

Plans:
- [x] 04-01-PLAN.md — API key authentication foundation with AppOnly policy (Wave 1)
- [x] 04-02-PLAN.md — Live game lookup endpoint with Spectator mapping + cache (Wave 1)
- [x] 04-03-PLAN.md — Adaptive live game polling job + snapshot persistence (Wave 2)
- [x] 04-04-PLAN.md — Participant/team analysis + win probability enrichment (Wave 2)
- [x] 04-05-PLAN.md — JWT user accounts (register/login/refresh/reset init) (Wave 3)
- [x] 04-06-PLAN.md — Favorites/preferences + secure admin operational endpoints (Wave 3)

---

### Phase 5: Management & Monitoring - Operational Hardening

**Goal:** Provide health visibility, manual data controls, and production monitoring for reliable operations.

**Requirements:**
- MGMT-01: API exposes health check endpoints
- MGMT-02: Admins can access Hangfire job dashboard
- MGMT-03: Admins can trigger manual data refreshes
- MGMT-04: System exposes metrics via OpenTelemetry

**Success Criteria:**
1. Health check endpoints report PostgreSQL, Redis, Hangfire, Riot API status
2. Hangfire dashboard accessible with authentication (not localhost-only)
3. Manual refresh endpoints allow on-demand summoner/match data updates
4. OpenTelemetry exports metrics to Prometheus (rate limit usage, cache hit rate, job success rate)
5. Structured logging captures Riot API errors, rate limit warnings, retry attempts

**Dependencies:** Phase 1 (infra to monitor), Phase 4 (auth for dashboard)

**Directory:** `.planning/phases/05-management/`

**Plans:** 0 plans

Plans:
- [ ] TBD (use `/gsd:plan-phase 5` to create)

---

## Progress

| Phase | Status | Requirements | Completion |
|-------|--------|--------------|------------|
| 1 - Foundation | ✓ Complete | INFRA-01, INFRA-02 | 100% |
| 2 - Summoner Profiles | ✓ Complete | PROF-01, PROF-02, PROF-03, PROF-04 | 100% |
| 3 - Champion Analytics | ✓ Complete | CHAMP-01, CHAMP-02, CHAMP-03, CHAMP-04 | 100% |
| 4 - Live Game & Auth | ✓ Complete | LIVE-01, LIVE-02, LIVE-03, LIVE-04, AUTH-01, AUTH-02, AUTH-03 | 100% |
| 5 - Management | Pending | MGMT-01, MGMT-02, MGMT-03, MGMT-04 | 0% |

**Overall:** 81% (17/21 requirements complete)

## Coverage

| Category | Requirements | Phases |
|----------|--------------|--------|
| Data Infrastructure | 2 | Phase 1 |
| Summoner Profiles | 4 | Phase 2 |
| Champion Analytics | 4 | Phase 3 |
| Live Game | 4 | Phase 4 |
| Authentication | 3 | Phase 4 |
| Management | 4 | Phase 5 |

**Total:** 21 requirements across 5 phases
**Unmapped:** 0 ✓

---

## Notes

**Phase Compression:** Research suggested 6 phases, compressed to 5 for "quick" depth by combining authentication (API keys + user accounts) with live game in Phase 4. Both are needed to unblock desktop app development.

**Critical Risks (Phase 1):** Rate limit violations cause permanent API blacklisting. Data retention blindness creates unrecoverable gaps. Patch-blind caching shows wrong data. Phase 1 has no do-overs—these must be solved correctly.

**Brownfield Context:** Existing codebase already has summoner lookup, match history, and ranked tracking. Phase 2 polishes these features into production-ready endpoints rather than building from scratch.

---

*Last updated: 2026-02-05*
