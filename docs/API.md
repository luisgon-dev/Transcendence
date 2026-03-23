# API

This repo's API contract is defined by the committed OpenAPI spec:

- `openapi/transcendence.v1.json`

If you change endpoints, request/response shapes, or auth semantics, update the spec (see `README.md` and `docs/DEVELOPMENT.md`) in the same PR.

## Authentication

High-level model:

- `AppOnly`: `X-API-Key: <key>`
- `UserOnly`: `Authorization: Bearer <jwt>`
- `AdminOnly`: `Authorization: Bearer <jwt>` with `admin` role claim
- `AppOrUser`: accepts either

The Next.js web frontend uses route handlers as a BFF:

- Browser talks to Next under `/api/session/*` and `/api/trn/*`
- Next talks to the backend at `TRN_BACKEND_BASE_URL`
- Tokens live in HttpOnly cookies on the web domain (never exposed to browser JS)
- AppOnly calls attach `X-API-Key` server-side from `TRN_BACKEND_API_KEY`
- `/api/trn/app/*` is allowlisted to approved AppOnly routes only (not a generic AppOnly passthrough)
- Proxy route handlers reject invalid path segments (`.`/`..`)

## Rate Limiting

Read-heavy endpoints are protected by server-side fixed-window rate limiting and may return:

- `429 Too Many Requests`

Auth endpoints (`/api/auth/register`, `/api/auth/login`, `/api/auth/refresh`, `/api/auth/logout`) also use dedicated per-client rate limits.

## Key Endpoint Areas (Current)

This is a navigational summary; the OpenAPI spec is the source of truth.

### LoL Summoners and Stats

- `GET /api/lol/summoners/{region}/{name}/{tag}`
- `GET /api/lol/summoners/search`
- `POST /api/lol/summoners/multi-search` (`AppOnly`)
- `POST /api/lol/summoners/{region}/{name}/{tag}/refresh`
- `GET /api/lol/summoners/{summonerId}/stats/overview`
- `GET /api/lol/summoners/{summonerId}/stats/champions`
- `GET /api/lol/summoners/{summonerId}/stats/roles`
- `GET /api/lol/summoners/{summonerId}/matches/recent`
- `GET /api/lol/summoners/{summonerId}/matches/{matchId}`

Default stats scope:
- `stats/overview`, `stats/champions`, and `stats/roles` are computed from ranked solo/duo sample data.
- `matches/recent` defaults to full stored history and can be filtered by queue metadata.

`GET /api/lol/summoners/{summonerId}/matches/recent` supports:
- `page` / `pageSize`
- `queueFamily` (optional; e.g. `ALL`, `RANKED_SOLO_DUO`, `RANKED_FLEX`, `NORMAL_SR`, `ARAM`, `CLASH`, `ARENA`, `ROTATING`, `BOT`, `CUSTOM`, `OTHER`)
- `queueIds` (optional repeated query param for explicit queue IDs)

`GET /api/lol/summoners/search` supports:
- `region` (required; platform route or alias such as `NA1` or `na`)
- `q` (required; min length 2, supports `gameName` or `gameName#tag` prefix forms)
- `limit` (optional; default `8`, max `10`)
- Autosuggest only returns summoners with at least one stored match participant (to avoid low-signal entries)

`POST /api/lol/summoners/multi-search` supports:
- `region` (required; platform route or alias such as `NA1` or `na`)
- `summoners` (required array; min `1`, max `5`)
- Each `summoners[]` entry requires `gameName` and `tagLine`
- Returns only already-stored data (no refresh side effects); includes per-summoner stats plus team insights in a single response

Stats and profile read surfaces now fail closed on backend errors:
- `GET /api/lol/summoners/{summonerId}/stats/*` and `GET /api/lol/summoners/{summonerId}/matches/*` return `500` ProblemDetails on internal failures.
- `GET /api/lol/summoners/{region}/{name}/{tag}` also returns `500` ProblemDetails when dependent stats aggregation fails.

#### Rune Payloads

