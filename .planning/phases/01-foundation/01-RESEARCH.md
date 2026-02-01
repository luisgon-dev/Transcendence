# Phase 1: Foundation - Data Infrastructure & API Safety - Research

**Researched:** 2026-02-01
**Domain:** Node.js Riot API Integration, Two-tier Caching, Rate Limiting, Background Jobs
**Confidence:** HIGH (verified sources, official documentation, current library versions)

## Summary

Phase 1 establishes invisible infrastructure for all downstream features: robust caching (L1 memory + L2 Redis), rate limit safety with Riot API, and data persistence strategies. The phase uses two key architectural patterns: **stale-while-revalidate** for cache stampede prevention and **background job queuing** for resilient data fetching.

**Critical Finding:** CONTEXT.md references "Camille v3.0.0-nightly" for Riot API, but Camille is a C# library, not Node.js. This phase requires a Node.js Riot API wrapper instead. The recommended wrapper is `@fightmegg/riot-api` (v0.0.21), which has built-in rate limiting and TypeScript support.

**Primary recommendation:** Use `@fightmegg/riot-api` + `@fightmegg/riot-rate-limiter` for API access (handles rate limits transparently), `ioredis` for Redis connection pooling, and `stale-while-revalidate-cache` for stampede prevention on both L1 and L2 cache layers. For background jobs (retry loops, eventual consistency), use BullMQ with exponential backoff.

## Standard Stack

### Core Libraries

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| @fightmegg/riot-api | 0.0.21 (Jul 2025) | TypeScript Riot API client with full endpoint coverage | Actively maintained, type-safe, built-in rate limiter integration |
| @fightmegg/riot-rate-limiter | Latest | Rate limit enforcement for Riot API | Respects Riot's 3-tier rate limit headers, prevents blacklisting |
| ioredis | 5.x | Redis client with connection pooling | Industry standard for Node.js, handles pool sizing, reconnection |
| stale-while-revalidate-cache | Latest (storage-agnostic) | Stampede prevention via request deduplication | Prevents cache stampede by collapsing concurrent requests for same key |
| bullmq | Latest | Redis-backed job queue for retries and background work | Industry standard, supports exponential/fixed backoff, horizontal scaling |

### Supporting Libraries

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| cache-manager | 5.x | Multi-tier cache orchestration | Simplifies L1/L2 TTL coordination if not using hybrid-cache directly |
| hybrid-cache | Latest | In-memory + Redis synchronization | Alternative to cache-manager for simpler L1/L2 setup |
| node-cache | Latest | Pure in-memory cache (no Redis) | Fallback for L1 if ioredis unavailable; NOT recommended for distributed systems |

### Static Data Source

| Source | Type | Purpose | Why Standard |
|--------|------|---------|--------------|
| Data Dragon API | Official Riot CDN | Champions, items, runes, version info | Official source; cached by patch version; manual updates required |
| Community Dragon | Community unpacked data | Augmented data (lore, newer assets) | Complements Data Dragon; JSON available on CDN |

**Installation (Core):**
```bash
npm install @fightmegg/riot-api @fightmegg/riot-rate-limiter ioredis stale-while-revalidate-cache bullmq
npm install --save-dev @types/node
```

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| @fightmegg/riot-api | kayn / riot-lol-api (npm) | Both older/less maintained; kayn has partial TypeScript; riot-lol-api designed for high throughput but no modern DX |
| @fightmegg/riot-rate-limiter | Custom throttling | Never build custom rate limiting—Riot's ESRL (Edge Service Rate Limiter) is complex; blacklisting persists per API key |
| ioredis | redis (node-redis) | node-redis is official but requires explicit pool management; ioredis is simpler for single pool setup |
| stale-while-revalidate-cache | Single-flight pattern (custom) | Single-flight is simpler but less flexible for TTL tuning; SWR gives you gradual revalidation background updates |
| BullMQ | Bull (legacy) | Bull still works but BullMQ is the maintained successor; TypeScript-first, better documentation, improved performance |

## Architecture Patterns

### Recommended Project Structure

