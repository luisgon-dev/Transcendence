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

Auth endpoints use dedicated per-client rate limits: `/api/auth/register`, `/api/auth/password-reset`, and `/api/auth/password-reset/complete` share the conservative `auth-register` policy; `/api/auth/login` uses `auth-login`; `/api/auth/refresh` + `/api/auth/logout` share `auth-refresh`.

## Error Model

All error responses use RFC 7807 **ProblemDetails** (`application/problem+json`):

- Empty-body `4xx/5xx` (e.g. `NotFound()`), model-validation failures, and unhandled exceptions are ProblemDetails automatically.
- Body-carrying errors are normalized: a bare string body (`BadRequest("…")`) is rewrapped as ProblemDetails `detail`, and admin operations return `Problem(title, detail, status)` rather than the legacy `{ message, detail }` object.
- Model-validation failures (e.g. `POST /api/lol/summoners/multi-search`) return **`ValidationProblemDetails`** — ProblemDetails plus a per-field `errors` map. The schema is published in the OpenAPI contract.
- The OpenAPI document declares only `application/problem+json` for these error schemas, matching the runtime response rather than the ordinary JSON/text formatter list.

The service's routes are intentionally unversioned because the API is an internal contract consumed
by the lock-step web app and generated client. The OpenAPI document's `v1` label versions the
published schema snapshot; introduce negotiated URL or header versioning before supporting an
independently deployed external client.

Side-effecting operations that acknowledge success with a message return a typed **`OperationResult`** (`{ message, id? }`) instead of an anonymous body, so the shape is documented in the contract and typed in the generated client. Pure side-effect operations may return `204 No Content`.

## Key Endpoint Areas (Current)

This is a navigational summary; the OpenAPI spec is the source of truth.

### LoL Summoners and Stats

- `GET /api/lol/summoners/{region}/{name}/{tag}`
- `GET /api/lol/summoners/search`
- `POST /api/lol/summoners/multi-search` (`AppOnly`)
- `POST /api/lol/summoners/{region}/{name}/{tag}/refresh` (`UserOnly`)
- `GET /api/lol/summoners/{summonerId}/stats/overview`
- `GET /api/lol/summoners/{summonerId}/stats/champions`
- `GET /api/lol/summoners/{summonerId}/stats/roles`
- `GET /api/lol/summoners/{summonerId}/stats/rank-history`
- `GET /api/lol/summoners/{summonerId}/matches/recent`
- `GET /api/lol/summoners/{summonerId}/matches/{matchId}`
- `GET /api/lol/summoners/{summonerId}/matches/{matchId}/timeline` (public, `expensive-read` rate limit; per-minute team gold/XP and difference curves, or `404` before timeline ingestion)

Default stats scope:
- The Riot-ID lookup always returns `200` with `SummonerLookupResponse`, whose `status` is `ready`,
  `refreshing`, or `missing`. `profile` is populated only for `ready`; `refreshing` includes the poll
  URL and retry hint. This keeps the read response single-typed. The separate signed-in refresh POST
  retains `202 Accepted` because it queues work.
- `stats/overview`, `stats/champions`, and `stats/roles` are computed from ranked solo/duo sample data.
- `GET /api/lol/summoners/{region}/{name}/{tag}` uses the active season for profile overview and champion stats. When a signed-in manual refresh has produced full-history facts, those profile stats use the durable active-season aggregate; otherwise they fall back to retained match detail currently present in the database.
- `matches/recent` defaults to full stored history and can be filtered by queue metadata.
- `stats/rank-history` is app-observed history from stored snapshots. Riot League-V4 exposes current league entries, not an official per-account past-season rank history endpoint. The web profile converts tier + division + LP into a monotonic ladder-points series so promotions do not look like LP resets, labels it as observed (not per-game Riot history), and appends the current rank only when it differs from the latest snapshot.
- Profile champion entries (`topChampions[]`, `topMastery[]`) carry `championId` only; champion display names are resolved client-side from static (DDragon) data, so `championId` is the single source of truth.

Profile responses include additional season/history metadata:
- `activeSeason`: `{ seasonKey, displayName, queueScope }`
- `fullHistory`: nullable status/coverage object with backfill status, scan counters, stored completed solo/duo count, current Riot ranked wins/losses/total, count delta, coverage status, and classifier version

`GET /api/lol/summoners/{summonerId}/matches/recent` supports:
- `page` / `pageSize`
- `queueFamily` (optional; e.g. `ALL`, `RANKED_SOLO_DUO`, `RANKED_FLEX`, `NORMAL_SR`, `ARAM`, `CLASH`, `ARENA`, `ROTATING`, `BOT`, `CUSTOM`, `OTHER`)
- `queueIds` (optional repeated query param for explicit queue IDs)
- `championId` (optional; filters before pagination)
- Responses include stable `facets.queues` and `facets.championIds` collected across the summoner's
  full stored history, independent of the current page and active filters.
