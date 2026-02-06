# Project Research Summary: Transcendence Backend

**Project:** Transcendence Backend - League of Legends Analytics Platform
**Domain:** Real-time sports analytics with API integration and distributed caching
**Researched:** 2026-02-01
**Overall Confidence:** HIGH (official Microsoft/Riot docs, verified patterns)

---

## Executive Summary

Transcendence is a production-grade League of Legends analytics backend that extends an existing .NET 10 architecture with four new capability areas: champion performance analytics with caching, live game analysis via Spectator API, dual authentication (API keys + user accounts), and management dashboards. The recommended approach leverages modern .NET 10 patterns—particularly HybridCache for two-tier caching and ASP.NET Core's multiple authentication schemes—to avoid reinventing distributed infrastructure while maintaining the existing layered architecture.

The core technical bet is that a well-designed caching layer (Redis + HybridCache) combined with rate-limited, retention-aware Riot API consumption can handle analytics computation at scale through thoughtful batch processing and background jobs. This avoids proprietary analytics engines until data volume demands it (beyond 1M users). The research identifies five critical pitfalls in Riot API integration (rate limiting blacklisting, data retention blindness, patch-blind caching, identity confusion, and eventual consistency mismanagement) that must be solved in Phase 1—there are no do-overs if these fail.

Authentication requires a staged approach: simple API key auth first (unblocks desktop app), then user JWT accounts later (adds complexity but enables personalization). The biggest risks are operational: rate limit violations causing permanent blacklisting, and misunderstanding Riot's data retention windows (2 years for matches, 1 year for timelines) leading to unrecoverable data gaps.

---

## Key Findings

### Recommended Stack

The technology stack builds on .NET 10 with strategic additions for caching, observability, authentication, and data operations. Full details in [STACK.md](.planning/research/STACK.md).

**Caching & Performance:**
- **HybridCache (10.2.0)** — Two-tier caching (memory + Redis) with stampede protection and tag-based invalidation; essential for expensive analytics aggregations that spike traffic
- **Redis via Microsoft.Extensions.Caching.StackExchangeRedis (10.0.2)** — Distributed cache backend; supports multi-worker deployments and live game data sharing

**Authentication & Authorization:**
- **JWT Bearer (10.0.2)** — Stateless user authentication; enables web client personalization with 15-30 minute tokens + refresh tokens
- **ASP.NET Core Identity (10.0.2)** — User account management with password hashing, roles, claims, and built-in metrics for monitoring sign-ins
- **Custom API Key Handler** — Lightweight authentication for desktop app; no package needed, built with ASP.NET Core's authentication middleware

**Observability & Reliability:**
- **OpenTelemetry (1.15.0+)** — Metrics, traces, logs collection with Prometheus export; CNCF standard replacing proprietary APM
- **Serilog (10.0.0)** — Structured logging with rich sink ecosystem; critical for debugging Riot API issues and background job execution
- **Health Checks (AspNetCore.HealthChecks.*)** — Database, Redis, Hangfire connectivity monitoring; exposes `/health` endpoints for orchestrators

**Data Operations:**
- **EFCore.BulkExtensions (10.0.0)** — 9-15x faster bulk inserts for match history ingestion; essential for background jobs processing 100+ matches
- **AutoMapper (16.0.0)** — DTO mapping reduces boilerplate for complex League data models (participants, runes, items)

**Built-in (No Install):**
- Response compression (Brotli/Gzip) — reduce JSON response bandwidth
- Rate limiting middleware — per-endpoint, per-IP/user policies protect against abuse

**Confidence: HIGH** — All packages have .NET 10 support verified via NuGet (released Jan 2026). Architecture avoids beta/preview packages except OpenTelemetry Prometheus exporter (1.15.0-beta.1, but stable).

### Expected Features

Analytics features cluster into four tiers based on dependency and user expectations. Full breakdown in [FEATURES.md](.planning/research/FEATURES.md).

**Table Stakes (Already Implemented or Easily Completed):**
- Summoner lookup, match history, KDA stats, rank display — core expected in every platform
- Champion mastery visualization, role-specific breakdowns — low effort with Mastery API