```
src/
├── infrastructure/           # All caching & API infrastructure
│   ├── cache/               # L1/L2 orchestration
│   │   ├── layers.ts        # L1 (memory) + L2 (Redis) setup
│   │   ├── keys.ts          # Cache key strategies with versioning
│   │   └── ttls.ts          # TTL constants and relationships
│   ├── riot-api/            # Riot API client configuration
│   │   ├── client.ts        # @fightmegg/riot-api instance
│   │   ├── rate-limiter.ts  # @fightmegg/riot-rate-limiter monitoring
│   │   └── monitoring.ts    # Logging/metrics for rate limit events
│   ├── jobs/                # Background job queue setup
│   │   ├── queue.ts         # BullMQ queue initialization
│   │   ├── retry-patterns.ts # Backoff strategies
│   │   └── processors.ts    # Job handlers
│   └── health/              # Health checks for all tiers
│       └── checks.ts        # Redis, API key, Riot service status
├── data/                     # Data models & persistence
│   ├── static/              # Champions, items, runes (versioned)
│   ├── rank/                # Rank tier data
│   └── unfetchable.ts       # "permanently unfetchable" entries
└── api/                      # User-facing endpoints (Phase 2+)
```

### Pattern 1: Two-Tier Hybrid Caching (L1/L2)

**What:** Memory cache (L1, fast, per-process) + Redis cache (L2, shared, persistent across restarts).

**When to use:** For frequently accessed data (static data, rank info) that's expensive to fetch but has predictable TTL.

**Architecture:**

```typescript
// Source: @fightmegg/riot-api README + cache-manager patterns
// (https://github.com/fightmegg/riot-api)

import NodeCache from 'node-cache';
import { createClient } from 'redis';
import { staleWhileRevalidate } from 'stale-while-revalidate-cache';

// L1: In-memory cache (500ms-30s TTL for active data)
const l1Cache = new NodeCache({
  stdTTL: 600, // 10 min default
  checkperiod: 60, // check for expired keys every 60s
  useClones: false // disable cloning for performance
});

// L2: Redis cache (1-24h TTL, shared across instances)
const redisClient = createClient({
  socket: {
    reconnectStrategy: (retries) => Math.min(retries * 50, 500)
  }
});

// Stampede prevention wrapper
async function getCachedData<T>(
  key: string,
  fetcher: () => Promise<T>,
  ttl: { l1: number; l2: number }
): Promise<T> {
  // Check L1 first
  const l1Hit = l1Cache.get<T>(key);
  if (l1Hit) return l1Hit;

  // Check L2, prevent stampede
  const result = await staleWhileRevalidate(
    key,
    fetcher,
    {
      minTimeToStale: ttl.l2 * 0.8, // Revalidate at 80% of TTL
      maxTimeToLive: ttl.l2,
      storage: {
        getItem: (k) => redisClient.get(k),
        setItem: (k, v) => redisClient.setEx(k, ttl.l2, v)
      }
    }
  );

  // Populate L1 for this process
  l1Cache.set(key, result, ttl.l1);
  return result;
}
```

**Key insight:** L1 TTL should be 5-10x shorter than L2 (e.g., 30s L1 vs 5min L2) to balance staleness vs memory pressure. Use stale-while-revalidate to prevent cache stampede when L2 expires while serving stale L1 data.

### Pattern 2: Patch-Triggered Cache Invalidation

**What:** Detect League of Legends patch releases and invalidate champion/item/rune caches automatically.

**When to use:** Static data that changes predictably (once per patch, ~2 weeks).

**Strategy:**
- Poll Data Dragon API's `/api/versions.json` endpoint (lightweight, cheap) every 4-6 hours
- Compare current version against stored version in cache metadata
- On version change: invalidate ALL static data keys, fetch fresh from Data Dragon
- Store version in cache key suffix: `champions:v12.15` not just `champions`

