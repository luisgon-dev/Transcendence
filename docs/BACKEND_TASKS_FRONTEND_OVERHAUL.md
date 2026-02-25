# Backend Follow-Up Tasks For Frontend Overhaul

This document tracks backend work needed to fully unlock the new frontend surfaces (`/matchups/*`, `/pro-builds/*`) without placeholders.

## Priority 0: Pro Builds Data Contract

### Task
Add a dedicated pro/high-ELO builds endpoint for champion pages.

### Why
`/pro-builds/[championId]` is currently a public teaser because backend pro feed data is not exposed yet.

### Proposed API
- `GET /api/analytics/champions/{championId}/pro-builds`
- Query params:
  - `region` (optional: `KR|EUW|NA|CN|ALL`)
  - `role` (optional)
  - `patch` (optional)

### Proposed response shape
- `championId`
- `patch`
- `role`
- `region`
- `recentProMatches[]`
  - `matchId`
  - `playerName`
  - `teamName`
  - `win`
  - `playedAt`
  - `items[]`
  - `runes`
- `topPlayers[]`
  - `playerName`
  - `teamName`
  - `games`
  - `winRate`
- `commonBuilds[]`
  - `items[]`
  - `games`
  - `winRate`

### Acceptance criteria
- Endpoint returns at least one non-empty region for current patch in production-like data.
- OpenAPI + generated TypeScript schema updated.

## Priority 1: Matchup Depth Fields

### Task
Extend matchup analytics response to include lane economy signal.

### Why
`/matchups/[championId]` currently shows a `Gold @ 15` placeholder.

### Proposed change
Add optional fields to `MatchupEntryDto`:
- `avgGoldDiffAt15` (number)
- `avgXpDiffAt15` (number, optional)

### Acceptance criteria
- Field is populated for at least supported queues/patches.
- Missing values remain nullable and do not break existing clients.

## Priority 1: Ban Rate Surface

### Task
Expose ban rate in champion/tier analytics payloads.

### Why
Tier list and champion pages currently display `Ban Rate —`.

### Proposed change
- Add `banRate` to:
  - `TierListEntry`
  - `ChampionWinRateDto`

### Acceptance criteria
- Field is returned as ratio (0..1) consistently across endpoints.
- OpenAPI and generated API client updated.

## Priority 2: Full Matchup Universe

### Task
Return full matchup rows (not only top counters/favorable subsets).

### Why
Current API shape limits the frontend's ability to render richer sorting and pagination on matchup tables.

### Proposed change
In `ChampionMatchupsResponse`, add:
- `allMatchups[]` (full list for selected role/rank filter)

Keep:
- `counters[]`
- `favorableMatchups[]`

### Acceptance criteria
- `allMatchups` is deterministic and sorted by games descending by default.
- Payload remains cacheable with existing strategy.

## Priority 2: Champion Rank In Role

### Task
Provide role-scoped rank metadata for champion details.

### Why
Champion page currently has a `Rank # —` placeholder.

### Proposed change
Expose:
- `roleRank`
- `rolePopulation`
in either champion winrate summary or a dedicated rank endpoint.

### Acceptance criteria
- Rank is computed for selected role/rankTier filter.
- Includes denominator to avoid ambiguous rank display.

## Cross-Cutting Requirements

- Update:
  - `docs/API.md`
  - `openapi/transcendence.v1.json`
  - `packages/api-client/src/schema.ts` (via generation workflow)
- Maintain backward compatibility where possible:
  - Additive fields preferred.
  - New endpoints should not break existing frontend pages.
- Add backend unit/integration tests for any new analytics compute logic.