**Must-Have for MVP (Missing, High Priority):**
- **Live game lookup** (Spectator API) — critical for desktop app champ select feature; blocks other live features
- **Build recommendations** — expected baseline; aggregate high-winrate builds per champion/patch
- **Champion counters** — matchup analysis shows who wins against whom
- **Tier lists** — meta rankings by role/patch; traffic magnet
- **Recent performance trends** — win streaks, form indicators; simple calculation, high visibility

**Should-Have (Competitive Differentiators):**
- Pro build tracking — show what high-elo players build; requires pro account database but high engagement
- Head-to-head comparison — simple comparison view drives social value
- Regional meta differences — KR vs NA performance; interesting for competitive players

**Defer to v2+ (High Effort, Niche Value):**
- AI-powered insights — emerging differentiator but requires mature feature pipeline first
- Performance score (GPI) — sophisticated algorithm, strong engagement but not table stakes
- Win probability prediction — high-complexity ML, engaging but not must-have

**Anti-Features (Explicitly Avoid):**
- Real-time push notifications — minimal value for poll-friendly League updates
- Social features — scope creep; Riot handles comms
- Paywalling core analytics — users expect free stats, drives to competitors
- Overwolf dependency — increasingly rejected by users; build standalone
- Revealing strategic opponent patterns too deeply — crosses from helpful to unfair

**Dependency Tree:** Live game lookup → enables champ select features. Build recommendations → enables item/rune suggestions. Tier lists → enables meta insights. Champion counters → requires tier lists + matchup analysis.

**Confidence: MEDIUM** — Based on cross-referencing 5 major platforms (OP.GG, U.GG, Mobalytics, Porofessor, DPM.LOL) but no internal Riot requirements docs.

### Architecture Approach

The architecture extends the existing three-tier pattern (WebAPI → Service.Core → Data + PostgreSQL) with horizontal concerns: caching layer, authentication middleware, background jobs for live game polling. Full patterns in [ARCHITECTURE.md](.planning/research/ARCHITECTURE.md).

**Integration Strategy by Component:**

1. **Analytics Computation Layer** — Add IAggregationService in Service.Core with ICacheService abstraction for Redis integration. Existing services (SummonerStatsService, ChampionAnalysisService) inject cache to check before computing expensive LINQ aggregations. Cache-aside pattern with keys like `champion:89:stats:14.3` and 6-hour TTLs for low-volatility data.

2. **Live Game Polling** — Leverage existing Hangfire infrastructure; add LiveGamePollingJob that polls Spectator V4 API every 30-60 seconds for tracked summoners. Cache in Redis (2-minute TTL) to reduce API calls. Background job persists snapshots to PostgreSQL for historical analysis.

3. **Authentication System** — Dual-scheme middleware: API keys (custom handler) for desktop app, JWT Bearer for user accounts. Both schemes can satisfy `[Authorize]` attribute; specific policies enforce "AppOnly" (keys) or "UserOnly" (JWT). No changes to existing endpoints—auth added at middleware level.

4. **Management/Monitoring** — Extend Hangfire dashboard with custom pages for manual refresh, data purge, aggregate recomputation. Health check endpoints expose PostgreSQL/Redis/Hangfire status. IDataMaintenanceService handles bulk operations.

**Key Patterns to Follow:**
- Cache-aside with computation for analytics aggregations
- Background jobs for long-running operations (live game polling, bulk data refresh)
- Multiple authentication schemes with ASP.NET Core policies
- Repository + service layering for new entities (ApiKeys, UserAccounts, LiveGameSnapshots)

**Anti-Patterns to Avoid:**
- Caching in repositories — cache should be injected into services, not embedded in data access
- Real-time per-request Spectator API calls — exceeds rate limits; use background polling + Redis cache
- Manual auth parsing in controllers — middleware handles both schemes transparently
- Tight coupling to IDistributedCache — abstract behind ICacheService with domain-specific methods
- Heavy logic in Hangfire jobs — jobs orchestrate; services contain testable business logic

**Scalability Path:**
- 100-1K users: In-memory LINQ aggregations, no caching needed
- 1K-100K users: Add Redis caching (6-24 hour TTLs for aggregations)
- 100K-1M users: Pre-compute aggregations nightly, partition live game polling by region
- 1M+ users: Separate analytics DB (OLAP) from transactional (OLTP); ClickHouse/TimescaleDB for time-series data

**Confidence: HIGH** — Patterns verified with Microsoft Docs, community consensus. Live game polling specifics (polling intervals, state transitions) based on Spectator API characteristics and best practices (MEDIUM confidence).