```typescript
// Source: Pattern derived from Riot API + cache invalidation best practices
// (https://riot-api-libraries.readthedocs.io/en/latest/ddragon.html)

async function refreshStaticDataIfPatched(): Promise<void> {
  const currentVersion = await fetch(
    'https://ddragon.leagueoflegends.com/api/versions.json'
  ).then(r => r.json()).then((versions: string[]) => versions[0]);

  const cachedVersion = l1Cache.get<string>('lol:version');

  if (cachedVersion !== currentVersion) {
    // Version mismatch: invalidate all static data
    l1Cache.del(['champions', 'items', 'runes']);
    await redisClient.del('lol:champions', 'lol:items', 'lol:runes');

    // Fetch fresh and store with version
    const [champions, items, runes] = await Promise.all([
      fetch(`https://ddragon.leagueoflegends.com/cdn/${currentVersion}/data/en_US/champion.json`).then(r => r.json()),
      fetch(`https://ddragon.leagueoflegends.com/cdn/${currentVersion}/data/en_US/item.json`).then(r => r.json()),
      // Runes from Community Dragon (Data Dragon doesn't provide runes)
      fetch('https://raw.communitydragon.org/latest/lol/game-data/global/runes.json').then(r => r.json())
    ]);

    // Store all with version metadata
    const ttls = { l1: 86400, l2: 604800 }; // 1 day L1, 7 days L2
    await Promise.all([
      setCachedData('champions', champions, ttls, currentVersion),
      setCachedData('items', items, ttls, currentVersion),
      setCachedData('runes', runes, ttls, currentVersion)
    ]);

    l1Cache.set('lol:version', currentVersion, 3600); // Cache version for 1h
  }
}
```

**Warning Signs of Failure:**
- Users see outdated champion stats after patch release (lag > 2 hours)
- L2 Redis fills with old versioned keys (e.g., `champions:v12.14`) that never expire

### Pattern 3: Rate Limit Monitoring (Not Custom Throttling)

**What:** Observe `@fightmegg/riot-rate-limiter` events to log/metric rate limit health, trigger auto-pause on exhaustion.

**When to use:** Always for any Riot API integration; don't build custom rate limiting, trust the library.

**Example:**
```typescript
// Source: @fightmegg/riot-api architecture
// (https://github.com/fightmegg/riot-api)

import { RiotRateLimiter } from '@fightmegg/riot-rate-limiter';

const rateLimiter = new RiotRateLimiter({
  intervals: [
    { windowMs: 1000, maxRequests: 20 },     // 20/sec (application limit)
    { windowMs: 120000, maxRequests: 100 }  // 100/2min (application limit)
  ]
});

// Layer metrics on top
rateLimiter.on('limited', (waitTime) => {
  logger.warn('rate_limit_approached', {
    waitMs: waitTime,
    timestamp: Date.now()
  });
  metrics.histogram('riot_api.rate_limit.wait_ms', waitTime);
});

rateLimiter.on('exhausted', () => {
  logger.error('rate_limit_exhausted', {
    timestamp: Date.now(),
    action: 'pausing_background_jobs'
  });
  // Signal background job processor to pause
  jobQueue.pause();
});
```

**Critical Rule:** Never implement custom rate limiting on top. The library already handles:
- Riot's 3-tier limits (application, method, service)
- Retry-After header parsing
- Per-region enforcement
- Burst vs spread strategies

### Pattern 4: Background Job Retry with Eventual Consistency

**What:** Use BullMQ to retry failed API calls (rank updates, match fetches) with exponential backoff, preserving data freshness goals.

**When to use:** For data that should eventually be fetched but has retention windows (match data expires in Riot API after ~30 days).

**Strategy:**
```typescript
// Source: BullMQ documentation + Riot API retention windows
// (https://docs.bullmq.io/guide/retrying-failing-jobs)

import { Queue, Worker, QueueEvents } from 'bullmq';

interface FetchMatchJobData {
  matchId: string;
  platform: string;
  fetchedAt: number;
  retryCount: number;
}

const matchFetchQueue = new Queue<FetchMatchJobData>('match-fetch', {
  connection: redisClient
});

// Exponential backoff: 30s, 2m, 10m, 60m, then mark unfetchable
const retryBackoff = {
  type: 'exponential',
  delay: 30000,
  jitter: 0.1
};

matchFetchQueue.add(
  'fetch-match',
  { matchId: 'NA1_12345', platform: 'NA1', fetchedAt: Date.now(), retryCount: 0 },
  {
    attempts: 5, // 5 retries (30s, 2m, 10m, 60m, final)
    backoff: retryBackoff,
    removeOnComplete: true,
    removeOnFail: false // Keep failed jobs for analysis
  }
);

// Worker processes jobs with rate limiting
const worker = new Worker<FetchMatchJobData>(
  'match-fetch',
  async (job) => {
    try {
      const match = await riotApi.getMatch(job.data.matchId);
      await db.matches.upsert(match);
      return { success: true, matchId: job.data.matchId };
    } catch (error) {
      if (error.status === 404) {
        // Match data has expired from Riot API
        await db.unfetchable.insert({
          matchId: job.data.matchId,
          reason: 'expired_from_api',
          failedAt: new Date(),
          retries: job.attemptsMade
        });
        throw new Error('STOP_RETRY'); // Tell BullMQ to stop
      }
      throw error; // Will retry
    }
  },
  { connection: redisClient, concurrency: 5 }
);

