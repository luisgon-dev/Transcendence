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

### Summoners and Stats

- `GET /api/summoners/{region}/{name}/{tag}`
- `GET /api/summoners/search`
- `POST /api/summoners/multi-search` (`AppOnly`)
- `POST /api/summoners/{region}/{name}/{tag}/refresh`
- `GET /api/summoners/{summonerId}/stats/overview`
- `GET /api/summoners/{summonerId}/stats/champions`
- `GET /api/summoners/{summonerId}/stats/roles`
- `GET /api/summoners/{summonerId}/matches/recent`
- `GET /api/summoners/{summonerId}/matches/{matchId}`

Default stats scope:
- `stats/overview`, `stats/champions`, and `stats/roles` are computed from ranked solo/duo sample data.
- `matches/recent` defaults to full stored history and can be filtered by queue metadata.

`GET /api/summoners/{summonerId}/matches/recent` supports:
- `page` / `pageSize`
- `queueFamily` (optional; e.g. `ALL`, `RANKED_SOLO_DUO`, `RANKED_FLEX`, `NORMAL_SR`, `ARAM`, `CLASH`, `ARENA`, `ROTATING`, `BOT`, `CUSTOM`, `OTHER`)
- `queueIds` (optional repeated query param for explicit queue IDs)

`GET /api/summoners/search` supports:
- `region` (required; platform route or alias such as `NA1` or `na`)
- `q` (required; min length 2, supports `gameName` or `gameName#tag` prefix forms)
- `limit` (optional; default `8`, max `10`)
- Autosuggest only returns summoners with at least one stored match participant (to avoid low-signal entries)

`POST /api/summoners/multi-search` supports:
- `region` (required; platform route or alias such as `NA1` or `na`)
- `summoners` (required array; min `1`, max `5`)
- Each `summoners[]` entry requires `gameName` and `tagLine`
- Returns only already-stored data (no refresh side effects); includes per-summoner stats plus team insights in a single response

Stats and profile read surfaces now fail closed on backend errors:
- `GET /api/summoners/{summonerId}/stats/*` and `GET /api/summoners/{summonerId}/matches/*` return `500` ProblemDetails on internal failures.
- `GET /api/summoners/{region}/{name}/{tag}` also returns `500` ProblemDetails when dependent stats aggregation fails.

#### Rune Payloads

- `GET /api/summoners/{summonerId}/matches/recent`
  - `runes` remains a compact summary (`primaryStyleId`, `subStyleId`, `keystoneId`)
  - `runesDetail` now includes full selections:
    - `primarySelections` (4)
    - `subSelections` (2)
    - `statShards` (3)
  - `queueId` is included alongside `queueType`
- `GET /api/summoners/{summonerId}/matches/{matchId}`
  - Participant runes continue to return full selections (`primarySelections`, `subSelections`, `statShards`)
  - Match payload includes `queueId` and `queueType`

#### Refresh Priority Behavior

- `POST /api/summoners/{region}/{name}/{tag}/refresh` is implicitly treated as a high-priority refresh request.
- The request/response contract is unchanged (no priority request parameter).
- When high-priority refresh demand is active, lower-priority Riot-calling background jobs are temporarily paused.

### Analytics

- `GET /api/analytics/tierlist`
- `GET /api/analytics/champions/{championId}/winrates`
- `GET /api/analytics/champions/{championId}/builds`
- `GET /api/analytics/champions/{championId}/pro-builds`
- `GET /api/analytics/champions/{championId}/matchups`
- `POST /api/analytics/cache/invalidate` (`AppOnly`)

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

`GET /api/analytics/champions/{championId}/builds` includes full rune setup per build:
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

`GET /api/analytics/champions/{championId}/pro-builds` supports optional filters:
- `region` (`KR|EUW|NA|CN|ALL`)
- `role`
- `patch`

Response includes:
- `recentProMatches[]`
- `topPlayers[]`
- `commonBuilds[]`

### Live Game (`AppOnly`)

- `GET /api/summoners/{region}/{gameName}/{tagLine}/live-game`

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
- `GET /api/admin/jobs/recurring`
- `POST /api/admin/jobs/recurring/{id}/trigger`
- `GET /api/admin/jobs/failed`
- `GET /api/admin/jobs/failed/{jobId}`
- `POST /api/admin/jobs/failed/{jobId}/retry`
- `POST /api/admin/cache/invalidate`
- `GET /api/admin/audit-log`
- `GET /api/admin/logs/services`

`GET /api/admin/jobs/failed/{jobId}` returns deep diagnostics for a job, including:
- invocation type/method
- serialized arguments
- state history timeline
- failed-at timestamp
- exception type/message/details (when available)

`GET /api/admin/logs/services` query params:
- `service` (`webapi` or `service`)
- `level` (optional; e.g. `ERROR`, `WARNING`, `INFORMATION`)
- `q` (optional case-insensitive search over category/message/exception)
- `limit` (optional; min `1`, max `500`)

### Pro Roster Admin (`AdminOnly`)

- `GET /api/admin/pro-summoners`
- `POST /api/admin/pro-summoners`
- `GET /api/admin/pro-summoners/{id}`
- `PUT /api/admin/pro-summoners/{id}`
- `DELETE /api/admin/pro-summoners/{id}`

### User Preferences (`UserOnly`)

- Favorites and preferences under `/api/users/me/*`

## OpenAPI Generation Workflow

The repo keeps the exported spec committed and uses it to generate the TypeScript schema.

- Export spec: `scripts/openapi/export.sh` (invoked via `pnpm api:spec`)
- Generate client schema: `packages/api-client` (invoked via `pnpm api:client`)

See root `package.json` scripts:
- `api:gen`
- `api:check`
