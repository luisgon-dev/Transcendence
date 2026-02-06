# Domain Pitfalls: League of Legends Analytics Backend

**Domain:** League of Legends analytics platform (op.gg/u.gg-like service)
**Researched:** 2026-02-01
**Confidence:** HIGH (verified with official Riot documentation and community resources)

## Executive Summary

Building a League analytics backend involves navigating Riot's complex API constraints, managing data that expires and changes format frequently, and handling scale issues that only appear when tracking millions of matches. The most costly mistakes are:

1. **Rate limiting mismanagement** - Hard-coding limits or ignoring regional enforcement leads to blacklisting
2. **Data retention blindness** - Not planning for match data (2 years) and timeline data (1 year) expiration
3. **Stale cache disasters** - Caching static data across patch boundaries without version awareness
4. **Authentication token confusion** - Mixing PUUIDs, summonerIDs, and deprecated summoner names
5. **Eventual consistency assumptions** - Expecting immediate data availability after match completion

---

## Critical Pitfalls

Mistakes that cause rewrites, service outages, or permanent API access revocation.

### Pitfall 1: Rate Limit Blacklisting

**What goes wrong:** Application gets permanently blacklisted (403 on all requests) from Riot API.

**Why it happens:**
- Hard-coding rate limit values instead of reading response headers dynamically
- Not tracking limits per-region (limits are regional, not global)
- Confusing method rate limits (per-endpoint) with service rate limits (shared across apps)
- Running multi-threaded requests without atomic counters across threads
- Repeatedly calling non-existent endpoints during development

**Consequences:**
- Permanent 403 responses - no recovery without new API key
- Production applications go dark immediately
- Takes weeks to get new production key approved

**Prevention:**
```
REQUIRED IMPLEMENTATION:
1. Read X-App-Rate-Limit and X-Method-Rate-Limit headers dynamically
2. Maintain separate rate limit tracking per region
3. Implement shared atomic counters for multi-threaded apps
4. Add exponential backoff for 429 responses
5. Cache 404s to avoid repeated calls to non-existent endpoints

NEVER:
- Hard-code rate limit values (they change without notice)
- Assume global rate limits (they're per-region)
- Ignore Retry-After headers on 429 responses
```

**Detection:**
- 429 responses appearing in logs
- X-Rate-Limit-Type headers showing "application" or "method" violations
- Sudden increase in 403 responses (blacklist has begun)

**Phase mapping:** Must be implemented in Phase 1 (Core Data Pipeline) - no recovery if done wrong.