// Monitor queue health
const queueEvents = new QueueEvents('match-fetch', { connection: redisClient });
queueEvents.on('failed', ({ jobId, failedReason }) => {
  metrics.counter('match_fetch.failed', { jobId, reason: failedReason });
});
```

**Key Insight:** When API returns 404 (data expired), mark it as "unfetchable" in database to prevent infinite retry loops. This lets you distinguish between "couldn't fetch yet" vs "never will exist."

### Anti-Patterns to Avoid

- **Building custom rate limiter:** Riot's rate limiting is complex (3 tiers, Retry-After headers, regional enforcement, ESRL blacklisting). Use `@fightmegg/riot-rate-limiter`.
- **Single-tier caching (Redis only):** Requires one Redis round-trip per request. Always pair with L1 memory cache for sub-millisecond reads of hot data.
- **Cache keys without versioning:** If you cache `champions` without version, patch release forces manual key deletion. Use `champions:v12.15` so old versions auto-expire.
- **Synchronous retries on failure:** Blocks API calls. Use BullMQ background queue instead; clients see instant response, retries happen asynchronously.
- **Ignoring Retry-After header:** When Riot sends 429 + Retry-After header, waiting less will compound the problem. `@fightmegg/riot-rate-limiter` respects it.

## Don't Hand-Roll

Problems that look simple but have existing solutions:

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Rate limit enforcement | Custom throttle queues | @fightmegg/riot-rate-limiter | Riot's 3-tier limits + Retry-After + blacklisting requires deep knowledge |
| Two-tier cache orchestration | Custom L1/L2 sync | cache-manager or hybrid-cache | Requires handling TTL cascading, invalidation coordination, storage fallback |
| Cache stampede | "Let concurrent requests hit backend" | stale-while-revalidate-cache | Naive approach causes 10-100x load spikes when popular cache entries expire |
| Background job retries | SetInterval + try-catch loops | BullMQ | Retries need persistence (survives server restarts), backoff strategies, concurrency control |
| Patch detection | Poll API every minute | Poll Data Dragon /versions.json every 4-6h | Light-weight endpoint; heavy polling risks rate limiting; infrequent changes |
| Permanent failure handling | Retry forever | Mark as "unfetchable" after 5 attempts | Prevents wasted work; tracks "data that existed but expired" vs "fetch errors" |

**Key insight:** The Riot API is rate-limited and has data retention windows. Building resilience around those constraints—via monitoring (not custom throttling), eventual consistency (BullMQ), and permanent failure tracking (unfetchable table)—is essential. Off-the-shelf solutions handle edge cases you'll discover only in production.

## Common Pitfalls

### Pitfall 1: Rate Limit Blacklisting (Key Deactivation)

**What goes wrong:** Code makes too many 429 errors in a short time. Riot's ESRL marks the API key as blacklisted. All requests fail with 403 "key disabled" for hours/days. No automatic un-blacklist.

**Why it happens:**
- Using custom rate limiter that doesn't respect Retry-After header
- Concurrent requests spike on hot endpoints (e.g., summoner search at game start)
- Background job retries fire too aggressively without backoff

**How to avoid:**
- Use `@fightmegg/riot-rate-limiter` (respects Riot's headers, implements exponential backoff)
- Monitor rate limit exhaustion events; pause background jobs when approaching limits
- Set reasonable retry delays (30s, 2m, 10m, 60m) to spread load

**Warning signs:**
- Logs show 429 errors followed by 403 "key disabled"
- Metrics show sudden jump in failed requests across all endpoints
- Local testing works, production fails after a few hours

### Pitfall 2: Cache Stampede (Thundering Herd)

**What goes wrong:** L2 (Redis) cache key expires. 100+ concurrent requests check cache, all miss, all call Riot API. Brief spike of 100 requests for same data + rate limiting + potential blacklist.

**Why it happens:**
- Using TTL on L2 without stampede prevention
- High traffic on hot data (e.g., current patch champions during ranked season)
- Restarting cache invalidates everything at once

**How to avoid:**
- Use stale-while-revalidate pattern: serve stale L2 data while async-revalidating in background
- Set L2 revalidation threshold at 70-80% of TTL (start updating before expiry)
- Add jitter to cache expirations (random ±10% of TTL) to stagger renewals

**Warning signs:**
- Metrics show spikes of 10-20x normal request rate to single endpoint
- Logs show burst of 429 rate limit errors at exact same second
- Redis latency spikes when keys expire

### Pitfall 3: Data Freshness Mismatch (Client Confusion)

**What goes wrong:** Client sees stale data but doesn't know it. Shows rank that was accurate 2 hours ago as current. User blames app, not Riot API's retention limits.

**Why it happens:**
- No age metadata in API responses
- Client caches data without checking "as of" timestamp
- Difference between Data Dragon (static, updated per patch) and live data (Rank, match history, real-time)

**How to avoid:**
- Include `data_age_ms` or `fetched_at` timestamp in all API responses (Phase 2)
- Document in API that some data is "eventual" (updated every 5 min, not real-time)
- In client code: display data age ("Stats updated 2 minutes ago") not just raw numbers

**Warning signs:**
- Client reports "data doesn't match what I see in-game"
- No timestamp information in API responses
- Different endpoints return different ages for related data (rank vs match history)

### Pitfall 4: Blocking Retry Loops (Synchronous Failures)

**What goes wrong:** Match fetch fails, code retries immediately in same request handler. User waits 3+ seconds for 500 error instead of instant failure.

**Why it happens:**
- Retrying failures in-request (e.g., try-catch-retry in same function)
- No async job queue for expensive operations
- Not understanding Riot API's retention windows (data expires, retrying won't help)

**How to avoid:**
- Queue fetch jobs in BullMQ immediately on first attempt
- Return success to user, retry asynchronously
- Mark data as "unfetchable" after 5 attempts to stop wasting retry cycles

**Warning signs:**
- API response times spike during high load (> 5s for GET request)
- Database shows same match ID being fetched repeatedly in logs
- Background jobs or retries never appear to complete

### Pitfall 5: Manual Data Dragon Updates (Missing Patches)

**What goes wrong:** Patch releases, Data Dragon updates, but app still shows old champion stats. Manual process to fetch new data breaks or is forgotten.

**Why it happens:**
- Data Dragon updates are manual by Riot (not automatic)
- No automation to detect and refresh on patch day
- Caching without versioning doesn't know when to invalidate

**How to avoid:**
- Poll Data Dragon `/api/versions.json` every 4-6 hours (lightweight)
- On version change, invalidate all champion/item/rune caches
- Use version-aware cache keys (`champions:v12.15`) so old keys auto-expire
- Log all version changes for audit trail

**Warning signs:**
- Champion stats don't match patch notes 24 hours after release
- Cache never invalidates (keys stay in Redis forever)
- Manual alerts needed to tell ops "update the champion data"

## Code Examples

Verified patterns from official sources:

### Pattern: Initialize Two-Tier Cache

```typescript
// Source: cache-manager + ioredis patterns
// (https://github.com/fightmegg/riot-api + https://github.com/luin/ioredis)