- Each match includes `performance`, a team-relative impact readout:
  - `score` is a deterministic `1.0` to `10.0` weighted percentile score within that match and team
  - `teamRank` / `teamSize` make the comparison scope explicit
  - `label` is `MVP` for the top-ranked player on the winning team, `ACE` for the top-ranked player on
    the losing team, and `null` otherwise
  - `killParticipation`, `damageShare`, `goldShare`, `visionShare`, and `csPerMin` expose the real
    inputs used for UI explanations
  - weights are kill participation 30%, champion damage 25%, vision 15%, gold 10%, farm 10%, and
    survival 10%; each input is percentile-ranked within the participant's team before weighting
  - the score is computed from existing participant rows when the cached recent-history response is
    built. It does not require a migration, stored label, or background precomputation.

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
  - Every participant includes the same `performance` readout used by recent history, allowing the
    expanded scoreboard to show team rank and MVP/ACE labels without a second scoring implementation.
  - Match payload includes `queueId` and `queueType`

#### Refresh Priority Behavior

- `POST /api/lol/summoners/{region}/{name}/{tag}/refresh` requires a signed-in user (`UserOnly`) and returns `401` when no user JWT is present.
- The Next.js app calls this through `/api/trn/user/lol/summoners/{region}/{name}/{tag}/refresh`; the anonymous `/api/trn/public/*` proxy does not forward refresh POSTs.
- The refresh is implicitly treated as a high-priority refresh request.
- The request/response contract is unchanged (no priority request parameter).
- When high-priority refresh demand is active, lower-priority Riot-calling background jobs are temporarily paused.
- After the normal quick refresh completes, signed-in manual refreshes enqueue a full-history profile backfill. The backfill scans all Riot-searchable queues for that PUUID and persists compact per-summoner facts/season aggregates independently of the raw match-detail retention window.

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
- `GET /api/lol/analytics/patches`
- `GET /api/lol/analytics/champions/{championId}/winrates`
- `GET /api/lol/analytics/champions/{championId}/profile`
- `GET /api/lol/analytics/champions/{championId}/builds`
- `GET /api/lol/analytics/champions/{championId}/pro-builds`
- `GET /api/lol/analytics/champions/{championId}/matchups`
- `GET /api/lol/analytics/champions/{championId}/synergies`
- `GET /api/lol/analytics/pro/champions`
- `GET /api/lol/analytics/pro/players`
- `GET /api/lol/analytics/items`
- `GET /api/lol/analytics/items/{itemId}`
- `GET /api/lol/analytics/runes`
- `GET /api/lol/analytics/runes/{runeId}`

Analytics cache invalidation is intentionally exposed only through the audited
`POST /api/admin/cache/invalidate` operation.

### LoL Static Content

Display metadata for champions, items, runes and summoner spells, so clients do
not fetch Riot's CDN themselves.

- `GET /api/lol/static/versions`
- `GET /api/lol/static/{version}/champions`
- `GET /api/lol/static/{version}/items`
- `GET /api/lol/static/{version}/runes`
- `GET /api/lol/static/{version}/spells`

`{version}` is a Data Dragon version (`16.17.1`) or the literal `latest`. Anything
that is not version-shaped is a `400` — the value reaches an upstream URL, so it is
validated rather than trusted.

**Every response carries an absolute `iconUrl`.** Clients must not construct CDN
paths. That is the whole point of these endpoints: the URLs happen to point at Data
Dragon today, and moving the bytes behind this API later becomes a server-side
change with no client release. Champion responses also carry `splashUrl`.

Three details these endpoints exist to stop every client re-learning:

- Champion `id` is the NUMERIC id that match data carries; `alias` is Data Dragon's
  string handle and is what icon filenames use. They differ for some champions
  (`Wukong` is `MonkeyKing`).
- Summoner spell `id` is likewise the numeric id, not the handle — Data Dragon's own
  payload inverts these.
- `runes` returns individual runes, the top-level STYLES (a rune page's
  `primaryStyleId`/`subStyleId` point at those), and stat shards. Riot does not
  publish shards in `runesReforged.json` at all, so a client without them renders
  three of every rune page's nine slots as bare numbers.