### Critical Pitfalls

The domain has five showstopper pitfalls that must be solved in Phase 1; no recovery if handled incorrectly. Details and mitigation in [PITFALLS.md](.planning/research/PITFALLS.md).

1. **Rate Limit Blacklisting** — Hard-coding rate limits or ignoring regional enforcement causes permanent API blacklisting (403 on all requests, no recovery). Prevention: Read X-App-Rate-Limit headers dynamically, track per-region, implement exponential backoff on 429s, use atomic counters in multi-threaded context. Detection: 429 responses in logs, sudden 403 spike. Phase: Phase 1 (Core Data Pipeline) — no do-overs.

2. **Data Retention Blindness** — Matches kept 2 years, timelines kept 1 year. Not proactively archiving creates permanent data gaps. Match history endpoints return IDs for expired matches that 404 when requested. Prevention: Fetch match details within 23 months, timelines within 11 months; check match age before requesting; implement "last fetchable date" tracking. Detection: 404s for matches in history, DB with orphaned match IDs. Phase: Phase 1 (fetching logic) + Phase 2 (automated archival).

3. **Patch-Blind Static Data Caching** — Data Dragon updates after patches (can take 2 days), regions patch at different times, new champion IDs change. Caching without version awareness shows wrong abilities/items. Prevention: Version all static data fetches (`/cdn/{version}/data/{lang}/champion.json`), poll `versions.json` every 6 hours for new releases, implement versioned cache keys (`champion_data_14.2.1` not `champion_data`), graceful degradation for new IDs. Detection: User reports wrong champion abilities post-patch, errors on new champion IDs, cache spike on patch days. Phase: Phase 1 (versioned fetching) + Phase 3 (automated detection).