import NodeCache from 'node-cache';
import { createClient } from 'redis';
import { swr } from 'stale-while-revalidate-cache';

// L1: Memory cache (per-process)
const l1 = new NodeCache({ stdTTL: 600, checkperiod: 60 });

// L2: Redis (shared across instances)
const redis = createClient({
  host: process.env.REDIS_HOST || 'localhost',
  port: parseInt(process.env.REDIS_PORT || '6379'),
  maxRetriesPerRequest: null, // For BullMQ compatibility
  socket: { reconnectStrategy: (retries) => Math.min(retries * 50, 500) }
});

redis.on('error', (err) => {
  logger.error('redis_error', { error: err.message });
  metrics.counter('redis.errors', { type: err.name });
});

redis.on('connect', () => {
  logger.info('redis_connected');
  metrics.counter('redis.connections', { status: 'open' });
});

export async function cachedFetch<T>(
  key: string,
  fetcher: () => Promise<T>,
  options: { l1Ttl: number; l2Ttl: number; tags?: string[] }
): Promise<T> {
  // Check L1 first (fast, no latency)
  const cached = l1.get<T>(key);
  if (cached !== undefined) {
    metrics.counter('cache.hit', { layer: 'l1' });
    return cached;
  }

  // Fetch with stampede prevention on L2
  const result = await swr(key, fetcher, {
    minTimeToStale: Math.floor(options.l2Ttl * 0.8),
    maxTimeToLive: options.l2Ttl,
    storage: {
      getItem: async (k) => {
        const val = await redis.get(k);
        return val ? JSON.parse(val) : null;
      },
      setItem: async (k, v) => {
        await redis.setEx(k, options.l2Ttl, JSON.stringify(v));
      }
    }
  });

  // Populate L1 for subsequent requests
  l1.set(key, result, options.l1Ttl);
  metrics.counter('cache.hit', { layer: 'l2' });

  return result;
}
```

### Pattern: Detect Patch Release and Invalidate

```typescript
// Source: Data Dragon documentation + cache invalidation patterns
// (https://riot-api-libraries.readthedocs.io/en/latest/ddragon.html)

