# Requirements: Transcendence Backend

**Defined:** 2026-02-01
**Core Value:** Summoner profiles with comprehensive stats — the foundation that enables the desktop app to be built against this API

## v1 Requirements

Requirements for initial release. Each maps to roadmap phases.

### Data Infrastructure

- [x] **INFRA-01**: Static data (champions, items, runes) auto-updates on patch releases
- [x] **INFRA-02**: Two-tier caching with memory (L1) and Redis (L2)

### Summoner Profiles

- [x] **PROF-01**: User can look up any summoner by Riot ID (gameName#tagLine)
- [x] **PROF-02**: User can view summoner's recent match history with full stats
- [x] **PROF-03**: User can view summoner's current rank, LP, and win/loss record
- [x] **PROF-04**: User can view summoner's performance breakdown by champion

### Champion Analytics

- [x] **CHAMP-01**: User can view champion win rates by role and rank tier
- [x] **CHAMP-02**: User can view popular builds (items, runes, skill orders) per champion
- [x] **CHAMP-03**: User can view champion tier lists by role
- [x] **CHAMP-04**: User can view champion matchup data (counters, synergies)

### Live Game

- [x] **LIVE-01**: User can detect if a summoner is currently in game
- [x] **LIVE-02**: User can view all participants' ranks and recent performance
- [x] **LIVE-03**: User can see team composition analysis (strengths/weaknesses)
- [x] **LIVE-04**: User can see estimated win probability for each team

### Authentication

- [x] **AUTH-01**: Apps can authenticate via API keys
- [x] **AUTH-02**: User can create account and log in
- [x] **AUTH-03**: User can save favorites and preferences

### Management

- [ ] **MGMT-01**: API exposes health check endpoints
- [ ] **MGMT-02**: Admins can access Hangfire job dashboard
- [ ] **MGMT-03**: Admins can trigger manual data refreshes
- [ ] **MGMT-04**: System exposes metrics via OpenTelemetry

## v2 Requirements

Deferred to future release. Tracked but not in current roadmap.

### Data Infrastructure

- **INFRA-03**: Rate limit compliance with dynamic throttling and backoff
- **INFRA-04**: Data retention handling for expired Riot data

### Authentication

- **AUTH-04**: Riot account linking via RSO (Riot Sign On)

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Desktop application | Separate repo, consumes this API |
| Web frontend | Implemented in this repo under `apps/web` (monorepo) |
| Mobile app | Web-first approach |
| Real-time push notifications | Polling model for v1 |
| Social features (friends, sharing) | Not core to analytics |
| Real-time chat | Not relevant to analytics use case |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| INFRA-01 | Phase 1 | Complete |
| INFRA-02 | Phase 1 | Complete |
| PROF-01 | Phase 2 | Complete |
| PROF-02 | Phase 2 | Complete |
| PROF-03 | Phase 2 | Complete |
| PROF-04 | Phase 2 | Complete |
| CHAMP-01 | Phase 3 | Complete |
| CHAMP-02 | Phase 3 | Complete |
| CHAMP-03 | Phase 3 | Complete |
| CHAMP-04 | Phase 3 | Complete |
| LIVE-01 | Phase 4 | Complete |
| LIVE-02 | Phase 4 | Complete |
| LIVE-03 | Phase 4 | Complete |
| LIVE-04 | Phase 4 | Complete |
| AUTH-01 | Phase 4 | Complete |
| AUTH-02 | Phase 4 | Complete |
| AUTH-03 | Phase 4 | Complete |
| MGMT-01 | Phase 5 | Pending |
| MGMT-02 | Phase 5 | Pending |
| MGMT-03 | Phase 5 | Pending |
| MGMT-04 | Phase 5 | Pending |

**Coverage:**
- v1 requirements: 21 total
- Mapped to phases: 21
- Unmapped: 0 ✓

---
*Requirements defined: 2026-02-01*
*Last updated: 2026-02-05 after Phase 4 completion*