- `GET /api/lol/summoners/{summonerId}/matches/recent`
  - `runes` remains a compact summary (`primaryStyleId`, `subStyleId`, `keystoneId`)
  - `runesDetail` now includes full selections:
    - `primarySelections` (4)
    - `subSelections` (2)
    - `statShards` (3)
  - `queueId` is included alongside `queueType`
- `GET /api/lol/summoners/{summonerId}/matches/{matchId}`
  - Participant runes continue to return full selections (`primarySelections`, `subSelections`, `statShards`)
  - Match payload includes `queueId` and `queueType`

#### Refresh Priority Behavior

- `POST /api/lol/summoners/{region}/{name}/{tag}/refresh` is implicitly treated as a high-priority refresh request.
- The request/response contract is unchanged (no priority request parameter).
- When high-priority refresh demand is active, lower-priority Riot-calling background jobs are temporarily paused.

#### Refresh Contention Contract (LOCK-01)

`POST /api/lol/summoners/{region}/{name}/{tag}/refresh` and `POST /api/admin/pro-summoners/{id}/refresh` share deterministic `202 Accepted` semantics:

- **Queued (lock acquired):**
  - `message`: `"Refresh queued"`
  - `poll`: absolute URL to query refresh progress
  - `retryAfterSeconds`: omitted (`null`)
- **In progress (lock contention):**
  - `message`: `"Refresh in process"`
  - `poll`: absolute URL to query refresh progress
  - `retryAfterSeconds`: positive integer hint (seconds) for next poll attempt

Example (`SummonerAcceptedResponse`):

```json
{
  "message": "Refresh in process",
  "poll": "https://localhost/api/lol/summoners/na1/name/tag",
  "retryAfterSeconds": 42
}
```

### LoL Analytics

- `GET /api/lol/analytics/tierlist`
- `GET /api/lol/analytics/regions`
- `GET /api/lol/analytics/status`
- `GET /api/lol/analytics/champions/{championId}/winrates`
- `GET /api/lol/analytics/champions/{championId}/builds`
- `GET /api/lol/analytics/champions/{championId}/pro-builds`
- `GET /api/lol/analytics/champions/{championId}/matchups`
- `POST /api/lol/analytics/cache/invalidate` (`AppOnly`)

Early-patch semantics:
- Analytics endpoints return **current active patch data only** (no previous-patch fallback payloads).
- Responses now include `sample` metadata for UI messaging:
  - `sampleStatus` (`sufficient`, `low_sample`, `no_data`)
  - `sampleSize`
  - `minimumRecommendedSampleSize`
  - `patchAgeHours`
  - `isEarlyPatchWindow`
- `low_sample` and `no_data` are expected during early patch windows while ingestion ramps up.

`rankTier` query semantics across tier list, win rates, builds, and matchups:
- `all` (or omitted): no rank filter
- Exact tier token: `IRON|BRONZE|SILVER|GOLD|PLATINUM|EMERALD|DIAMOND|MASTER|GRANDMASTER|CHALLENGER`
- Tier scope token: `EMERALD_PLUS` (alias `EMERALD+`) = `EMERALD` and above

`region` query semantics across tier list, win rates, builds, and matchups:
- `ALL` (or omitted): global aggregate across enabled ingestion regions
- Concrete platform region token: for example `NA1|EUW1|EUN1|KR`
- Supported public region tokens are discoverable via `GET /api/lol/analytics/regions`
- Tier list, builds, and matchup responses now echo the resolved `region` field so the UI can badge active scope without guessing

`GET /api/lol/analytics/status` returns the backend-owned active LoL analytics patch metadata:
- `patch`
- `activePatchReleasedAtUtc`
- `activePatchDetectedAtUtc`

`GET /api/lol/analytics/champions/{championId}/builds` includes full rune setup per build:
- `primaryStyleId`, `subStyleId`
- `primaryRunes` (4), `subRunes` (2), `statShards` (3)
- Build item lists include only completed, build-impact items (no components, trinkets, wards, or consumables).
- If patch item metadata is temporarily incomplete, the service uses a legacy exclusion fallback so builds still render while metadata refresh catches up.