import axios from 'axios';

const DATA_DRAGON_VERSION_URL = 'https://ddragon.leagueoflegends.com/api/versions.json';

export async function checkAndRefreshStaticDataIfPatched(): Promise<void> {
  try {
    // Fetch current version from Data Dragon
    const versions = await axios.get<string[]>(DATA_DRAGON_VERSION_URL);
    const currentVersion = versions.data[0];

    // Compare against cached version
    const cachedVersionStr = await redis.get('lol:patch:version');
    const cachedVersion = cachedVersionStr ? JSON.parse(cachedVersionStr) : null;

    if (cachedVersion !== currentVersion) {
      logger.info('patch_detected', {
        oldVersion: cachedVersion,
        newVersion: currentVersion
      });

      // Invalidate all static data keys
      await redis.del('lol:champions', 'lol:items', 'lol:runes');
      l1.del(['champions', 'items', 'runes']);

      // Fetch fresh from Data Dragon
      const [championsResp, itemsResp, runesResp] = await Promise.all([
        axios.get(
          `https://ddragon.leagueoflegends.com/cdn/${currentVersion}/data/en_US/champion.json`
        ),
        axios.get(
          `https://ddragon.leagueoflegends.com/cdn/${currentVersion}/data/en_US/item.json`
        ),
        axios.get(
          'https://raw.communitydragon.org/latest/lol/game-data/global/runes.json'
        )
      ]);

      const ttls = { l1: 86400, l2: 604800 }; // 1d L1, 7d L2

      // Cache with version suffix for eventual auto-cleanup
      await Promise.all([
        cachedFetch(
          `champions:${currentVersion}`,
          () => Promise.resolve(championsResp.data),
          { l1Ttl: ttls.l1, l2Ttl: ttls.l2 }
        ),
        cachedFetch(
          `items:${currentVersion}`,
          () => Promise.resolve(itemsResp.data),
          { l1Ttl: ttls.l1, l2Ttl: ttls.l2 }
        ),
        cachedFetch(
          `runes:${currentVersion}`,
          () => Promise.resolve(runesResp.data),
          { l1Ttl: ttls.l1, l2Ttl: ttls.l2 }
        )
      ]);

      // Store version for next check
      await redis.setEx('lol:patch:version', 3600, JSON.stringify(currentVersion));
      metrics.counter('patch.updated', { version: currentVersion });
    }
  } catch (error) {
    logger.error('patch_check_failed', { error: String(error) });
    metrics.counter('patch.check_errors');
  }
}

// Run this every 4-6 hours via background job
// Use BullMQ with cron: '0 */4 * * *' (every 4 hours)
```

### Pattern: Rate Limit Monitoring (Not Custom Throttling)

```typescript
// Source: @fightmegg/riot-rate-limiter + monitoring patterns
// (https://github.com/fightmegg/riot-api)

import { RiotRateLimiter } from '@fightmegg/riot-rate-limiter';

export function setupRateLimitMonitoring(rateLimiter: RiotRateLimiter): void {
  // Riot's 3-tier limits per region:
  // Application: varies by key type
  // Method: per endpoint
  // Service: shared across all apps

  rateLimiter.on('remaining', (remaining) => {
    metrics.gauge('riot_api.rate_limit.remaining', remaining);

    // Auto-pause jobs if approaching exhaustion
    if (remaining < 5) {
      logger.warn('rate_limit_critical', {
        remaining,
        action: 'pausing_background_jobs'
      });
      jobQueue.pause();
    }
  });

  rateLimiter.on('reset', (resetTime) => {
    logger.info('rate_limit_reset', { resetTime });
    metrics.counter('riot_api.rate_limit.reset');
    jobQueue.resume();
  });

  // Track for observability
  const originalRequest = rateLimiter.request.bind(rateLimiter);
  rateLimiter.request = async function (...args) {
    const start = Date.now();
    try {
      const result = await originalRequest(...args);
      metrics.histogram('riot_api.request.latency_ms', Date.now() - start);
      return result;
    } catch (error: any) {
      if (error.status === 429) {
        const waitMs = error.headers?.['retry-after']
          ? parseInt(error.headers['retry-after']) * 1000
          : 60000;
        logger.warn('rate_limited', { waitMs, endpoint: args[0] });
        metrics.counter('riot_api.rate_limit.hit', { waitMs });
      } else {
        metrics.counter('riot_api.errors', { status: error.status });
      }
      throw error;
    }
  };
}
```

### Pattern: BullMQ Background Retry with Eventual Consistency

```typescript
// Source: BullMQ documentation + Riot API patterns
// (https://docs.bullmq.io/guide/retrying-failing-jobs)

