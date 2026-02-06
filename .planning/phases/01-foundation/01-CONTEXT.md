# Phase 1: Foundation - Context

**Gathered:** 2026-02-01
**Status:** Ready for planning

<domain>
## Phase Boundary

Establish robust caching and rate limit safety that prevents API blacklisting and enables all downstream features. This is invisible infrastructure—no user-facing features, but critical for everything built on top.

Requirements in scope:
- INFRA-01: Static data (champions, items, runes) auto-updates on patch releases
- INFRA-02: Two-tier caching with memory (L1) and Redis (L2)

</domain>

<decisions>
## Implementation Decisions

### Cache Invalidation Strategy
- Claude's discretion on patch detection timing (gradual overlap vs immediate)
- Claude's discretion on cache key versioning strategy
- Claude's discretion on L1/L2 TTL relationship (based on HybridCache patterns)
- Claude's discretion on stampede protection approach (single-flight vs stale-while-revalidate)

### Rate Limit Handling
- Trust Camille library for rate limit enforcement (already handles Riot API headers internally)
- Add monitoring layer on top of Camille—don't add custom throttling, but observe what's happening
- Claude's discretion on logging vs metrics approach for rate limit events
- Claude's discretion on background job auto-pause behavior during API outages

### Retry & Failure Behavior
- Claude's discretion on retry schedule for eventual consistency (30s-5min delays for match data)
- Goal is building historical depth—fetch and preserve data while it exists within retention windows
- Mark permanently failed fetches as "unfetchable" in database to prevent infinite retry loops
- Claude's discretion on whether failure status is exposed via API or admin-only

### Data Freshness Policy
- Claude's discretion on static data patch check frequency
- Rank data should update near real-time (< 5 minutes after game ends)
- Hybrid match fetching: proactive when API capacity available, prioritize active users
- Include data age metadata in API responses so clients can show freshness

</decisions>

<specifics>
## Specific Ideas

- Using Camille.RiotGames library (v3.0.0-nightly) for Riot API access—has built-in rate limiting
- Long-term historical data preservation is a goal—fetch aggressively while data is within retention windows
- Clients need to know data freshness, so include "as of" timestamps in responses

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 01-foundation*
*Context gathered: 2026-02-01*