Additional analytics fields:
- Tier list and champion winrate surfaces now include `banRate` (ranked solo queue denominator).
- Champion winrate rows include `roleRank` and `rolePopulation` when resolvable.
- Matchups include timeline-derived `avgGoldDiffAt15`, optional `avgXpDiffAt15`, and `allMatchups[]` in addition to `counters[]` and `favorableMatchups[]`.
- Matchup responses include timeline quality metadata:
  - `timelineCoverageRatio`
  - `timelineSampleSize`
  - `timelineDataFreshnessUtc`

`GET /api/lol/analytics/champions/{championId}/pro-builds` supports optional filters:
- `region` (`ALL` or supported platform-region token such as `NA1|EUW1|EUN1|KR`)
- `role`
- `patch`

Response includes:
- `recentProMatches[]`
- `topPlayers[]`
- `commonBuilds[]`

### Live Game (`AppOnly`)

- `GET /api/lol/summoners/{region}/{gameName}/{tagLine}/live-game`

### TFT

- `GET /api/tft/summoners/{region}/{name}/{tag}`
- `POST /api/tft/summoners/{region}/{name}/{tag}/refresh`
- `GET /api/tft/summoners/search`
- `GET /api/tft/summoners/{summonerId}/matches/recent`
- `GET /api/tft/summoners/{summonerId}/matches/{matchId}`
- `GET /api/tft/analytics/regions`
- `GET /api/tft/analytics/comps`
- `GET /api/tft/analytics/comps/{compSlug}`
- `GET /api/tft/analytics/champions`
- `GET /api/tft/analytics/champions/{championId}`
- `GET /api/tft/analytics/items`
- `GET /api/tft/analytics/items/{itemId}`
- `GET /api/tft/analytics/traits`
- `GET /api/tft/analytics/traits/{traitId}`
- `GET /api/tft/analytics/augments`
- `GET /api/tft/analytics/augments/{augmentId}`
- `POST /api/tft/analytics/cache/invalidate` (`AppOnly`)

TFT behavior notes:
- TFT controllers are read-only against persisted data and never call Camille directly.
- `GET /api/tft/summoners/{region}/{name}/{tag}` returns `200` with stored profile/matches or `202 Accepted` when the profile is missing or already refreshing.
- `POST /api/tft/summoners/{region}/{name}/{tag}/refresh` queues a background refresh behind `tft:summoner-refresh:*` locks.
- TFT analytics are isolated from LoL analytics. The comps endpoint is a separate surface and does not share the LoL tier-list route or payload.
- TFT catalog/detail analytics endpoints (`champions`, `items`, `traits`, `augments`) serve the active set only.
- TFT static data remains set-versioned in storage; active-set reads preserve response shapes across set rollovers without returning duplicate cross-set rows.
- `GET /api/tft/analytics/comps` defaults `rankTier` to `EMERALD_PLUS`; `rankTier=ALL` is treated case-insensitively as an all-ranks query.

### Operational Health

- `GET /health/live`
- `GET /health/ready`

### Auth and Keys

- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/auth/refresh`
- `POST /api/auth/logout`
- `GET /api/auth/me` (`AppOrUser`)
- Key management endpoints under `/api/auth/keys` (`AdminOnly`)

Auth behavior notes:
- Registration duplicate-email responses are intentionally generic (`Registration failed.`).
- Password minimum length is 12 characters.

### Admin Operations (`AdminOnly`)

- `GET /api/admin/overview`
- `GET /api/admin/metrics/analysis`
- `GET /api/admin/jobs/recurring`
- `POST /api/admin/jobs/recurring/{id}/trigger`
- `POST /api/admin/jobs/recurring/{id}/pause`
- `POST /api/admin/jobs/recurring/{id}/resume`
- `GET /api/admin/jobs/queues`
- `GET /api/admin/jobs/list`
- `GET /api/admin/jobs/inspect/{jobId}`
- `POST /api/admin/jobs/inspect/{jobId}/delete`
- `POST /api/admin/jobs/bulk-delete`
- `GET /api/admin/jobs/failed`
- `GET /api/admin/jobs/failed/{jobId}`
- `POST /api/admin/jobs/failed/{jobId}/retry`
- `POST /api/admin/cache/invalidate`
- `GET /api/admin/audit-log`
- `GET /api/admin/logs/services`

`GET /api/admin/overview` now includes:
- queue totals plus deleted-job count
- active Hangfire server snapshots (`name`, `workersCount`, `queues`, heartbeat)
- `effectiveConcurrency` as the sum of active worker counts

`GET /api/admin/metrics/analysis` returns:
- active patch metadata
- global database/analysis summary cards
- per-region ingestion health rows including fetch-status counts, timeline coverage, tracked pro-summoner counts, and queue backlog by region

`GET /api/admin/jobs/list` query params:
- `state` (`enqueued`, `processing`, `scheduled`, `failed`)
- `queue`, `type`, `region`, `q` (optional filters)
- `olderThanMinutes` (optional age filter)
- `from`, `count` (paged response)
- `scanLimit` (optional admin scan cap)

`GET /api/admin/jobs/queues` returns queue snapshots plus grouped backlog contributors by state, queue, job type, method, and inferred region.

`GET /api/admin/jobs/inspect/{jobId}` returns deep diagnostics for any job, including:
- invocation type/method
- serialized arguments
- state history timeline
- queue, server id, inferred region, and state timestamps
- exception type/message/details (when available)

`POST /api/admin/jobs/inspect/{jobId}/delete` accepts:
- `expectedState` (optional state assertion such as `Processing` or `Failed`)
- `reason` (optional audit metadata)

`POST /api/admin/jobs/inspect/{jobId}/delete` returns:
- `deleted` to distinguish a successful state transition from a no-op
- `expectedState` echo when provided
- `currentState` when Hangfire can still resolve the job after the attempt
- `message` always included with an operator-facing outcome summary for stale-state / already-missing jobs

`POST /api/admin/jobs/bulk-delete` accepts:
- `states[]` restricted to backlog states (`enqueued`, `scheduled`, `failed`)
- optional filters: `queues[]`, `jobType`, `region`, `query`, `olderThanMinutes`
- `limit`, `scanLimit`
- `dryRun`

`GET /api/admin/jobs/recurring` now distinguishes:
- configured vs present-in-storage recurring jobs
- pause state for producer jobs
- whether a recurring job is pausable from admin

`GET /api/admin/logs/services` query params:
- `service` (`webapi` or `service`)
- `level` (optional; e.g. `ERROR`, `WARNING`, `INFORMATION`)
- `q` (optional case-insensitive search over category/message/exception)
- `sinceUtc` / `untilUtc` (optional timestamp window filters)
- `limit` (optional; min `1`, max `500`)

`GET /api/admin/logs/services` returns:
- `source.available` to distinguish missing log files from empty filtered results
- `source.filesScanned` and `source.latestTimestampUtc` for diagnostics
- `source.truncated` when the response hit the current row limit
- `items[]` with the matching structured log rows

### Pro Roster Admin (`AdminOnly`)

- `GET /api/admin/pro-summoners`
- `POST /api/admin/pro-summoners`
- `GET /api/admin/pro-summoners/{id}`
- `PUT /api/admin/pro-summoners/{id}`
- `DELETE /api/admin/pro-summoners/{id}`
- `POST /api/admin/pro-summoners/{id}/refresh`

### User Preferences (`UserOnly`)

- Favorites and preferences under `/api/users/me/*`

## OpenAPI Generation Workflow

The repo keeps the exported spec committed and uses it to generate the TypeScript client during build/check flows.

- Export spec: `scripts/openapi/export.sh` (invoked via `pnpm api:spec`)
- Generate client package from the spec: `packages/api-client` (invoked via `pnpm api:client`)

See root `package.json` scripts:
- `api:gen`
- `api:check`