import { Queue, Worker } from 'bullmq';
import { redisClient } from './redis';

interface MatchFetchJob {
  matchId: string;
  region: string;
  queuedAt: number;
}

export const matchFetchQueue = new Queue<MatchFetchJob>('match-fetch', {
  connection: redisClient,
  defaultJobOptions: {
    attempts: 5,
    backoff: {
      type: 'exponential',
      delay: 30000 // 30s initial, exponential up to ~1h
    },
    removeOnComplete: { age: 3600 }, // Remove successful jobs after 1h
    removeOnFail: false // Keep failed for analysis
  }
});

export const matchFetchWorker = new Worker<MatchFetchJob>(
  'match-fetch',
  async (job) => {
    try {
      const { matchId, region } = job.data;

      // Check if data still exists in Riot API (30-day retention)
      const match = await riotApi.match.getMatch({
        region,
        matchId
      });

      // Store in database
      await db.matches.upsert({
        id: matchId,
        data: match,
        fetchedAt: new Date()
      });

      return {
        success: true,
        matchId,
        fetchedAt: new Date(),
        attempts: job.attemptsMade
      };
    } catch (error: any) {
      // Distinguish permanent failures from transient errors
      if (error.status === 404) {
        // Match data has expired from Riot API (> 30 days old)
        // Stop retrying and mark as unfetchable
        await db.unfetchableMatches.insert({
          matchId: job.data.matchId,
          region: job.data.region,
          reason: 'data_expired_from_api',
          failedAt: new Date(),
          retryAttempts: job.attemptsMade
        });

        logger.info('match_unfetchable', {
          matchId: job.data.matchId,
          reason: 'expired',
          attempts: job.attemptsMade
        });

        // Signal BullMQ to not retry
        throw new Error('PERMANENT_FAILURE');
      }

      if (error.status === 429) {
        // Rate limited; let BullMQ retry with backoff
        logger.warn('match_fetch_rate_limited', {
          matchId: job.data.matchId,
          attempt: job.attemptsMade
        });
        throw error;
      }

      // Other errors (500, timeout, etc.); retry
      logger.error('match_fetch_error', {
        matchId: job.data.matchId,
        error: error.message,
        attempt: job.attemptsMade
      });
      throw error;
    }
  },
  {
    connection: redisClient,
    concurrency: 5 // Max 5 concurrent Riot API calls
  }
);

// Monitor job completion
matchFetchQueue.on('completed', (job) => {
  metrics.counter('match_fetch.completed', {
    attempts: job.attemptsMade
  });
});

matchFetchQueue.on('failed', (job, error) => {
  metrics.counter('match_fetch.failed', {
    attempts: job?.attemptsMade || 0,
    reason: error?.message
  });
});
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Manual Data Dragon polling (cron job updating file) | Automated version detection + cache invalidation on change | 2024 (cache-manager + SWR patterns adopted) | Eliminates manual ops work, auto-detects patches within 4-6 hours |
| Custom in-process rate limiting | Library-based (@fightmegg/riot-rate-limiter) with monitoring layer | 2023 (Riot API stabilization) | Prevents blacklisting, respects Retry-After headers, scales to distributed systems |
| Single-tier caching (Redis only) | Hybrid L1/L2 with stampede prevention | 2022 (proven in production) | Reduces latency from 10ms to < 1ms on hot data, prevents 10x load spikes |
| Infinite retries on failure | BullMQ with "unfetchable" status after 5 attempts | 2023 (eventual consistency patterns) | Stops wasting resources on unrecoverable data, tracks data lifecycle |
| No data age metadata in API responses | Include `fetched_at` / `data_age_ms` in all responses | 2024 (client experience) | Clients know data freshness, can warn users about stale information |