4. **Summoner Name API Dependency** — Summoner names became stale Nov 2023; Riot ID (gameName#tagLine) is now canonical. SummonerName field is deprecated and will be removed. Building around "Find by Name" breaks for new accounts. Prevention: PUUID as primary key (immutable), Riot ID as display (mutable). Lookup: Riot ID → PUUID → Summoner. Store gameName and tagLine separately. Phase: Phase 1 (architecture from start) — backporting is painful.

5. **Eventual Consistency Mismanagement** — Match appears in history before details process (30s-5min lag). Timeline data lags further. Users refresh immediately after game and see 404. Prevention: Retry logic with exponential backoff (30s, 60s, 120s, 300s, then hourly for 24h), queue-based processing, show processing state to users ("Match detected - processing stats"), separate availability checks for match vs timeline. Detection: High 404 rate for recently completed matches, user complaints about missing games. Phase: Phase 1 (basic retries) + Phase 2 (queue-based processing with state tracking).

**Secondary Pitfalls (Moderate Impact):**
- Live game polling inefficiency — Fixed intervals waste rate limits; adaptive polling by game state (5min offline, 30s lobby, 10s loading, 30-60s in-game)
- Missing data type gaps — Custom games completely inaccessible; some game modes don't generate detailed stats; requires feature planning with clear "not available" messaging
- Database schema inflexibility — Rigid schemas break when Riot adds fields; solution: store full API responses as JSONB, extract key fields for indexes
- Authentication security holes — Account takeovers if no Riot Sign-On verification; leaked API keys if stored plaintext; requires proper hashing and RSO integration
- Wrong API key type at launch — Development and personal keys have rate limits (20 req/sec); production keys need 4+ week approval; applying late kills launch

**Confidence: HIGH** — All verified with official Riot documentation, HextechDocs, and community consensus from multiple analytics platforms.

---

## Implications for Roadmap

Based on dependencies between components, architectural patterns, and pitfall avoidance, the research suggests six phases with clear deliverables and risk mitigation.

### Phase 1: Foundation - Caching Infrastructure & API Rate Limit Safety

**Rationale:** Caching and rate limiting are prerequisite for all downstream features (analytics, live game, auth all benefit). Rate limit safety is non-negotiable—if broken here, entire product blacklisted with no recovery.

**Delivers:**
- ICacheService abstraction + Redis integration (HybridCache + StackExchange.Redis)
- Dynamic rate limit reading from Riot headers (not hard-coded)
- Retention-aware API fetching (check match age before requesting)
- Retry logic with exponential backoff for eventual consistency
- Versioned static data fetching from Data Dragon

**Implements (STACK.md):**
- HybridCache (10.2.0), StackExchange.Redis (10.0.2), Serilog (10.0.0)
- Health checks for PostgreSQL/Redis
- Response compression + rate limiting middleware (built-in)

**Addresses (FEATURES.md):**
- None directly; foundation for all analytics features

**Avoids (PITFALLS.md):**
- Pitfall 1: Rate Limit Blacklisting
- Pitfall 2: Data Retention Blindness
- Pitfall 3: Patch-Blind Static Data Caching
- Pitfall 5: Eventual Consistency Mismanagement

**Success Criteria:**
- All analytics endpoints check cache before computing
- Rate limit headers logged and respected
- 404s on expired data detected and logged
- Static data versioned with Data Dragon version
- Match fetch retries succeed 99% of time within 5 minutes

### Phase 2: API Key Authentication & Desktop App Access

**Rationale:** Simpler than user JWT accounts; unblocks desktop app development immediately. API keys are stateless and require no password hashing complexity.

**Delivers:**
- Custom API Key authentication handler
- API key CRUD endpoints (management)
- `/health` endpoint for monitoring
- Key rotation/revocation support
- Desktop app can authenticate with X-API-Key header

**Implements (STACK.md):**
- ASP.NET Core built-in authentication (no package needed)
- OpenTelemetry (1.15.0) for monitoring key usage
- Health check endpoints

**Architecture (ARCHITECTURE.md):**
- ApiKey entity, IApiKeyRepository, IApiKeyService
- ApiKeyAuthenticationHandler middleware
- Multiple auth schemes (prepare for future JWT)

**Addresses (FEATURES.md):**
- Unblocks: Live game lookup, build recommendations, tier lists (need auth)
- May-have: Head-to-head comparison if protected

**Avoids (PITFALLS.md):**
- Pitfall 4: Summoner Name → use PUUID architecture here
- Pitfall 9: Authentication Security Holes (proper key hashing, no plaintext)

**Success Criteria:**
- Desktop app can include API key in requests
- Key management endpoints working
- Keys can be revoked without downtime
- Rate limiting per API key operational

### Phase 3: Analytics Computation - Tier Lists, Build Recommendations, Champion Counters

**Rationale:** Depends on Phase 1 (caching) and Phase 2 (auth). Aggregation queries are expensive and benefit from cache warming via background jobs. Core feature set users expect.

**Delivers:**
- IAggregationService for complex analytics
- Tier list endpoint (champions ranked by winrate/pick/ban per role)
- Build recommendations (runes, items, skill order by patch/rank)
- Champion counter matchup matrix
- Recent performance trends (win streaks, form)

**Implements (STACK.md):**
- EFCore.BulkExtensions (10.0.0) for efficient match data processing
- AutoMapper (16.0.0) for DTO mapping
- New analytics service endpoints

**Architecture (ARCHITECTURE.md):**
- Cache-aside pattern: check ICacheService before computing
- Background jobs for pre-computation of aggregations (6-24h TTLs)
- New database indexes for champion/matchup queries

**Addresses (FEATURES.md):**
- Must-have: Tier lists, build recommendations, champion counters, performance trends
- Quick-win: Role-specific deep dive enhancement

**Avoids (PITFALLS.md):**
- Pitfall 3: Patch-blind caching — tier lists versioned by patch
- Pitfall 7: Missing data types — document unsupported game modes

**Success Criteria:**
- Tier list endpoints respond <500ms (cached)
- Build recommendations accurate for top 10% player builds
- Matchup counters show consistent results with third-party tools
- Cache hit rate >80% on analytics queries

### Phase 4: Live Game Polling & Analysis

**Rationale:** Depends on Phase 1 (caching) and Phase 3 (analytics). Live game polling requires efficient caching to avoid rate limits. Can reuse Phase 3 analytics for enrichment.

**Delivers:**
- ILiveGameService wrapper around Spectator V4 API
- LiveGamePollingJob background polling every 30-60s
- Live game analysis enriching with player stats, champion counters
- LiveGameSnapshot persistence for accuracy tracking
- `/api/summoners/{id}/live-game` endpoint
- Redis cache (2-min TTL) for live game data

**Implements (STACK.md):**
- Hangfire recurring jobs (existing, no new package)
- Redis caching for transient live game state
- Health check for Spectator API availability

**Architecture (ARCHITECTURE.md):**
- Polling architecture (Spectator API pattern, not event-driven)
- Adaptive polling by game state (5min offline → 30s lobby → 10s loading → 60s in-game)
- ILiveGameSnapshotRepository for persistence
- ILiveGameAnalysisService enriches with stats

**Addresses (FEATURES.md):**
- Must-have: Live game lookup (critical for desktop app)
- Enables: Champ select overlay features, draft recommendations

**Avoids (PITFALLS.md):**
- Pitfall 6: Live Game Polling Inefficiency (adaptive polling, state transitions, batching)

**Success Criteria:**
- Live game data available <2s after detection
- Polling respects rate limits (max 200 req/10s budget)
- Game start/end transitions detected accurately
- Spectator API 404s handled gracefully (game ended)

### Phase 5: User Accounts & Personalization (JWT Auth)

**Rationale:** Complex due to password hashing, email verification, Riot Sign-On integration. Not critical for MVP but enables web client personalization (favorites, saved searches).

**Delivers:**
- UserAccount entity with password hashing (bcrypt)
- JWT Bearer token authentication (15-min access, 7-day refresh)
- User registration/login endpoints with email verification
- Riot Sign-On (RSO) OAuth flow for account linking
- Favorite summoners feature
- User-specific saved searches/filters

**Implements (STACK.md):**
- Microsoft.AspNetCore.Authentication.JwtBearer (10.0.2)
- Microsoft.AspNetCore.Identity.EntityFrameworkCore (10.0.2)
- System.IdentityModel.Tokens.Jwt (>=7.0.0)

**Architecture (ARCHITECTURE.md):**
- Multiple auth schemes: ApiKey (Phase 2) + Bearer (Phase 5)
- JwtAuthenticationHandler middleware
- IUserAccountService for registration/login
- IJwtService for token generation
- Separate authorization policies: "AppOnly" (API key) vs "UserOnly" (JWT)

**Addresses (FEATURES.md):**
- Should-have: Personalization features (favorites, preferences)
- Enables: Pro build tracking (can subscribe to pro players)

**Avoids (PITFALLS.md):**
- Pitfall 9: Authentication Security Holes (RSO for ownership verification, proper token encryption)

**Success Criteria:**
- Users can register with email/password
- JWT tokens validate correctly at middleware
- Riot Sign-On integration works (RSO flow complete)
- Password reset via email functional
- Token refresh extends session without new login

### Phase 6: Management, Monitoring & Production Hardening

**Rationale:** Ongoing throughout project but crystallizes after core features. Provides ops visibility and data governance.

**Delivers:**
- Extended Hangfire dashboard with management pages
- Manual summoner refresh endpoint
- Data purge controls (delete matches >90 days old)
- Aggregate recomputation triggers
- Health check UI with detailed status
- Structured logging to Seq/Elasticsearch (optional)
- OpenTelemetry metrics → Prometheus → Grafana dashboards

**Implements (STACK.md):**
- OpenTelemetry (1.15.0) + Prometheus (1.15.0-beta.1)
- Serilog (10.0.0) sinks for structured output
- AspNetCore.HealthChecks.* (9.0.0) for infra monitoring
- Hangfire.Dashboard.Management for custom pages

**Architecture (ARCHITECTURE.md):**
- IDataMaintenanceService for bulk operations
- Custom HealthCheck implementations (RiotApiHealthCheck, HangfireHealthCheck)
- Dashboard authentication (not just localhost)

**Addresses (FEATURES.md):**
- None directly; operational excellence

**Avoids (PITFALLS.md):**
- General operational reliability; catches issues early

**Success Criteria:**
- All health checks passing in dashboard
- Metrics exported to Prometheus/Grafana
- Admin can trigger manual refresh without code
- Old data purged automatically (no manual intervention)
- Rate limit usage visible in dashboards

### Phase Ordering Rationale

1. **Phase 1 first (Caching + Rate Limit Safety):** Non-negotiable foundation. Rate limit failure is permanent (blacklist). All downstream features depend on caching working. No shortcuts possible.

2. **Phase 2 before Phase 3 (API Keys before User Accounts):** Simpler authentication unblocks desktop app development in parallel. User accounts require email/password/OAuth complexity not needed for desktop.

3. **Phase 3 parallel with Phase 2:** Analytics computation doesn't depend on Phase 2; can be built simultaneously. Phase 1 (caching) is the only true blocker.

4. **Phase 4 after Phase 3:** Live game polling reuses Phase 3 analytics services for enrichment. Polling architecture depends on cache (Phase 1) and auth (Phase 2).

5. **Phase 5 after Phase 4:** User accounts are the most complex auth layer. Deferred until core features proven. Adds personalization, not critical path.

6. **Phase 6 throughout:** Health checks and monitoring added early (Phase 1+), enhanced incrementally as services added. No blocking dependency, improves visibility at every step.

**Dependency Graph:**
```
Phase 1 (Caching + Rate Limit Safety)
├─ Phase 2 (API Key Auth) ─┐
│                          └─ Phase 5 (User Accounts + JWT) [parallel development]
├─ Phase 3 (Analytics) ────┐
│                          └─ Phase 4 (Live Game Polling)
└─ Phase 6 (Monitoring) ────┤
                             └─ [Throughout all phases]
```

---

## Research Flags

### Phases Needing Deeper Research During Planning

- **Phase 1: Rate Limit Dynamics** — Riot's actual rate limit enforcement (application vs method limits, regional isolation) is documented but not exhaustively tested. Recommend building test harness early to verify behavior under load. Action: Create test suite that hits rate limits intentionally and validates retry logic.

- **Phase 4: Spectator V4 API Specifics** — Live game timestamps are reportedly unreliable (community consensus). Recommend reverse-engineering Spectator API behavior (game state transitions, latency, consistency) before finalizing polling intervals. Action: Implement polling prototype and compare observed timestamps vs actual game clock.

- **Phase 5: Riot Sign-On (RSO) Implementation** — Official RSO docs are sparse. Community examples exist but vary. Recommend contacting Riot Developer Relations to clarify PKCE flow, token handling, scopes before implementation. Action: Request RSO documentation review from Riot DevRel.

### Phases with Standard Patterns (Skip Research-Phase)

- **Phase 2: API Key Authentication** — ASP.NET Core multiple authentication schemes are well-documented in official Microsoft docs and community examples. Extensive StackOverflow coverage. No surprises expected. Action: Follow Code Maze tutorials, adapt example code.

- **Phase 3: Analytics Aggregations** — EF Core LINQ performance is well-understood (recent benchmarks available). Caching patterns standardized across .NET ecosystem. No exotic requirements. Action: Implement cache-aside pattern, monitor query performance.

- **Phase 6: Observability** — OpenTelemetry and Serilog are mature, standardized libraries. Prometheus integration proven at scale (widely used by analytics platforms). No special considerations. Action: Follow official getting-started guides, export to existing Prometheus/Grafana if available.

---

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| **Stack** | HIGH | All packages verified with .NET 10 support via NuGet (Jan 2026 releases). HybridCache, Redis, JWT Bearer, Identity, OpenTelemetry, Serilog have official Microsoft/CNCF backing. EFCore.BulkExtensions verified Jan 2026 release. No beta packages in critical path except OpenTelemetry Prometheus exporter (1.15.0-beta.1, but stable in practice). |
| **Features** | MEDIUM | Table stakes extracted from cross-referencing 5 major platforms (OP.GG, U.GG, Mobalytics, Porofessor, DPM.LOL). Competitive differentiators inferred from platform comparisons but not validated with actual user research. MVP recommendations are strong but missing direct Riot feedback on priorities. |
| **Architecture** | HIGH | Layered patterns (WebAPI → Service.Core → Data) are standard Microsoft guidance. Caching with Redis verified with official Docs. Multiple authentication schemes documented extensively with community examples. Live game polling pattern based on Spectator API characteristics + general API polling best practices. Hangfire integration proven by existing background job use. |
| **Pitfalls** | HIGH | All critical pitfalls (rate limiting, data retention, patch caching, summoner IDs, eventual consistency) verified with official Riot documentation, HextechDocs community resources, and Riot API Libraries. Secondary pitfalls (polling efficiency, schema flexibility, auth security) backed by community patterns and best practices. Confidence reduced only to HIGH (not EXTREMELY HIGH) due to rate limit enforcement details being sparsely documented. |

**Overall Confidence:** **HIGH** — Research is grounded in official Microsoft and Riot documentation with community consensus on patterns. Stack recommendations have explicit .NET 10 verification. Architecture patterns follow established Microsoft guidance. Pitfalls are well-documented in official Riot resources. The main uncertainties (rate limit enforcement nuances, Spectator API timestamp reliability, RSO documentation completeness) are lower-risk and resolvable during Phase 1 with prototyping.

### Gaps to Address

1. **Rate Limit Enforcement Dynamics** — Official Riot docs explain rate limits but don't specify how "application rate limit" vs "method rate limit" interact under load or how blacklisting escalation works. Mitigation: Build rate limit test harness in Phase 1 to observe behavior empirically.

2. **Riot Sign-On (RSO) Details** — RSO is described at high level but implementation specifics (token expiration, refresh flow, scopes for different API endpoints) are sparse. Mitigation: Contact Riot Developer Relations during Phase 5 planning; use community examples as interim reference.

3. **User Feature Priority Validation** — MVP feature list inferred from competitive analysis, not from actual Transcendence user research. If user base prioritizes different features (e.g., historical rank charts over live game), roadmap shifts. Mitigation: Validate with stakeholders before Phase 3; adjust feature order if needed.

4. **Performance Thresholds at Scale** — Caching strategy assumes LINQ aggregations are "fast enough" for <1M users. No benchmarks for Transcendence-specific query patterns (e.g., matchup matrix computation across millions of matches). Mitigation: Monitor Phase 3 query performance in staging; add materialized views/pre-computation if P99 latency exceeds 500ms.

5. **Data Dragon Patch Timing** — Assumption: patch detection every 6 hours is sufficient. Riot patches affect regions at different times; some regions may be ahead. Mitigation: Implement automated alerts for Data Dragon version changes; add manual override for rapid patch day response.

---

## Sources

### Official Microsoft Documentation (HIGH Confidence)
- [HybridCache in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/hybrid?view=aspnetcore-10.0)
- [Distributed caching in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed?view=aspnetcore-10.0)
- [Authentication in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/?view=aspnetcore-10.0)
- [ASP.NET Core Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0)
- [Health checks in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0)
- [Rate limiting middleware](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)
- [Response compression](https://learn.microsoft.com/en-us/aspnet/core/performance/response-compression?view=aspnetcore-10.0)

### Official Riot Documentation (HIGH Confidence)
- [Rate Limiting — Riot Developer Portal](https://developer.riotgames.com/docs/portal)
- [Summoner Name to Riot ID](https://www.riotgames.com/en/DevRel/summoner-names-to-riot-id)
- [Summoner Name to Riot ID FAQ](https://developer.riotgames.com/docs/summoner-name-to-riot-id-faq)
- [RSO (Riot Sign On)](https://support-developer.riotgames.com/hc/en-us/articles/22801670382739-RSO-Riot-Sign-On)

### Community & Best Practices (HIGH-MEDIUM Confidence)
- [Riot API Libraries — Collecting Data](https://riot-api-libraries.readthedocs.io/en/latest/collectingdata.html)
- [Riot API Libraries — Info About Specific Data](https://riot-api-libraries.readthedocs.io/en/latest/specifics.html)
- [Riot API Libraries — Data Dragon](https://riot-api-libraries.readthedocs.io/en/latest/ddragon.html)
- [HextechDocs — Rate Limiting](https://hextechdocs.dev/rate-limiting/)
- [OpenTelemetry .NET Getting Started](https://opentelemetry.io/docs/languages/dotnet/getting-started/)
- [EFCore.BulkExtensions GitHub](https://github.com/borisdj/EFCore.BulkExtensions)
- [Serilog GitHub](https://github.com/serilog/serilog)
- [API Polling Best Practices](https://www.merge.dev/blog/api-polling-best-practices)

### Domain Analysis (MEDIUM Confidence)
- OP.GG, U.GG, Mobalytics, Porofessor, DPM.LOL platform comparison (cross-referenced features, limitations, positioning)
- 2026 League patch cycle and release schedule (Patch/2026 Annual Cycle - LoL Wiki)

---

**Research Completion:** 2026-02-01
**Next Step:** Ready for roadmap creation using phase structure above
**Last Review:** 2026-02-01