Responses are cached server-side, so the CDN is hit roughly once per patch for the
whole user base. The version LIST has a short TTL because it is how a new patch is
discovered; per-patch content is cached for 24h since a shipped patch never changes.
A `503` means Data Dragon is unreachable, as distinct from a `400` for a bad
request — the desktop client classifies those differently to decide whether to show
an outage screen.

These are read-only and served from the CDN rather than the `ChampionVersion` /
`ItemVersion` / `RuneVersion` tables. Those tables exist for analytics (balance
hashes, role pooling) and carry neither summoner spells nor rune icon paths.

### LoL Leaderboards

- `GET /api/lol/leaderboards`

Returns a public ranked leaderboard for one platform region. Query parameters:

- `region`: public region slug or platform token such as `na`, `NA1`, `euw`, or `EUW1` (default `na`)
- `queue`: `solo` or `flex` (default `solo`)
- `championId`: optional positive champion ID; when present, ranks tracked champion specialists from the active ranked season
- `role`: optional `TOP|JUNGLE|MIDDLE|BOTTOM|UTILITY`; applies only with `championId`
- `limit`: `1` to `100` (default `100`)
- `minimumChampionGames`: `1` to `100` (default `5`)

Regional boards are ordered by tier, league points, wins, and losses. Champion boards are ordered by champion-game sample, ranked tier, league points, and champion win rate. Responses include the resolved platform region and queue, generation time, profile identity, current rank and record, plus champion games, wins, win rate, and KDA when champion filters are active.
Champion-board region filtering uses the match's recorded platform region, so an account transfer does not move historical games between regional boards.

Early-patch semantics:
- Analytics endpoints default to the active patch and support a `patch` query parameter for stored historical patches.
- Historical patch requests do not fall back to another patch; unknown patch values return empty `200 OK` payloads with the requested patch echoed.
- Responses now include `sample` metadata for UI messaging:
  - `sampleStatus` (`sufficient`, `low_sample`, `no_data`)
  - `sampleSize`
  - `minimumRecommendedSampleSize`
  - `patchAgeHours`
  - `isEarlyPatchWindow`
  - `patchPhase` (`bootstrap`, `provisional`, `maturing`, `steady`)
  - `isProvisional`
- `low_sample` and `no_data` are expected during early patch windows while ingestion ramps up.
- Tier-list entries carry `movement` / `previousTier` for the persisted region=ALL default scopes (rank scope `all` or `EMERALD_PLUS`); they are omitted (movement `SAME`/null) for specific-region or exact-tier views (computed live) and when no previous patch exists.

`queue` query semantics across tier list, patch options, champion profile, win rates, builds, and matchups:
- `solo` (or omitted): Ranked Solo/Duo (`RANKED_SOLO_DUO`)
- `flex`: Ranked Flex (`RANKED_FLEX`)
- `aram`: ARAM (`ARAM`)
- `arena`: Arena (`ARENA`)
- Unsupported values return `400 Bad Request` instead of silently falling back to Solo/Duo.
- Solo/Duo and Flex retain lane roles. ARAM and Arena use the synthetic role `ALL`; their champion pages hide lane-only matchup UI and return an empty matchup collection because those modes have no stable lane pairing.
- Flex rank scopes use current Flex rank. ARAM/Arena rank scopes use current Solo/Duo rank as a player-skill segment.

