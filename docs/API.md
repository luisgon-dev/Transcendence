# API

This repo's API contract is defined by the committed OpenAPI spec:

- `openapi/transcendence.v1.json`

If you change endpoints, request/response shapes, or auth semantics, update the spec (see `README.md` and `docs/DEVELOPMENT.md`) in the same PR.

## Authentication

High-level model:

- `AppOnly`: `X-API-Key: <key>`
- `UserOnly`: `Authorization: Bearer <jwt>`
- `AppOrUser`: accepts either

The Next.js web frontend uses route handlers as a BFF:

- Browser talks to Next under `/api/session/*` and `/api/trn/*`
- Next talks to the backend at `TRN_BACKEND_BASE_URL`
- Tokens live in HttpOnly cookies on the web domain (never exposed to browser JS)
- AppOnly calls attach `X-API-Key` server-side from `TRN_BACKEND_API_KEY`

## Key Endpoint Areas (Current)

This is a navigational summary; the OpenAPI spec is the source of truth.

### Summoners and Stats

- `GET /api/summoners/{region}/{name}/{tag}`
- `POST /api/summoners/{region}/{name}/{tag}/refresh`
- `GET /api/summoners/{summonerId}/stats/overview`
- `GET /api/summoners/{summonerId}/stats/champions`
- `GET /api/summoners/{summonerId}/stats/roles`
- `GET /api/summoners/{summonerId}/matches/recent`
- `GET /api/summoners/{summonerId}/matches/{matchId}`

### Analytics

- `GET /api/analytics/tierlist`
- `GET /api/analytics/champions/{championId}/winrates`
- `GET /api/analytics/champions/{championId}/builds`
- `GET /api/analytics/champions/{championId}/matchups`
- `POST /api/analytics/cache/invalidate` (`AppOnly`)

### Live Game (`AppOnly`)

- `GET /api/summoners/{region}/{gameName}/{tagLine}/live-game`

### Auth and Keys

- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/auth/refresh`
- `GET /api/auth/me` (`AppOrUser`)
- Key management endpoints (`AppOnly`)

### User Preferences (`UserOnly`)

- Favorites and preferences under `/api/users/me/*`

## OpenAPI Generation Workflow

The repo keeps the exported spec committed and uses it to generate the TypeScript schema.

- Export spec: `scripts/openapi/export.sh` (invoked via `pnpm api:spec`)
- Generate client schema: `packages/api-client` (invoked via `pnpm api:client`)

See root `package.json` scripts:
- `api:gen`
- `api:check`