**Sources:**
- [Rate Limiting documentation - Riot Developer Portal](https://developer.riotgames.com/docs/portal)
- [Rate Limiting - HextechDocs](https://hextechdocs.dev/rate-limiting/)
- [Riot API Libraries - Collecting Data](https://riot-api-libraries.readthedocs.io/en/latest/collectingdata.html)

---

### Pitfall 2: Data Retention Blindness

**What goes wrong:** Application tries to fetch match/timeline data that no longer exists, creating permanent gaps in historical data.

**Why it happens:**
- Not understanding Riot's retention policy: **matches kept 2 years, timelines kept 1 year**
- Match history endpoints return IDs for matches older than 2 years that no longer exist in match detail endpoints
- No proactive archival strategy for data approaching expiration
- Assuming "if we have the match ID, we can fetch the data"

**Consequences:**
- Permanent data gaps - once timeline expires (1 year), it's gone forever
- Match history pagination returns IDs for matches that 404 when requested
- Historical analysis features break for older accounts
- Database contains match IDs with no corresponding match details

**Prevention:**
```
REQUIRED STRATEGY:
1. Fetch and archive match details within 23 months of match date
2. Fetch and archive timeline data within 11 months of match date
3. Check match age before requesting - don't waste rate limits on expired data
4. Implement "last fetchable date" tracking per match ID
5. Set up alerts for data approaching expiration threshold

DATA RETENTION TIMELINE:
- 0-11 months: Match + Timeline available
- 11-24 months: Match only (timeline expired)
- 24+ months: Nothing available (match expired)
```

**Detection:**
- 404 responses when fetching match details from match history
- Match IDs in database without corresponding match data
- Increasing percentage of "no timeline" matches in older data

**Phase mapping:**
- Phase 1: Implement retention-aware fetching
- Phase 2: Add automated archival for data approaching expiration

**Sources:**
- [Info About Specific Data - Riot API Libraries](https://riot-api-libraries.readthedocs.io/en/latest/specifics.html)
- [Match history pagination issues - GitHub](https://github.com/RiotGames/developer-relations/issues/868)

---

### Pitfall 3: Patch-Blind Static Data Caching

**What goes wrong:** Application shows wrong champion abilities, incorrect item stats, or crashes when new champions release.

**Why it happens:**
- Caching Data Dragon (DDragon) static data without version awareness
- Not monitoring https://ddragon.leagueoflegends.com/api/versions.json for new versions
- Assuming DDragon updates immediately after patches (can take 2 days)
- Not handling regional patch timing differences (regions patch at different times)
- Hard-coding champion/item IDs that change or get removed

**Consequences:**
- Wrong builds shown to users (items changed/removed)
- Champion abilities display incorrect data
- Application crashes on new champion IDs
- Users lose trust in data accuracy after patch day

**Prevention:**
```
REQUIRED IMPLEMENTATION:
1. Version all static data queries: /cdn/{version}/data/{language}/champion.json
2. Poll versions.json every 6 hours to detect new DDragon releases
3. Check per-region version via realms files (different regions = different patches)
4. Implement versioned cache keys: cache["champion_data_14.2.1"] not cache["champion_data"]
5. Graceful degradation when new IDs appear before DDragon updates

CACHE STRATEGY:
- Champion base stats: 24 hours with version key
- Item data: 12 hours with version key
- Summoner spells: 7 days with version key
- Profile icons: 30 days (rarely change)

PATCH DAY PROTOCOL:
- Clear all static data caches on patch detection
- Wait 24-48 hours before assuming DDragon has new patch data
- Display "Patch X data updating" message if DDragon lags
```

**Detection:**
- Users reporting wrong champion abilities after patches
- Errors when fetching data for new champions/items
- Cache misses spike on patch days
- Item build recommendations using deleted items

**Phase mapping:**
- Phase 1: Implement versioned static data fetching
- Phase 3: Add automated patch detection and cache invalidation

**Sources:**
- [Data Dragon documentation - Riot API Libraries](https://riot-api-libraries.readthedocs.io/en/latest/ddragon.html)
- [Patch/2026 Annual Cycle - LoL Wiki](https://wiki.leagueoflegends.com/en-us/Patch/2026_Annual_Cycle)

---

### Pitfall 4: Summoner Name API Dependency

**What goes wrong:** Application breaks when trying to look up summoners or relies on summoner names that are stale/deprecated.

**Why it happens:**
- Using deprecated /summoner/v4/summoners/by-name/{summonerName} endpoint
- Storing summoner names as primary identifiers instead of PUUIDs
- Not understanding that summonerName field became stale on November 20, 2023
- Building features around "Find by Summoner Name" instead of Riot ID (gameName#tagLine)
- Assuming summoner names are unique (they're not - Riot IDs are unique)

**Consequences:**
- Summoner lookup fails for accounts created after Nov 2023 (random UUID string as name)
- Database has outdated summoner names that don't match current names
- User searches fail because they search by current name but DB has old name
- Application breaks entirely when Riot removes summonerName field from API (planned)

**Prevention:**
```
REQUIRED MIGRATION:
1. Use PUUID as primary identifier - never summoner name or summoner ID
2. Lookup flow: Riot ID → PUUID → Summoner data
   /riot/account/v1/accounts/by-riot-id/{gameName}/{tagLine} → get PUUID
   /lol/summoner/v4/summoners/by-puuid/{encryptedPUUID} → get summoner
3. Store both gameName and tagLine separately (format: "gameName#tagLine")
4. Treat displayed names as display-only - never as identifiers

SCHEMA DESIGN:
players table:
  - puuid (primary key, immutable)
  - game_name (mutable display field)
  - tag_line (mutable display field)
  - summoner_id (regional, mutable)
  - last_updated (timestamp)

NEVER use summoner_name as identifier or foreign key.
```

**Detection:**
- Failed lookups for new accounts
- Users reporting "can't find my account" after name changes
- Stale name data in database (names displayed don't match in-game)

**Phase mapping:**
- Phase 1: Use PUUID-based architecture from start
- Phase 4: Add summoner name → Riot ID migration for legacy users

**Sources:**
- [Summoner Name to Riot ID - Official Riot](https://www.riotgames.com/en/DevRel/summoner-names-to-riot-id)
- [Summoner Name to Riot ID FAQ - Developer Portal](https://developer.riotgames.com/docs/summoner-name-to-riot-id-faq)
- [PUUIDs and Other IDs - Riot API Libraries](https://riot-api-libraries.readthedocs.io/en/latest/ids.html)

---

### Pitfall 5: Eventual Consistency Mismanagement

**What goes wrong:** Users refresh immediately after match and see "Match not found" or partial/incorrect stats.

**Why it happens:**
- Assuming match data is immediately available after game ends
- Match processing takes time - match appears in history before details are available
- Timeline data processes separately and may lag behind match data
- During high load (patch day, major events), processing delays increase
- Different endpoints may return inconsistent data during processing window

**Consequences:**
- "Match not found" errors when users refresh immediately after game
- Partial stats showing (kills/deaths/assists but no items or detailed timeline)
- User perception: "Your site is broken, [competitor] already has my match"
- Support tickets flood in after patch releases or major tournaments

**Prevention:**
```
REQUIRED HANDLING:
1. Implement retry logic with exponential backoff:
   - Match history returns ID → wait 30s before fetching details
   - Details 404 → retry after 30s, 60s, 120s, 300s (max 5 retries)
2. Queue-based match processing (don't fetch synchronously on user request)
3. Show processing state to users:
   "Match detected - processing stats (usually 1-2 minutes)"
4. Separate availability checks for match vs timeline data
5. Graceful degradation: show basic stats if timeline unavailable

PROCESSING STATES:
- Detected: Match ID in history, details not available
- Processing: Details available but incomplete
- Partial: Details complete, timeline unavailable
- Complete: All data available

RETRY STRATEGY:
attempt 1: immediate
attempt 2: +30s (match might still be processing)
attempt 3: +60s (90s total - most matches available)
attempt 4: +120s (210s total - high load scenarios)
attempt 5: +300s (510s total - patch day/major events)
after 5: mark as "delayed" and retry hourly for 24h
```

**Detection:**
- High rate of 404s for recently completed matches
- User complaints about missing recent matches
- Match details without timeline data (normal for >1 year, problem for recent)
- Spike in processing delays after patches

**Phase mapping:**
- Phase 1: Basic retry logic for match fetching
- Phase 2: Queue-based processing with status tracking
- Phase 3: User-facing processing state indicators

**Sources:**
- [Info About Specific Data - Riot API Libraries](https://riot-api-libraries.readthedocs.io/en/latest/specifics.html)
- [Understanding Data Consistency - Multiple sources on eventual consistency](https://www.metaplane.dev/blog/data-consistency-definition-examples)

---

## Moderate Pitfalls

Mistakes that cause performance issues, technical debt, or feature limitations.

### Pitfall 6: Live Game Polling Inefficiency

**What goes wrong:** Live game tracking consumes excessive API quota or has significant lag.

**Why it happens:**
- Polling at fixed intervals regardless of game state
- Polling too frequently (wasting rate limits) or too slowly (missing game starts)
- Not detecting game state transitions (lobby → loading → in-game → finished)
- Spectator API has inconsistent/inaccurate timestamps - relying on them causes issues
- Fetching full game data when only checking if game is still active

**Prevention:**
```
ADAPTIVE POLLING STRATEGY:
Game states and polling frequency:
- Player offline/not in game: 5 minutes
- Player in lobby: 30 seconds
- Player in loading screen: 10 seconds
- Game in progress (first 5min): 30 seconds (lots happens early)
- Game in progress (after 5min): 60 seconds (more stable)
- Game finished detected: stop polling, queue for match fetch

OPTIMIZATION:
1. Track last-known state to detect transitions
2. Use lightweight endpoints first (/lol/spectator/v5/active-games)
3. Batch players in same game (one request serves all)
4. Implement circuit breaker for players who disconnect
5. Don't rely on spectator timestamps - use your own tracking

RATE LIMIT BUDGETING:
With 500 req/10s production limit:
- 100 req/10s: live game tracking (200 active players max)
- 200 req/10s: match history fetching (main workload)
- 100 req/10s: summoner lookups and static queries
- 100 req/10s: buffer for burst traffic
```

**Detection:**
- Rate limit exhaustion during peak hours
- Live game data showing "Game started X minutes ago" but displaying old state
- Missing game start/end transitions
- Users reporting live game feature is "slow" or "doesn't update"

**Phase mapping:**
- Phase 2: Implement adaptive polling
- Phase 3: Add state transition detection and batching

**Sources:**
- [7 best practices for polling API endpoints](https://www.merge.dev/blog/api-polling-best-practices)
- [Building real-time apps - polling frequency best practices](https://www.sportmonks.com/blogs/building-a-real-time-livescore-app-with-a-football-api-best-practices/)

---

### Pitfall 7: Missing Data Type Gaps

**What goes wrong:** Analytics features fail because critical data isn't available via API.

**Why it happens:**
- Assuming all game modes provide data (many don't: Ultimate Spellbook, Arena, custom games)
- Not knowing that custom games (except tournament realm) are completely inaccessible
- Expecting detailed bot match data (bot matches don't generate stats)
- Planning features around replay file data (reverse engineering .rofl files violates ToS)
- Not accounting for game mode rotations and special events

**Consequences:**
- "No data available" for entire categories of matches users expect to see
- Features like "full gameplay replay analysis" impossible (would require ToS violation)
- Custom game statistics unavailable (major gap for content creators and teams)
- Data gaps in match history that confuse users

**Prevention:**
```
DATA AVAILABILITY MATRIX:
Game Mode          | Match Data | Timeline | Stats
-------------------|------------|----------|-------
Ranked Solo/Duo    | YES        | YES      | YES
Ranked Flex        | YES        | YES      | YES
Normal Draft       | YES        | YES      | YES
ARAM              | YES        | YES      | YES
Bot Games         | YES        | YES      | NO (bot info missing)
Custom Games      | NO         | NO       | NO (privacy policy)
Special Modes     | PARTIAL    | PARTIAL  | PARTIAL (varies)

FEATURE PLANNING:
1. Check game mode before promising features
2. Document unsupported modes in user-facing docs
3. Show "Not available for this game type" instead of errors
4. For special events, verify data availability before building features
5. Plan RSO-based custom game support for future (Riot working on it)

WORKAROUNDS:
- Use community sources (Leaguepedia) for esports data
- Leverage community-hosted match ID lists (Canisback's lists)
- Clear communication about limitations
```

**Detection:**
- Users reporting missing matches (check if they're custom/special modes)
- Error rates spike during special event modes
- Support tickets asking "Where are my custom game stats?"

**Phase mapping:**
- Phase 1: Document data availability clearly
- Phase 3: Implement game mode detection and graceful degradation
- Phase 5: Consider RSO integration for custom game access (if available)

**Sources:**
- [Info About Specific Data - Riot API Libraries](https://riot-api-libraries.readthedocs.io/en/latest/specifics.html)
- [League of Legends data scraping challenges](https://maknee.github.io/blog/2025/League-Data-Scraping/)

---

### Pitfall 8: Database Schema Inflexibility

**What goes wrong:** Schema can't handle Riot's frequent data model changes, leading to failed inserts or data loss.

**Why it happens:**
- Defining strict schemas for JSON data that changes frequently (items, runes, champions)
- Not accounting for Riot adding fields to API responses
- Using auto-increment IDs instead of Riot's provided IDs (PUUID, matchId)
- Not versioning schema to handle historical data with different structure
- Poor indexing strategy for common query patterns

**Consequences:**
- Application crashes when Riot adds new fields or changes types
- Can't query historical data efficiently (queries time out)
- Data migration nightmares when schema needs updates
- Can't distinguish between "missing because old version" vs "missing because error"

**Prevention:**
```
SCHEMA BEST PRACTICES:

1. IDENTITY FIELDS:
   - ALWAYS use Riot's IDs (PUUID, matchId, championId)
   - NEVER use auto-increment as primary key
   - Multiple ID types exist for legacy reasons - PUUID is authoritative

2. JSON STORAGE:
   - Store full API responses as JSON (JSONB in Postgres)
   - Extract key fields for indexing/querying
   - Allows future queries on fields you didn't initially plan for

3. INDEXING STRATEGY:
   matches table:
     - match_id (primary key)
     - game_creation (timestamp) - index for recency queries
     - queue_id - index for mode filtering
     - participants (array of PUUIDs) - GIN index for "find player in match"

   player_stats table:
     - (match_id, puuid) composite primary key
     - champion_id - index for champion performance
     - (puuid, game_creation) composite index for player history

4. VERSIONING:
   - Track API version that returned data: api_version field
   - Allows handling schema differences in historical data
   - Critical for migrations and data quality checks

5. DENORMALIZATION:
   - Denormalize for read-heavy analytics queries
   - Pre-compute aggregates (champion winrates, item popularity)
   - Refresh materialized views hourly/daily based on data freshness needs

AVOID RIOT'S MISTAKES:
Riot's own account database had issues with multiple ID fields - don't repeat it.
Use single authoritative ID (PUUID) with regional identifiers separate.
```

**Detection:**
- Insert failures after API changes
- Slow query performance (missing indexes)
- "Field not found" errors in application
- Can't run analytics queries without timeouts

**Phase mapping:**
- Phase 1: Design flexible schema with JSON storage
- Phase 2: Add proper indexing for query patterns
- Phase 4: Implement materialized views for analytics

**Sources:**
- [Globalizing Player Accounts - Riot's ID mistakes](https://technology.riotgames.com/news/globalizing-player-accounts)
- [Database optimization techniques 2025](https://nextnative.dev/blog/database-optimization-techniques)
- [Query Optimization - Supabase Docs](https://supabase.com/docs/guides/database/query-optimization)

---

### Pitfall 9: Authentication Security Holes

**What goes wrong:** Users claim accounts they don't own, leak API keys, or authentication bypass occurs.

**Why it happens:**
- Not implementing summoner verification (proving account ownership)
- Using weak verification (e.g., "just change your icon to verify")
- Exposing Riot API keys in client-side code
- Not using Riot Sign-On (RSO) for sensitive operations
- Implementing OAuth flow incorrectly (no HTTPS, weak state validation)
- Storing user tokens without encryption

**Consequences:**
- Account takeover - users claim someone else's profile
- Riot API key leaked and abused (key revocation, blacklist)
- User data exposure
- Failed security audit, potential legal issues (GDPR, privacy laws)

**Prevention:**
```
AUTHENTICATION LAYERS:

1. RIOT SIGN-ON (RSO) FOR ACCOUNT OWNERSHIP:
   - Required for features like "claim this profile" or "private match access"
   - OAuth 2.0 flow with PKCE (Proof Key for Code Exchange)
   - MUST use HTTPS - Riot requires secure connections
   - Validate state parameter to prevent CSRF attacks

   Implementation:
   - User clicks "Verify with Riot"
   - Redirect to Riot OAuth with state token
   - Riot redirects back with authorization code
   - Exchange code for access token (server-side only)
   - Fetch /riot/account/v1/accounts/me to get PUUID
   - Match PUUID with claimed account

2. SUMMONER VERIFICATION (LEGACY FALLBACK):
   - Generate unique verification code
   - User sets specific summoner icon or changes Riot ID temporarily
   - Verify via API that change occurred
   - Less secure than RSO, deprecated but sometimes necessary

3. API KEY SECURITY:
   - NEVER expose Riot API keys client-side
   - Rotate keys on schedule (every 90 days minimum)
   - Use environment variables, never commit to git
   - Different keys for dev/staging/production
   - Monitor key usage for anomalies

4. SESSION MANAGEMENT:
   - Short-lived access tokens (15 minutes)
   - Refresh tokens with rotation
   - Encrypt tokens at rest
   - HTTP-only cookies for web clients
   - Track session origin (IP, device fingerprint)

SECURITY CHECKLIST:
- [ ] RSO OAuth implemented with HTTPS
- [ ] State parameter validated (CSRF protection)
- [ ] API keys in environment variables only
- [ ] No API keys in client code (check with grep)
- [ ] Access tokens encrypted in database
- [ ] Session timeout implemented
- [ ] Account ownership verification required for profile claims
```

**Detection:**
- Users reporting accounts they don't own showing as "theirs"
- API key showing up in git history or client bundles
- Sudden API usage spikes (key leaked and abused)
- Authentication bypass attempts in logs

**Phase mapping:**
- Phase 1: API key security (basic hygiene)
- Phase 4: RSO integration for account verification
- Phase 5: Full session management and security hardening

**Sources:**
- [RSO (Riot Sign On) - Developer Relations](https://support-developer.riotgames.com/hc/en-us/articles/22801670382739-RSO-Riot-Sign-On)
- [Building Secure Riot Authentication System](https://fuziion.nl/blog/building-a-secure-riot-games-authentication-system-with-php)
- [SSO Protocol Security 2025](https://guptadeepak.com/security-vulnerabilities-in-saml-oauth-2-0-openid-connect-and-jwt/)
- [2026: Turning point for fraud prevention in gaming](https://www.gamingintelligence.com/insight/2026-the-turning-point-for-fraud-prevention/)

---

### Pitfall 10: Ignoring Production vs Development API Keys

**What goes wrong:** Launching public service with development or personal API key, hitting rate limits immediately.

**Why it happens:**
- Not understanding key type limits: dev (very low), personal (20/sec), production (500/10sec)
- Assuming personal key is "good enough" for small user base
- Not planning for production key approval timeline (weeks)
- Thinking "we'll upgrade later" without testing production scenarios
- Not knowing production keys require application approval and review

**Consequences:**
- Service launches, immediately rate limited, unusable
- Users flood in, can only serve 20 req/sec with personal key (dies instantly)
- Emergency scramble for production key (takes weeks to approve)
- Reputation damage from launch disaster
- Violation of ToS (running public product with dev/personal key)

**Prevention:**
```
KEY TYPES AND LIMITS:

Development Key:
- Expires every 24 hours
- Very low limits (exact limits not published)
- For testing only
- NEVER use for any public-facing product

Personal Key:
- 20 requests/second per region
- 100 requests/2 minutes per region
- For personal projects with limited users
- CANNOT be used for public products per ToS

Production Key:
- 500 requests/10 seconds per region
- 30,000 requests/10 minutes per region
- Requires application approval
- Review process takes 2-4 weeks minimum
- Must demonstrate product value and proper API usage

PRODUCTION KEY APPLICATION:
1. Start application BEFORE building product (parallel track)
2. Demonstrate:
   - Proper rate limiting implementation
   - API key security
   - Clear value proposition for players
   - Professional product quality
3. Show working prototype (can use personal key for demo)
4. Plan for 4-week approval timeline
5. Have monitoring ready to prove you won't abuse limits

CAPACITY PLANNING:
Personal key (20 req/sec) supports roughly:
- 100 concurrent users doing light browsing
- 20 concurrent users doing heavy analytics
- NOT suitable for public launch

Production key (500 req/10sec = 50 req/sec sustained) supports:
- 5,000 concurrent users with caching
- 500 concurrent users doing analytics
- Real public product scale

LAUNCH STRATEGY:
Phase 1-3: Build with personal key, limited alpha/beta
Phase 4: Submit production key application
Phase 5: Wait for approval (2-4 weeks)
Phase 6: Public launch with production key
```

**Detection:**
- Rate limit errors immediately at launch
- Can only serve small number of concurrent users
- Users reporting "site is slow/broken"
- Realizing key type mistake after launch (too late)

**Phase mapping:**
- Phase 0 (Planning): Understand key requirements and timeline
- Phase 1-3: Build with personal key, document production readiness
- Phase 4: Submit production key application (before launch)
- Phase 5: Receive approval, migrate to production key
- Phase 6: Public launch

**Sources:**
- [Rate Limiting documentation - Riot Developer Portal](https://developer.riotgames.com/docs/portal)
- [Riot API Libraries - Best Practices](https://riot-api-libraries.readthedocs.io/en/latest/collectingdata.html)

---

## Minor Pitfalls

Issues that cause annoyance but are easily fixable.

### Pitfall 11: Season ID Staleness

**What goes wrong:** Season-based queries return wrong data or no data.

**Why it happens:** Season IDs in API lag months behind actual season changes.

**Prevention:** Use timestamps and reference Community Dragon patches.json for season boundaries, not seasonId field.

**Phase mapping:** Phase 3 (Analytics) - when season-based queries matter.

**Sources:**
- [Info About Specific Data - Riot API Libraries](https://riot-api-libraries.readthedocs.io/en/latest/specifics.html)

---

### Pitfall 12: Timestamp Unit Confusion

**What goes wrong:** Dates display as year 50000 or January 1, 1970.

**Why it happens:** Riot API uses milliseconds, many libraries expect seconds.

**Prevention:** Always divide by 1000 when converting to Date objects. Add unit tests for timestamp conversion.

**Phase mapping:** Phase 1 - catch early in development.

---

### Pitfall 13: totalGames Field Unreliability

**What goes wrong:** Displaying "You've played X games" with wrong number.

**Why it happens:** totalGames field in match history is unreliable and should be ignored per official docs.

**Prevention:** Count match IDs in paginated responses, don't trust totalGames field.

**Phase mapping:** Phase 2 - affects profile statistics.

**Sources:**
- [Info About Specific Data - Riot API Libraries](https://riot-api-libraries.readthedocs.io/en/latest/specifics.html)

---

### Pitfall 14: Pick Order Confusion

**What goes wrong:** Displaying draft order wrong in match timeline.

**Why it happens:** Pick order is 0-5-1-6-2-7-3-8-4-9, not sequential.

**Prevention:** Use documented pick order sequence, don't assume linear.

**Phase mapping:** Phase 3 - when showing detailed draft analysis.

**Sources:**
- [Info About Specific Data - Riot API Libraries](https://riot-api-libraries.readthedocs.io/en/latest/specifics.html)

---

## Phase-Specific Warnings

| Phase Topic | Likely Pitfall | Mitigation |
|-------------|---------------|------------|
| Phase 1: Core Data Pipeline | Rate limit blacklisting | Implement dynamic rate limiting from day 1 |
| Phase 1: Data Fetching | Data retention blindness | Build retention-aware fetching logic |
| Phase 2: Live Game Tracking | Polling inefficiency | Use adaptive polling based on game state |
| Phase 2: Match Processing | Eventual consistency | Implement retry queues with exponential backoff |
| Phase 3: Static Data Integration | Patch-blind caching | Version all static data with DDragon version |
| Phase 4: Authentication | Summoner name dependency | Use PUUID/Riot ID architecture from start |
| Phase 4: Account Verification | Security holes | Implement RSO properly with HTTPS |
| Phase 5: Analytics Features | Missing data types | Document unsupported game modes clearly |
| Phase 6: Production Launch | Using wrong API key type | Apply for production key 4+ weeks before launch |

---

## Research Confidence Assessment

| Area | Confidence | Basis |
|------|------------|-------|
| Rate limiting | HIGH | Official Riot documentation, HextechDocs community resources |
| Data retention | HIGH | Official Riot API Libraries documentation |
| Static data versioning | HIGH | Official DDragon documentation, verified with version API |
| Authentication | MEDIUM | Official RSO docs, community implementation examples |
| Database design | MEDIUM | Riot's published architecture, community patterns, general best practices |
| Performance optimization | MEDIUM | General database optimization practices applied to domain |
| Live game polling | MEDIUM | API polling best practices, spectator API characteristics |

---

## Key Takeaways for Roadmap Creation

1. **Phase 1 must include proper rate limiting** - No second chances if blacklisted
2. **Data retention strategy is non-negotiable** - Can't recover expired data
3. **Static data versioning from day 1** - Patch days will break unversioned caches
4. **PUUID-first architecture** - Summoner names are deprecated, migrate early
5. **Queue-based processing** - Eventual consistency requires async patterns
6. **Production key timeline** - Apply 4+ weeks before public launch
7. **Document unsupported features early** - Set user expectations about data gaps

---

## Sources

### Official Riot Documentation
- [Rate Limiting - Riot Developer Portal](https://developer.riotgames.com/docs/portal)
- [Summoner Name to Riot ID - Official](https://www.riotgames.com/en/DevRel/summoner-names-to-riot-id)
- [Summoner Name to Riot ID FAQ - Developer Portal](https://developer.riotgames.com/docs/summoner-name-to-riot-id-faq)
- [RSO (Riot Sign On) - Developer Relations](https://support-developer.riotgames.com/hc/en-us/articles/22801670382739-RSO-Riot-Sign-On)
- [Globalizing Player Accounts - Riot Technology](https://technology.riotgames.com/news/globalizing-player-accounts)

### Community Documentation
- [Rate Limiting - HextechDocs](https://hextechdocs.dev/rate-limiting/)
- [Collecting Data - Riot API Libraries](https://riot-api-libraries.readthedocs.io/en/latest/collectingdata.html)
- [Info About Specific Data - Riot API Libraries](https://riot-api-libraries.readthedocs.io/en/latest/specifics.html)
- [Data Dragon - Riot API Libraries](https://riot-api-libraries.readthedocs.io/en/latest/ddragon.html)
- [PUUIDs and Other IDs - Riot API Libraries](https://riot-api-libraries.readthedocs.io/en/latest/ids.html)

### Technical Implementation
- [Building Secure Riot Authentication System](https://fuziion.nl/blog/building-a-secure-riot-games-authentication-system-with-php)
- [League of Legends data scraping challenges](https://maknee.github.io/blog/2025/League-Data-Scraping/)
- [7 best practices for polling API endpoints](https://www.merge.dev/blog/api-polling-best-practices)
- [Building real-time apps - polling frequency](https://www.sportmonks.com/blogs/building-a-real-time-livescore-app-with-a-football-api-best-practices/)

### Security & Best Practices
- [SSO Protocol Security 2025](https://guptadeepak.com/security-vulnerabilities-in-saml-oauth-2-0-openid-connect-and-jwt/)
- [2026: Turning point for fraud prevention](https://www.gamingintelligence.com/insight/2026-the-turning-point-for-fraud-prevention/)
- [Database optimization techniques 2025](https://nextnative.dev/blog/database-optimization-techniques)
- [Query Optimization - Supabase](https://supabase.com/docs/guides/database/query-optimization)

### Domain Knowledge
- [Patch/2026 Annual Cycle - LoL Wiki](https://wiki.leagueoflegends.com/en-us/Patch/2026_Annual_Cycle)
- [Understanding data consistency](https://www.metaplane.dev/blog/data-consistency-definition-examples)
- [OP.GG vs U.GG comparison](https://www.oreateai.com/blog/opgg-vs-ugg-a-comprehensive-comparison-of-league-of-legends-data-tools/d07a09198958cdd4e11f6a1594f9b8aa)