Tier methodology (`GET /api/lol/analytics/tierlist`):
- Tiers are **per-role-first**: a champion is graded only against same-role peers. The unified ("All Roles", `role` omitted) list shows each champion at its **primary (most-played) role**; `role` on each entry is that graded role.
- The grade is driven by **strength = win-rate delta vs the role baseline**, with empirical-Bayes shrinkage toward that baseline (low-sample champions shrink to ~0 delta). Tiers are **absolute cutoffs** on that delta (config-driven), so `S` means a real, sample-resolvable edge and `S` may be **empty on a balanced patch**. The prior-fit and tier eligibility gates scale with the selected role volume between calibrated safety bounds; `isLowSample=true` champions are clamped to `B` so thin evidence cannot produce an extreme grade in either direction.
- Pick rate and ban rate are **not** in the strength score — they feed a separate popularity axis (`contestedScore`).
- `TierListResponse.confidence` reports whether the selected scope has a meaningful tier spread: `RESOLVED` (multiple tiers and at least one champion over the adaptive floor), `FLAT` (adequate samples but one tier), or `INSUFFICIENT` (no champion clears the floor / no data).
- `TierListEntry` fields include `strengthScore` (signed delta vs role baseline), `contestedScore` (popularity/meta-presence index), `roleBaseline` (the role's baseline win rate), and `isLowSample`. `compositeScore` is retained as a back-compat alias of `strengthScore` and is slated for removal.

`rankTier` query semantics across tier list, win rates, builds, and matchups:
- `all` (or omitted): no rank filter
- Exact tier token: `IRON|BRONZE|SILVER|GOLD|PLATINUM|EMERALD|DIAMOND|MASTER|GRANDMASTER|CHALLENGER`
- Tier scope token: `EMERALD_PLUS` (alias `EMERALD+`) = `EMERALD` and above

`region` query semantics across tier list, win rates, builds, and matchups:
- `ALL` (or omitted): global aggregate across enabled ingestion regions
- Concrete platform region token: for example `NA1|EUW1|EUN1|KR`. Historical analytics are keyed by
  the match's recorded platform region, not a participant account's current region after a transfer.
- Supported public region tokens are discoverable via `GET /api/lol/analytics/regions`
- Tier list, builds, and matchup responses now echo the resolved `region` field so the UI can badge active scope without guessing

`patch` query semantics across tier list, win rates, builds, matchups, and pro builds:
- Omitted: use the backend-owned active analytics patch
- Exact patch token: query that patch's persisted match/static-data slice
- Available patch options are discoverable via `GET /api/lol/analytics/patches`

`GET /api/lol/analytics/status` returns the backend-owned active LoL analytics patch metadata:
- `patch`
- `activePatchReleasedAtUtc`
- `activePatchDetectedAtUtc`

`GET /api/lol/analytics/patches` returns public patch options:
- `patch`
- `releasedAtUtc`
- `detectedAtUtc`
- `isActive`
- `matchCount`
- `queueFamily`
- `rankedSoloDuoMatchCount` (backward-compatible alias of `matchCount`; use `matchCount` for new clients)

Item and rune analytics are public Ranked Solo/Duo corpus reads. They accept optional `region`
and `patch` filters with the same semantics as champion analytics. Index responses rank resources
by observed player-games and include pick rate, win rate, and the three most common champion-role
pairs. Detail responses expand that breakdown to the top 100 champion-role samples. Item rows are
deduplicated per participant and restricted to completed build-impact items or upgraded boots;
rune rows exclude stat shards. All rates are `0..1` ratios. Champion-level `pickRate` uses that
champion-role's total games as its denominator, while `shareOfResourceUses` uses all observed uses
of the selected item/rune. These are descriptive correlations, not causal item/rune power scores.

`GET /api/lol/analytics/champions/{championId}/synergies` accepts `role`, `rankTier`,
`region`, `queue`, and `patch`. It measures actionable same-team role pairs: Bottom+Utility,
Jungle+lane, and lane+Jungle. Each partner includes games, wins, pair win rate, pick rate within
the focal champion-role sample, raw win-rate delta from that focal baseline, and a Wilson
confidence score. `bestPartners` is ordered by confidence-adjusted lift so tiny lucky samples do
not outrank supported pairings. The same `synergies` payload is included by the aggregate
`/profile` response; roleless queues return an empty pairing set.

`GET /api/lol/analytics/champions/{championId}/builds` includes full rune setup per build:

- `builds[]` is ordered by `games × winRate` (observed wins), balancing sample support and results rather than sorting by raw win rate alone. A later variant can therefore have a higher rate on fewer games.
- `primaryStyleId`, `subStyleId`
- `primaryRunes` (4), `subRunes` (2), `statShards` (3)
- Each build (and each build-path variant below) carries `games`, `winRate`, and `pickRate`. `pickRate` is the share of scoped games using that variant (0–1): main `builds[]` vs the champion+role+scope total; build-path sections vs their own section denominator. It is additive and defaults to `0` for snapshots computed before the field existed (clients hide a `0` pick rate); real values populate on the next analytics refresh.
- Build item lists include only completed, build-impact items (no components, trinkets, wards, or consumables).
- If patch item metadata is temporarily incomplete, the service uses a legacy exclusion fallback so builds still render while metadata refresh catches up.
- The response also carries a sectioned, timeline-derived build path (all optional — `null`/empty when timeline data has not been ingested for the champion/patch):
  - `summonerSpells[]` — top normalized spell pairs with `games`/`winRate`/`pickRate`
  - `skillOrder` — `{ firstThree, maxOrder, games, winRate, pickRate }` (e.g. `firstThree: "QWE"`, `maxOrder: "Q>E>W"`)
  - `startingItems[]` — top opening item sets with `games`/`winRate`/`pickRate`
  - `boots[]` — boots options with `games`/`winRate`/`pickRate`
  - `coreBuildPath[]` — the ordered 1st→2nd→3rd core items, each with `games`, `winRate`, `pickRate`, and `avgCompletionMinute`
  - `situationalSlots[]` — 4th/5th/6th slots, each with the top item `options[]` (each option carries `games`/`winRate`/`pickRate`)
- These sections degrade gracefully: champions/patches without ingested timeline build data return the existing build rows with the new fields null.

`GET /api/lol/analytics/champions/{championId}/profile` returns the champion detail payload in one request:
- Query filters: `role`, `rankTier`, `region`, `queue`, `patch`
- Response: `{ championId, effectiveRole, winRates, builds, matchups, grade, queueFamily, trend }`
- `grade` (`ChampionGradeDto`, nullable) is the champion's tier grade for the resolved `effectiveRole` + scope — the **same** grade the tier list shows for that champion in that role (so the detail page hero is consistent with the list). It carries `tier`, `strengthScore`, `winRate`, `pickRate`, `banRate`, `contestedScore`, `games`, `roleBaseline`, `isLowSample`, `movement`, `previousTier`, `role`, `rankScope`. Null when the champion is not graded in scope (render "Unrated").
- The endpoint reuses the cached winrate, build, matchup, and tier-list aggregates. For Solo/Duo and Flex, when `role` is omitted it chooses the most-played role from winrates; if a scoped rank filter has no winrate rows, it uses all-rank winrates only to choose the role while keeping the requested rank filter for build and matchup data. ARAM/Arena resolve `effectiveRole=ALL`.
- The build and matchup reads run in separate backend scopes so their cached aggregate reads can execute concurrently without sharing an EF `DbContext`.
- `trend` is the last 12 durable patch-grade points for the same champion, queue, role, and rank scope at the global region grain. Each point carries `patch`, `releasedAtUtc`, `tier`, `games`, `winRate`, `pickRate`, `banRate`, `strengthScore`, and `isLowSample`; it is empty for exact-tier scopes that are not persisted. The champion page renders a real patch-over-patch win-rate chart only when at least two points exist.

Additional analytics fields:
- Tier list and champion winrate surfaces include queue-scoped `banRate`.
- Champion winrate rows include `roleRank` and `rolePopulation` when resolvable.
- Matchups include timeline-derived `avgGoldDiffAt15`, optional `avgXpDiffAt15`, and `allMatchups[]` in addition to `counters[]` and `favorableMatchups[]`.
- Matchup responses include timeline quality metadata:
  - `timelineCoverageRatio`
  - `timelineSampleSize`
  - `timelineDataFreshnessUtc`

`GET /api/lol/analytics/champions/{championId}/pro-builds` supports optional filters:
- `region` (`ALL` or supported platform-region token such as `NA1|EUW1|EUN1|KR`)
- `role` — when omitted (or `ALL`), the champion's **most-played lane** is resolved from the cached win-rate aggregate (mirrors the profile endpoint) and echoed back as `role`, so the landing view is lane-scoped instead of the heavier cross-role aggregate. Any other unrecognized role is rejected with `400`.
- `scope`: `pro` (reviewed professional accounts, `IsPro`), `highelo` (verified Master+ one-tricks, `IsHighEloOtp`), or `all` (either). Defaults to `pro`.
- `patch`

The cross-role aggregate (no resolvable lane) bounds its participant scan to the most-recent `Analytics:Compute:ProBuildMaxParticipantRows` rows (default 1500) so the wide `role=ALL` + `scope=all` + `region=ALL` pool cannot command-timeout.

Response includes:
- `scope`
- `recentProMatches[]` — items are returned in **purchase order** (timeline-derived, final inventory as fallback); each match also carries `spell1Id`/`spell2Id` and an optional `skillOrder`
- `topPlayers[]`
- `commonBuilds[]` — non-empty item sets in purchase order, ranked by games and then win rate (empty inventories remain available only on their raw `recentProMatches[]` rows)

`GET /api/lol/analytics/pro/champions` (public) returns champions ranked by pick/play frequency among tracked pro / high-elo players (the "Pro Solo Queue Builds" home ranking). These are ranked solo-queue observations, not tournament drafts, esports schedules, or official match results. Optional filters:
- `region` (`ALL` or supported platform-region token such as `NA1|EUW1|EUN1|KR`)
- `scope`: `pro` (reviewed professional accounts, `IsPro`), `highelo` (verified Master+ one-tricks, `IsHighEloOtp`), or `all` (either). Defaults to `pro`.
- `patch`

Response: `{ patch, region, scope, champions[], sample }` where each champion entry is `{ championId, games, wins, winRate, uniquePlayers }`, ordered by games descending. Cached 24h (`analytics` + `proplayrate` tags).

`GET /api/lol/analytics/pro/players` (public) returns the public tracked-pro roster (`IsActive && IsPro`). Optional `region` filter. Response: `{ region, players[] }` where each player is `{ proName, teamName, platformRegion, gameName, tagLine }` (no internal identifiers). Cached 24h (`analytics` + `proroster` tags).

### Live Game (`AppOnly`)

- `GET /api/lol/summoners/{region}/{gameName}/{tagLine}/live-game`
- `POST /api/lol/summoners/{region}/{gameName}/{tagLine}/live-game/probe`
- Returns the latest worker-observed snapshot. `lastUpdatedUtc` and `dataAgeSeconds` expose
  freshness; the Web API does not call Riot directly.
- The probe endpoint queues a fresh Spectator-V5 check on the credentialed worker and returns
  `202` with `status` (`queued` or `in_progress`), `poll`, and `retryAfterSeconds`. A per-Riot-ID
  fenced lease coalesces repeated browser checks; the frontend polls the GET until it observes the
  newly persisted snapshot. Both routes are exposed only through the narrow AppOnly BFF allowlist.
- Active-game participants include champion, summoner spells, selected perk IDs/styles, and a
  stored-data analysis projection with Solo/Duo rank, recent-20 win rate/KDA, signed current streak
  (positive wins, negative losses), and the three most-played champions in that recent window.
- Team summaries are directional scouting signals derived from the stored participant sample, not
  predictions or live objective telemetry. Offline responses contain an empty participant list.

### Operational Health

- `GET /health/live`
- `GET /health/ready`

### Auth and Keys

- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/auth/refresh`
- `POST /api/auth/logout`
- `POST /api/auth/password-reset` (anonymous; returns the same generic `200 OK` for existing and unknown accounts; `503` when SMTP recovery is disabled/unconfigured)
- `POST /api/auth/password-reset/complete` (anonymous; consumes a one-time token and returns `204`; invalid/expired tokens return `400`)
- `POST /api/auth/riot/authorize` (anonymous; returns the configured Riot OAuth authorization URL for a caller-generated state value; `503` while RSO is disabled/unconfigured)
- `POST /api/auth/riot/complete` (anonymous; exchanges a one-time Riot code, signs in an existing linked account or creates a Riot-only account, and returns site tokens)
- `GET /api/auth/me` (`AppOrUser`)
- `GET /api/auth/keys` (`AdminOnly`)
- `POST /api/auth/keys` (`AdminOnly`)
- `POST /api/auth/keys/{id}/revoke` (`AdminOnly`)
- `POST /api/auth/keys/{id}/rotate` (`AdminOnly`)

Riot account linking (`UserOnly`):
- `GET /api/users/me/riot-account` returns the verified main or `404` when none is linked.
- `POST /api/users/me/riot-account/complete` exchanges a one-time Riot code and links its verified PUUID to the signed-in account. A PUUID can belong to only one account.
- `DELETE /api/users/me/riot-account` unlinks only when the user has an email/password credential; Riot-only accounts cannot remove their sole sign-in method.
- Riot access/refresh tokens are never persisted. Only PUUID, Riot ID, selected platform region, and link/verification timestamps are stored.

Auth behavior notes:
- Registration duplicate-email responses are intentionally generic (`Registration failed.`).
- Password minimum length is 12 characters.
- Login performs a current-cost dummy PBKDF2 verification when the email is unknown, so the invalid-credential response does not reveal account existence through a cheap early return.
- Registration still returns `409 Conflict` for an existing address. This is an intentional product tradeoff until an email-verification flow can provide a genuinely uniform accepted response without returning a session for an existing account.
- Password-reset tokens are random, stored only as SHA-256 hashes, expire after the configured lifetime (30 minutes by default), and are single-use. Completing a reset revokes every active refresh token for the account.

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
- `GET /api/admin/pro-summoners/candidates?status=pending|approved|rejected`
- `POST /api/admin/pro-summoners/candidates/{id}/approve`
- `POST /api/admin/pro-summoners/candidates/{id}/reject`

The candidate endpoints expose staged Leaguepedia directory rows. Approval requires a confirmed
Riot game name, tag line, and platform region (plus optional PUUID), creates the durable tracked
professional account, and records the source identity. Candidate rows never appear in public
pro-build analytics before approval.

### User Preferences (`UserOnly`)

- Favorites and preferences under `/api/users/me/*`. `GET /api/users/me/favorites` includes the
  latest stored live-game observation (`liveState`, `liveGameId`, `liveObservedAtUtc`) and an
  `isLive` convenience flag. `isLive` is true only for an `in_game` observation no more than ten
  minutes old, so a stale worker snapshot cannot present a player as currently live.

### Build Lab and Adjusted WPA

`GET /api/lol/analytics/build-lab/{championId}` is the public, rate-limited decision-analytics
surface. `role` is required. Optional context includes `opponentChampionId`, `patch`, and `region`;
`section=items|runes|spells` and `mode=supported|impact|common` control the decision family and
ranking. Ordered `itemPath`, `runeSelections`, and `spellPair` query values condition the next
supported stage and make the complete state permalinkable.

The response distinguishes requested from effective context and includes promoted generation,
dataset, static-data, model, cutoff, patch, and region provenance. Unsupported selected prefixes
remain selected and return an explicit unavailable reason; the API never silently broadens or discards
a path.

**A gate-failing cell withholds its numbers, not its evidence.** When `isPublishable` is false the
response nulls `adjustedWpa`, `confidenceLow`, `confidenceHigh`, **and the descriptive `rawWinRate` /
`pickRate`** — a gated candidate must not render a headline win rate one click behind its own
"insufficient evidence" label. `observedCount` and `effectiveSampleSize` are *always* populated, so a
client can show how thin a cell is and `evidenceQuality` / `unavailableReason` say why it was withheld.
The same rule applies to `pathEstimate`: `estimatedWinProbability`, `adjustedLift`, and both bounds are
null when the path failed its gates, while its counts remain visible.

**`evidenceTier` decides how much of a cell may be shown.** Publication is not all-or-nothing.
Patches ship fortnightly, and a cell needs far more evidence to pin a ≤3pp interval than to say which
side of "typical" it falls on, so gating everything on the interval would leave the lab empty for most
of a patch.

| `evidenceTier` | Meaning | Client renders |
| --- | --- | --- |
| `NUMERIC` | Every v1 gate passed | `adjustedWpa` and its interval |
| `BUCKETED` | Sample gates passed; only the interval-width gate failed, and the posterior still concentrates in one bucket | `evidenceBucket` (`ABOVE_AVERAGE` / `TYPICAL` / `BELOW_AVERAGE`); no number |
| `DESCRIPTIVE` | Not enough evidence for either | pick rate and timing only |

`evidenceBucket` is non-null only at the `BUCKETED` tier — a numeric cell shows its number and a
descriptive one has not earned a direction. Bucketing never relaxes the sample, overlap, balance, or
stability gates; it trades away *only* the interval width, and only when the modeler measured at least
80% posterior mass on one side. `available` is true once any candidate is numeric **or** bucketed.

Ranking is deliberately independent of the tier: `mode=supported` orders by the interval's lower bound
where one exists and by the point estimate otherwise, so a bucketed candidate is ranked on the evidence
it has rather than sinking below cells with no evidence at all.

**Regional fallback is decided per cell, not per response.** For a regional request each individual
estimate keeps its regional number only when that cell is publishable *and* differs meaningfully
from the pooled global baseline after multiple-comparison correction; otherwise that one cell serves
the global estimate. A single response therefore mixes regional and global rows, and each row states
which it is (`fallbackScope`) — there is no whole-response switch to `GLOBAL`, and a region with a
few thin cells does not lose its regional numbers everywhere. `context.effectiveRegion` reports the
requested region as soon as *any* row in the response is regional, so it summarizes the mix rather
than promising every row is regional.

**Patch resolution.** Omitting `patch` serves the active generation's own patch. An explicit `patch`
is servable when it is the active generation's patch **or** appears in that generation's borrowed
`includedPatches` set (`provenance.includedPatches`); anything else returns `available: false` with an
explicit "outside the promoted generation's modeled patch set" reason instead of silently answering
for a different patch. `context.requestedPatch` echoes what the caller asked for and
`context.effectivePatch` is the generation's patch, so the two differ whenever a borrowed patch was
requested. Because promotion retires every other generation, a borrowed patch is only ever addressable
through the active generation.

`GET /api/lol/analytics/champions/{championId}/profile` now includes an optional
`recommendation` summary for Ranked Solo/Duo. It contains the best-supported first item, rune
choice, and spell pair from the same promoted generation so the champion page does not need a
second request.

Saved builds are complete decision/filter states, not frozen estimates:

- `GET /api/users/me/lol/saved-builds?page=&pageSize=` (`UserOnly`)
- `POST /api/users/me/lol/saved-builds` (`UserOnly`)
- `PUT|DELETE /api/users/me/lol/saved-builds/{savedBuildId}` (`UserOnly`, owner only)
- `POST /api/users/me/lol/saved-builds/{savedBuildId}/repair` (`UserOnly`, owner only)
- `POST|DELETE /api/users/me/lol/saved-builds/{savedBuildId}/share` (`UserOnly`, owner only)
- `GET /api/lol/saved-builds/{shareId}` (public, unguessable read-only token, rate limited)

The list is a paginated envelope `{ items, page, pageSize, totalCount, hasMore }` ordered by
`updatedAtUtc` descending. `pageSize` is clamped to the configured maximum and `page` is clamped
against `totalCount`, so an absurd page number returns an empty page rather than an error. `POST`
enforces a **per-account cap** and answers `409 Conflict` (ProblemDetails) at the limit — delete a build
before saving another; the cap is deliberately a conflict, not a 400, because the request itself is
valid. `DELETE` is idempotent (204 even when the build is already gone). Share revocation takes effect
on the next read, and the public share route is metered per client IP so the token space cannot be
brute-forced.

Each build reports its own drift and repairability:

- `compatibilityStatus` is a single most-blocking state, evaluated in this order: `ITEMS_RETIRED` (at
  least one item is unusable), then `NO_SOURCE_GENERATION` (saved while no generation was active, so
  there is no baseline to compare against), then `PATCH_CHANGED` (saved on an older patch), else
  `CURRENT`. Inspect `unavailableItems` and `patch` for the full picture rather than the label alone.
- `unavailableItems` pairs each blocked item with a reason: `RETIRED` (absent from the active patch's
  static data) or `REMOVED_FROM_STORE` (still present but no longer purchasable). `unavailableItemIds`
  is the same set flattened for older clients. Items are reported, never silently replaced.
- `POST .../repair` takes explicit `{ itemId, action, replacementItemId }` choices where `action` is
  `DROP` or `REPLACE`; a `REPLACE` must name an item valid on the active patch. Repair is always the
  user's decision.
- `analyticsChanged` is a **material** change, not a generation-id difference. It is true only when the
  saved setup's own outcome moved under the new active generation: its publishability flipped, or its
  adjusted lift moved further than the configured epsilon. A promotion that leaves this build's numbers
  effectively where they were reports `false`, so the flag means "your build's answer changed", not
  "the model was rebuilt".

Admin generation control is `AdminOnly` and rate limited (`admin-write`):

- `GET /api/admin/analytics/build-lab`
- `POST /api/admin/analytics/build-lab/generations/{generationId}/promote`
- `POST /api/admin/analytics/build-lab/generations/{generationId}/rollback`
- `POST /api/admin/analytics/build-lab/generations/{generationId}/fail`

Promotion revalidates the model calibration/baseline/leakage gates and every action/path evidence
gate inside an atomic active-generation switch, and re-derives the artifact-manifest checksum. That
checksum covers `artifactManifestJson` only — it proves the manifest is populated and self-consistent
with the digest the modeler stored, **not** that the Parquet/model bundle at `artifactUri` is intact.

`promote` answers `204` on success and `409` when the generation is not a valid candidate, failed a
gate, or lost a race for the active pointer. `rollback` answers `204`, `404` when the target is not a
`Ready`/`Retired` generation, and **`409` when a competing promotion took the active pointer
concurrently** — retry once it settles. `fail` abandons a `PendingDataset`/`Modeling`/`Candidate`
generation with an optional `reason` (`204`, or `404` when it is in no failable state); use it to clear a
generation wedged in `Modeling` because the offline modeler died holding the lease. All three write an
`AdminAuditLog` entry (`analytics.buildlab.promote|rollback|fail`) with the actor, target generation,
request id, and success flag, including on the failure paths.

The generation rows returned by `GET` expose `leaseOwner` (diagnostic only — which modeler process
claimed the run) plus `promotionHistoryJson`, an append-only log of every `promote`/`rollback`/`fail`
with actor and reason. A `Modeling` row with no live modeler is reaped automatically: the worker
decides that by probing the modeling advisory lock, not by any timeout. `fail` is the manual
equivalent.

## OpenAPI Generation Workflow

The repo keeps the exported spec committed and uses it to generate the TypeScript client during build/check flows.

The spec is OpenAPI 3.0 with C# nullable-reference-type fidelity: Swashbuckle is configured with `SupportNonNullableReferenceTypes` + `NonNullableReferenceTypesAsRequired` + `UseAllOfToExtendReferenceSchemas` (`Transcendence.WebAPI/Program.cs`), so always-present properties are `required`/non-null and nullable reference properties emit `T | null` in the generated client.

- Export spec: `scripts/openapi/export.sh` (invoked via `pnpm api:spec`)
- Generate client package from the spec: `packages/api-client` (invoked via `pnpm api:client`)

See root `package.json` scripts:
- `api:gen`
- `api:check`