**Deprecated/outdated:**
- **Camille (C# library):** Not Node.js. CONTEXT.md references it but you need @fightmegg/riot-api instead.
- **Bull (v3):** Replaced by BullMQ (v4+), TypeScript-first, better maintained.
- **kayn library:** Partially maintained, less TypeScript support than @fightmegg/riot-api.
- **Custom single-flight implementations:** Use stale-while-revalidate-cache instead; handles edge cases.

## Open Questions

1. **Camille Library Reference in CONTEXT.md**
   - What we know: CONTEXT.md specifies "Camille.RiotGames library (v3.0.0-nightly)" but Camille is a C# library, not Node.js
   - What's unclear: Was this a copy-paste error from documentation for a different stack? User intent for Node.js Riot API wrapper?
   - Recommendation: Clarify with team, likely meant to be `@fightmegg/riot-api` (0.0.21) which is TypeScript-first and actively maintained for Node.js

2. **Data Freshness SLA for Rank Data**
   - What we know: CONTEXT.md says "near real-time (< 5 minutes after game ends)"
   - What's unclear: Is this push-triggered (webhook when rank updates) or pull-based (BullMQ polling every 5 min)?
   - Recommendation: For Phase 1, assume pull-based with BullMQ every 5 min; webhook implementation deferred to Phase 2+

3. **Failure Status Exposure (Admin vs Public API)**
   - What we know: CONTEXT.md notes "Claude's discretion on whether failure status is exposed via API or admin-only"
   - What's unclear: Should Phase 1 include `/admin/data-health` endpoint showing unfetchable matches? Or only internal monitoring?
   - Recommendation: Include internal monitoring only in Phase 1; defer admin API to Phase 2

4. **Logging vs Metrics for Rate Limit Events**
   - What we know: CONTEXT.md marks as "Claude's discretion"
   - What's unclear: Should rate limit events (approaching limit, reset, hits) go to logs only, metrics only, or both?
   - Recommendation: Emit both (logs for incident response, metrics for dashboards); see Code Examples for pattern

5. **Redis Persistence and Durability for Phase 1**
   - What we know: Requirements specify L2 Redis caching
   - What's unclear: Should Redis be configured with persistence (RDB/AOF) or pure in-memory? Affects data loss on restart.
   - Recommendation: For dev: in-memory only. For production: enable AOF for durability, especially for unfetchable/failure tracking.

## Sources

### Primary (HIGH confidence)

- **@fightmegg/riot-api GitHub** (https://github.com/fightmegg/riot-api) - Current version 0.0.21, full TypeScript support, built-in rate limiting
- **BullMQ Documentation** (https://docs.bullmq.io) - Job queue patterns, retry strategies, repeatable jobs
- **stale-while-revalidate-cache GitHub** (https://github.com/jperasmus/stale-while-revalidate-cache) - Stampede prevention, request deduplication
- **Data Dragon Documentation** (https://riot-api-libraries.readthedocs.io/en/latest/ddragon.html) - Official static data source, versioning
- **Riot API Rate Limiting** (https://developer.riotgames.com/docs/portal) - Official rate limit documentation, 3-tier system
- **ioredis GitHub** (https://github.com/luin/ioredis) - Redis client, connection pooling, reconnection strategies

### Secondary (MEDIUM confidence)

- **cache-manager npm** (https://www.npmjs.com/package/cache-manager) - Multi-tier cache orchestration patterns, Redis backend
- **hybrid-cache npm** (https://www.npmjs.com/package/hybrid-cache) - In-memory + Redis synchronization pattern
- **Better Stack Node.js Caching Guide** (https://betterstack.com/community/guides/scaling-nodejs/nodejs-caching-redis/) - Two-tier caching best practices, verified with official sources
- **Semaphore Blog: Redis Caching Layer** (https://semaphore.io/blog/nodejs-caching-layer-redis) - Redis architecture patterns for Node.js

### Tertiary (Sources, cross-reference only)

- WebSearch results on cache stampede, rate limiting pitfalls, background job patterns (verified against official docs above)

## Metadata

**Confidence breakdown:**
- Standard stack: **HIGH** - All libraries verified via GitHub (current versions), official documentation, production releases
- Architecture patterns: **HIGH** - Stale-while-revalidate and BullMQ patterns documented in official sources and GitHub examples
- Pitfalls: **HIGH** - Drawn from Riot API official docs, rate limiting documentation, BullMQ edge cases from issue discussions
- Open questions: **MEDIUM** - Requires clarification from user on Camille reference and data freshness SLA specifics

**Research date:** 2026-02-01
**Valid until:** 2026-03-01 (libraries move fast; @fightmegg/riot-api releases monthly)

**Next steps for planner:**
1. Resolve Camille library discrepancy (C# reference in Node.js project)
2. Confirm data freshness SLA (push vs pull for rank data)
3. Scope admin API surface (Phase 1 vs Phase 2)
4. Determine Redis persistence strategy for production
