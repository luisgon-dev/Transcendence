# Development

This repo contains a .NET backend (API + background worker) and a Next.js web frontend.

## Prerequisites

- .NET SDK (see `global.json`)
- Docker Desktop (recommended) or local:
  - PostgreSQL 16+
  - Redis 7+
- Node.js (recommended: Node 22)
- pnpm (repo pins `pnpm@10.22.0` in root `package.json`)

## Quick Start (Recommended)

1. Start infrastructure + backend:

```bash
cp .env.example .env
pnpm dev:stack:up
```

`dev:stack:up` runs `docker compose up --build -d`, so the full stack starts in the background. Use
`pnpm dev:stack:down` to stop it.

Compose reads local backend credentials from the repo-root [`.env.example`](../.env.example). Copy it to an untracked `.env` before first run. The current Riot key variable is:

- `RIOT_API_KEY_LOL`

2. Install JS dependencies:

```bash
pnpm install
```

3. Install repo Git hooks (recommended once per clone):

```bash
pnpm hooks:install
```

4. Configure the web app:

```bash
cp apps/web/.env.example apps/web/.env.local
```

Set:
- `TRN_BACKEND_BASE_URL=http://localhost:8080`
- `TRN_BACKEND_API_KEY=<api key for AppOnly endpoints>`
  - Note: the web app allowlists AppOnly proxy paths; this key is not exposed as a generic proxy capability.

Optional (admin bootstrap):
- `ADMIN_BOOTSTRAP_EMAIL_0=<your-admin-email>` before `docker compose up`
- Register/login that email in web app, then open `/admin`

Optional:
- `TRN_BACKEND_TIMEOUT_MS=10000` (server-side backend timeout, milliseconds)
- `TRN_ERROR_VERBOSITY=safe|verbose` (controls user-visible error detail from Next route handlers)
- `TRN_PUBLIC_ORIGIN=https://transcend.kronic.one` (optional locally, required in production; canonical public origin for metadata, social cards, sitemap/robots URLs, and credentialed BFF same-origin/CSRF checks. Setting it prevents canonical URLs or the CSRF comparison from being influenced by client-supplied host headers; local development falls back to `http://localhost:3000`)

5. Run the web app:

```bash
pnpm web:dev
```

Web: `http://localhost:3000`

API health:
- `http://localhost:8080/health/live`
- `http://localhost:8080/health/ready`

## Local E2E Workflows

Use full Compose when you want the simplest end-to-end path and you need the worker running for LoL refresh flows:

```bash
cp .env.example .env
pnpm e2e:stack
```

That script:
- creates `.env` from `.env.example` if needed
- starts the full Docker Compose stack in detached mode
- waits for `http://localhost:8080/health/ready`
- waits for `http://localhost:3000`
- runs Playwright against `http://localhost:3000`

For a faster frontend loop, keep backend services in Compose and run Next locally:

```bash
docker compose up --build -d postgres redis webapi service
cp apps/web/.env.example apps/web/.env.local
pnpm web:dev
pnpm e2e:local
```

Rule of thumb:
- Use `pnpm e2e:stack` for true local E2E and worker verification.
- Use the hybrid mode for day-to-day UI changes when you want faster frontend rebuilds.

## Run Without Docker (Backend)

### Secrets

`Transcendence.WebAPI`:

The WebAPI host is keyless. It does not need Riot API keys to start, serve reads, or export Swagger.

```bash
dotnet user-secrets set "ConnectionStrings:MainDatabase" "Host=localhost;Port=5432;Database=transcendence;Username=postgres;Password=changme" --project Transcendence.WebAPI
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:Jwt:Key" "CHANGE_THIS_TO_A_REAL_32+_CHAR_SECRET" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:Jwt:RequireKeyInDevelopment" "false" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:AdminBootstrap:Emails:0" "admin@example.com" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:BootstrapApiKey" "trn_bootstrap_dev_key" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:BootstrapApiKeyEnabledInDevelopmentOnly" "true" --project Transcendence.WebAPI
# Optional password recovery (use a local SMTP catcher or real provider)
dotnet user-secrets set "Auth:PasswordReset:Enabled" "true" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:PasswordReset:PublicBaseUrl" "http://localhost:3000" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:PasswordReset:Smtp:Host" "localhost" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:PasswordReset:Smtp:Port" "1025" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:PasswordReset:Smtp:EnableSsl" "false" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:PasswordReset:Smtp:FromAddress" "no-reply@local.dev" --project Transcendence.WebAPI
# Optional Riot Sign On (requires an approved production RSO client)
dotnet user-secrets set "Auth:RiotRso:Enabled" "true" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:RiotRso:ClientId" "your-rso-client-id" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:RiotRso:ClientSecret" "your-rso-client-secret" --project Transcendence.WebAPI
dotnet user-secrets set "Auth:RiotRso:RedirectUri" "http://localhost:3000/api/session/riot/callback" --project Transcendence.WebAPI
```

Security notes:
- `Auth:Jwt:Key` is required outside `Development`; startup fails if missing or if the known development placeholder is used.
- `Auth:BootstrapApiKeyEnabledInDevelopmentOnly=true` rejects bootstrap API key auth outside `Development`.
- Password recovery remains unavailable until `Auth:PasswordReset:Enabled=true`, a valid public base URL, SMTP host, and from-address are all configured. SMTP credentials are optional for trusted local relays; never commit them. Docker uses the matching `PASSWORD_RESET_*` variables documented in `.env.example`.
- Riot Sign On remains unavailable until `Auth:RiotRso:Enabled=true` and an approved RSO client ID,
  client secret, and exact registered callback URI are configured. Production endpoints/callbacks must
  use HTTPS; loopback HTTP is accepted only for local development. The matching Docker variables are
  `RIOT_RSO_*` in `.env.example`. RSO credentials belong only on the Web API host, never in `apps/web`.

`Transcendence.Service`:

```bash
dotnet user-secrets set "ConnectionStrings:MainDatabase" "Host=localhost;Port=5432;Database=transcendence;Username=postgres;Password=changme" --project Transcendence.Service
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379" --project Transcendence.Service
dotnet user-secrets set "RiotApi:League:ApiKey" "RGAPI-your-lol-key" --project Transcendence.Service
```

Local defaults:
- Shared backend defaults live in [`config/backend.shared.json`](../config/backend.shared.json) and use PostgreSQL/Npgsql on `localhost:5432` with `postgres/changme`, plus Redis on `localhost:6379`.
- `Transcendence.WebAPI/appsettings.json` and `Transcendence.Service/appsettings.json` contain host-only settings layered on top of the shared config.
- User-secrets remain the recommended override for local credentials and Riot keys.

Riot API key model:
- Only `Transcendence.Service` resolves the Riot key from the canonical nested setting:
  - `RiotApi:League:ApiKey`
- The legacy `ConnectionStrings:RiotApi` setting is no longer used.

### Observability & alerting (ops-tools profile)

Prometheus + Grafana are their own stack — the single source of truth for both local dev and prod — at [`config/monitoring/`](../config/monitoring/README.md) (separate from the app `compose.yml`). Bring the app stack up first (it creates the shared `transcendence_transcendence-net` network), then:

```bash
# Copy config/monitoring/secrets/grafana_admin_password.example to
# config/monitoring/secrets/grafana_admin_password and replace the placeholder.
# Grafana → http://localhost:3300 (admin + file-backed password), Prometheus → http://localhost:9090
docker compose -f config/monitoring/compose.yml up -d
```

Grafana is file-provisioned from `config/monitoring/grafana/provisioning`, including six dashboards
(fleet overview, read API, worker runtime, analytics refresh, Riot API, and ingestion rate gate), its datasource, and
`alerting/rules.yml` + `alerting/contactpoints.yml`. Prometheus scrapes the API, worker, host,
PostgreSQL, and Redis; provisioned liveness alerts cover each application/database/cache target in
addition to PostgreSQL connection saturation, Redis rejected connections, API 5xx ratio, p95 latency,
and host disk space. PostgreSQL exporter credentials belong in
`config/monitoring/.env` and should use a dedicated `pg_monitor` role (see the monitoring runbook).

- **`DISCORD_ALERT_WEBHOOK_URL`** (in `config/monitoring/.env`, see `.env.example`) — the `discord` contact point's URL is interpolated from it (`$VAR` provisioning interpolation). Grafana 13 **refuses to start** on an empty contact-point URL, so when unset the base compose falls back to a no-op placeholder URL: Grafana boots and the rules are visible in Grafana → Alerting, but alerts don't deliver anywhere real. In prod, set it to the same incoming webhook the worker's ingestion alerter uses (`Alerts__Webhook__Url`). Locally the `up == 0` rules go `pending`/`Alerting` because no webapi/worker target is scraped — expected.

Prod deploys the same stack with the `compose.prod.yml` overlay (admin-password file secret); the sync/deploy runbook is in `config/monitoring/README.md`. See `docs/ARCHITECTURE.md` → *Metrics-based alerting* for the rule set and DB/Redis coverage rationale.

### Database migrations

```bash
dotnet ef database update --project Transcendence.Service --startup-project Transcendence.Service
```

Migration policy:
- Do not hand-author or hand-edit EF migration files.
- Generate migrations only via EF CLI (for example: `dotnet ef migrations add <Name> --project Transcendence.Service --startup-project Transcendence.Service`).
- **Hot-table index/DDL is applied out-of-band, not via `database update`** — see the recipe below. CI (the migration-safety check) fails a PR that adds a non-concurrent `CreateIndex` or a defaulted `AddColumn` on `Summoners` / `Matches` / `MatchParticipants` / `MatchParticipantTimelineSnapshots` and points back here.

Automatic migrations on startup (`Database:AutoMigrate`):
- The **worker host** (`Transcendence.Service`) applies pending migrations on startup when `Database:AutoMigrate` is `true` (set in `config/backend.shared.json`, so it ships baked into the image). Only the worker can — it is the migrations assembly (`MigrationsAssembly("Transcendence.Service")`); the WebAPI host doesn't reference it and so never migrates (it relies on the worker). EF Core 9+ takes a database-wide migration lock, so concurrent worker instances stay safe. This removes the manual post-deploy `dotnet ef database update` step a migration-bearing release used to require (the WebAPI may briefly 500 on a brand-new table during the deploy window until the worker finishes).
- `Database:MigrateOnly=true` is the deploy-pipeline mode: after migrations finish, the worker host
  exits before starting Hangfire or recurring-job startup. `poll-deploy.sh` runs the newly pulled worker
  image in this mode before replacing any app service, then aborts/quarantines the release on failure.
- Locally, `dotnet run` with the shared config also auto-migrates your dev DB, so the manual `database update` above is optional; override with `Database:AutoMigrate=false` (user-secrets / `appsettings.Development.json`) if you want manual control. The OpenAPI export host force-disables it (`--Database:AutoMigrate=false`) since it boots against a throwaway connection.
- **Hot-table index migrations are the exception** — auto-migrate would run them as a blocking `CREATE INDEX`. Apply them via the out-of-band recipe below (create the index concurrently, then record the migration in `__EFMigrationsHistory`) **before** the deploy so the migration is already applied and auto-migrate skips it. The migration-safety CI gate failing your PR is the signal to use the recipe.
- **CI applies the full chain to real Postgres.** Because prod auto-migrates on worker startup, a migration that compiles and passes the drift check but fails at *runtime* (PG-specific DDL, ordering, or type error) would crash-loop the worker while the deploy still reports success. The `migration-apply` job (`.github/workflows/ci-web-backend.yml`) spins up an ephemeral `postgres:16` service and runs `dotnet ef database update` from an empty database on every PR to surface those failures pre-merge — SQLite/InMemory tests cannot. It applies to an *empty* DB, so data-dependent migration failures still need a seeded follow-up. The `Transcendence.IntegrationTests` tier (see Backend Tests) additionally applies the chain **in-process** against a Testcontainers Postgres 18 and asserts every migration is applied with none pending — catching migration/model drift and PG-runtime faults from within `dotnet test`.

#### Applying index migrations to hot tables

`Summoners` (~4M+ rows), `Matches`, `MatchParticipants`, and `MatchParticipantTimelineSnapshots` (~22.5M rows) are large and continuously written by ingestion. EF's generated `CreateIndex` emits a plain `CREATE INDEX`, which holds a `SHARE` lock for the entire build and blocks ingestion writes for its duration. **Do not** apply such a migration with `dotnet ef database update`. Split the apply instead:

1. Isolate the index in its own migration (don't bundle it with other DDL) so the steps below stay clean.
2. Read the index name / table / columns from the generated migration's `Up()`.
3. Build it without a lock, directly in psql. `CONCURRENTLY` cannot run inside a transaction, so run it as a standalone statement:

   ```sql
   CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Summoners_Region_UpdatedAt"
     ON "Summoners" ("PlatformRegion", "UpdatedAt");
   ```

4. Verify the build succeeded — a failed concurrent build leaves an INVALID index that serves nothing and must be dropped and rebuilt:

   ```sql
   SELECT indexrelid::regclass AS index, indisvalid
   FROM pg_index WHERE indrelid = '"Summoners"'::regclass;
   -- indisvalid must be 't'. If 'f':  DROP INDEX CONCURRENTLY "<name>";  then redo step 3.
   ```

5. Record the migration as applied so EF treats it as done and never emits the locking `CREATE INDEX`:

   ```sql
   INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
   VALUES ('20260612055024_AddSummonerRegionUpdatedAtIndex', '10.0.2');
   ```

   Use the migration's full id (`<timestamp>_<Name>`) and the EF Core product version from the migration's `.Designer.cs` `ProductVersion` annotation. Afterward, `dotnet ef migrations has-pending-model-changes` must report no drift.

Prod precedent: `IX_Summoners_Region_UpdatedAt` was applied exactly this way — see the note on the `HasIndex(PlatformRegion, UpdatedAt)` call in `Transcendence.Data/TranscendenceContext.cs`.

#### Troubleshooting: `MSB3552` / `CS2001` on `**/*.cs` or `**/*.resx`

If a **single-project** build or a `dotnet ef` design-time build of `Transcendence.Service` fails with the SDK default item glob shown *literally* —

```
error MSB3552: Resource file "**/*.resx" cannot be found.
error CS2001: Source file '**/*.cs' could not be found.
```

— the project directory has a stray folder whose **name contains a backslash**, typically `bin\Debug` (one literal path component, not `bin/Debug`), left behind by an external tool that wrote a Windows-style path on macOS/Linux. MSBuild treats `\` as a path separator, so this entry corrupts the recursive `**` enumeration and `FileMatcher` falls back to returning the glob unexpanded. A plain `rm -rf bin obj` does **not** remove it (that only matches the folder named `bin`), and the whole-solution build masks it — which is why CI (`dotnet build Transcendence.sln`) stays green while `dotnet build Transcendence.Service/Transcendence.Service.csproj` (and `dotnet ef … --no-build` against it) break. Remove it explicitly:

```bash
rm -rf 'Transcendence.Service/bin\Debug'   # quote the backslash; it is part of the name
# nuke-from-orbit equivalent: git clean -dfx -- Transcendence.Service
```

### Run services

```bash
dotnet run --project Transcendence.WebAPI
dotnet run --project Transcendence.Service
```

Admin web UI runs in `apps/web` under `/admin` and requires an authenticated user with `admin` role.
Admin diagnostics include:
- `/admin` for worker/server status, database + analysis metrics, and top backlog groups
- `/admin/jobs` for queue-state exploration, recurring-producer pause/resume, and backlog clearing
- `/admin/jobs/[jobId]` for per-job detail (state history, inferred region, delete/retry)
- `/admin/logs` for service/webapi operational logs
- `/admin/audit` for the admin audit log
- `/admin/api-keys` for AppOnly API key management (list/rotate/revoke)
- `/admin/pro-summoners` for pro/tracked-roster curation

The pro-summoner CSV importer accepts required `gameName`, `tagLine`, and `platformRegion` columns plus optional `puuid`, `proName`, `teamName`, and `type`. A sourced starter import is available at `docs/seeds/probuild-pro-roster-2026-06-07.csv`.

`/api/auth/logout` revokes the active refresh token server-side and the web logout flow calls it before clearing cookies.
WebAPI now defaults `Microsoft.EntityFrameworkCore.Database.Command` to `Warning` so operational logs are not dominated by insert/update chatter.

### Operational Log Files

`Transcendence.WebAPI` and `Transcendence.Service` both write structured operational log lines to files.

- Config section: `OperationalLogs`
- Keys:
  - `OperationalLogs:ServiceName` (`webapi` or `service`)
  - `OperationalLogs:DirectoryPath` (default `logs`)
  - `OperationalLogs:MinLevel` (default `Information`)
- Admin-reader overrides in `Transcendence.WebAPI`:
  - `AdminLogs:Sources:webapi:DirectoryPath` (optional explicit path for `webapi.log`)
  - `AdminLogs:Sources:service:DirectoryPath` (optional explicit path for `service.log`)

In Docker Compose (`compose.yml`), both services mount a shared `operational_logs` volume at `/var/log/transcendence` so admin APIs can read both log streams. The one-shot `operational-logs-init` service runs as root before either application service and assigns the named volume to the non-root `APP_UID` carried by the WebAPI image. This makes a brand-new or restored volume writable without a host-side `chown`; `webapi` and `service` will not start if initialization fails.
The admin logs API scans the live file plus rotated `*.log.N` archives and reports whether the selected source is currently available. In non-compose or split-host setups, configure the `AdminLogs:Sources:*:DirectoryPath` overrides in the Web API so `/api/admin/logs/services` can find worker logs outside the Web API's own content root.
The logger provider now pre-creates the target `*.log` file and writes a one-time stderr warning if the process cannot create or append the file. In container deployments, that warning appears in the container's stdout/stderr stream and is the first place to check when `service.log` is missing.

Compose env contract:
- [`compose.yml`](../compose.yml) injects the Riot key with `RiotApi__League__ApiKey`.
- The repo-root [`.env.example`](../.env.example) uses the matching variable:
  - `RIOT_API_KEY_LOL`

## Web Commands

From repo root:

```bash
pnpm backend:test
pnpm hooks:install
pnpm precommit:check
pnpm web:dev
pnpm web:test
pnpm web:lint
pnpm web:build
```

## Dependency Security (npm)

Most npm advisories here land on dev/build tooling, but **not all of them** — `next` and `sharp`
ship in the `transcendence-web` runtime, so never assume an alert is dev-only without reading the
dependency path.

Three things keep this honest, in the order you should reach for them:

1. `.github/dependabot.yml` — weekly version-update PRs across npm, NuGet, pip, Actions and Docker.
2. The `audit` job in `.github/workflows/ci-web-backend.yml` — `pnpm audit --audit-level=high`.
3. `pnpm.overrides` in the root `package.json` — the **last** resort, for chains that are frozen
   upstream.

### Use both scanners — they disagree

**GitHub Dependabot alerts and `pnpm audit` do not report the same set.** In a Sept 2026 sweep,
Dependabot listed 31 open alerts, all of them dev tooling. `pnpm audit` on the same tree found
advisories Dependabot had not surfaced at all — including two **critical** ones in `next` itself
and a high in `sharp`, both of which ship to production. Check both when triaging:

```bash
gh api repos/:owner/:repo/dependabot/alerts --paginate --jq '.[] | select(.state=="open")'
pnpm audit --audit-level=low
```

Treat `pnpm audit` as the authority on what is actually installed; it reads the real lockfile
resolution rather than GitHub's periodic scan. That is why CI gates on it and not on the alert list.

### Prefer a dependency bump over an override

Overrides are a fallback, not the default fix. In the Sept 2026 sweep the block had grown to 26
entries; **22 of them were dead weight** — the direct dependency's semver range already reached a
patched version and the pin was doing nothing. Only four chains were genuinely frozen.

To find out which entries are actually load-bearing, resolve from a clean slate with the block
emptied and see what the advisory scan still reports:

```bash
cp package.json /tmp/pkg.bak && cp pnpm-lock.yaml /tmp/lock.bak
# empty the pnpm.overrides block, then:
rm pnpm-lock.yaml && pnpm install && pnpm audit --audit-level=low
```

Deleting the lockfile matters — pnpm keeps an existing resolution when it still satisfies a range,
so re-resolving in place will make redundant pins look necessary. Restore the backups afterwards.

Reach for an override only when that test shows the range genuinely cannot get there. After
`@lhci/cli` was dropped in favour of driving Lighthouse directly, that is `esbuild` alone. Otherwise
raise the direct dependency's floor — especially when the vulnerable package is version-locked to a
parent we own. `@vitest/mocker` moves in lockstep with `@vitest/runner` / `expect` / `spy`, so
overriding it alone desyncs the set; `apps/web` pins `vitest` instead. Same for `next` and `sharp`.

### Writing an override

Use pnpm's **range-keyed selector** form, one entry per affected major line:

```jsonc
"tmp@<0.2.7": "0.2.7",
```

This only rewrites versions inside the vulnerable range and leaves other majors alone — prefer it
over the blunt `"pkg": "x.y.z"` form, which pins every copy in the tree.

**The gotcha:** a range-keyed override pins a floor, and when a *newer* advisory lands on the same
package the pin silently goes stale. It is still satisfied, so nothing fails, but it now sits below
the patched version. Five of the original eighteen entries were stale, and one freshly-added pin
(`tmp@0.2.6`) was outrun by a new advisory within the same working session. Nothing but the CI
`audit` job catches this — Dependabot cannot, because the fix is not a dependency range it can bump.

### When an advisory looks unfixable

It usually is not. Before concluding that nothing can be done, check whether the *top* of the
chain is the stale link — an actively maintained tool has often already fixed the problem in a
release that something above it is pinning you away from.

**Worked example, now resolved.** `extract-zip` reached this repo four levels beneath `@lhci/cli`:

```
@lhci/cli -> lighthouse -> puppeteer-core -> @puppeteer/browsers -> extract-zip
```

`extract-zip@2.0.1` is the last version its maintainers ever published (June 2020) and both
advisories against it have no patched release, so the package itself was a dead end. But the
ecosystem had already fixed it by *deleting* the dependency: `@puppeteer/browsers@3.x` dropped
`extract-zip`, `puppeteer-core@25.x` uses that, and `lighthouse@13.x` requires
`puppeteer-core@^25.3.0`. Lighthouse ships continuously; the only stale link was `@lhci/cli@0.15.1`
(June 2025, last commit to its default branch June 2025), which pinned `lighthouse` to exactly
`12.6.1`.

The interim fix was a single override, `"lighthouse@<13": "13.4.1"`. **That override no longer
exists** — `@lhci/cli` was subsequently dropped entirely in favour of driving Lighthouse directly
(see *Performance Gates*), which removed the dormant dependency rather than routing around it and
took `qs`, `tmp` and `uuid` with it. The overrides block is down to `esbuild`.

Two things that did **not** work, recorded so nobody retries them:

- **Overriding `@puppeteer/browsers` to 3.x under `puppeteer-core@24`.** 3.x is ESM-only
  (`"type": "module"`, no CJS build) and replaced `extract-zip` with `modern-tar`, while
  `puppeteer-core@24`'s CJS build `require()`s it. That ESM switch is what the major was for.
- **Suppressing it.** `pnpm.auditConfig.ignoreGhsas` exists and works, but a suppression is not a
  fix — it just stops you finding out. **This repo has no ignore list and should not acquire one.**

### Verifying a dependency bump

A bad pin usually surfaces as a runtime failure in a build tool, not as an install error, so
exercise the toolchain rather than trusting a clean install:

```bash
pnpm install --frozen-lockfile     # what CI runs
pnpm audit --audit-level=high      # what the CI audit job runs; must stay at zero
pnpm web:test && pnpm web:lint && pnpm web:build
pnpm api:check
pnpm perf:web                      # the Lighthouse CI chain, end to end
```

`pnpm perf:web` is slow but it is the only step that exercises the Lighthouse toolchain for real.
An install or type-check will not catch a runner that cannot launch Chrome.

Where a bump crosses a major (`tmp` 0.0.33 -> 0.2.x, `uuid` 8 -> 11), find the actual call sites in
the consuming package before trusting it — both of those turned out to use a single API
(`tmp.fileSync` / `tmp.tmpNameSync`, `uuid.v4()`) that survived the major.

## Backend Tests

From repo root:

```bash
pnpm backend:test   # runs all three projects below in sequence
# or individually:
dotnet test tests/Transcendence.Service.Core.Tests   # domain/service unit tests (SQLite/in-memory)
dotnet test tests/Transcendence.WebAPI.Tests         # controller unit tests (EF InMemory)
dotnet test tests/Transcendence.IntegrationTests     # real Postgres — REQUIRES a running Docker daemon
```

**Integration tier (`tests/Transcendence.IntegrationTests`).** Runs against a real Postgres 18 container
managed by Testcontainers (matching prod's major), exercising actual Npgsql translation, the migration
chain, and the auth/authz middleware — things SQLite and the EF InMemory provider cannot. A running
**Docker daemon is required**; a single shared container starts once per test run. Coverage:

- the full migration chain applied from an empty database (`Database.MigrateAsync`, in the fixture);
- the authorization boundary end-to-end through `WebApplicationFactory<Program>` — public / AppOnly
  (X-API-Key) / UserOnly (JWT) / AdminOnly (JWT+admin) × no / wrong / correct credentials, with
  credentials minted through the app's own `IApiKeyService` / `IJwtService`;
- analytics raw-vs-precompute equivalence on real Postgres (the SQLite equivalence gate, re-run on
  Npgsql for real GROUP BY / NULL collation / tie-break ordering), plus Build Atlas full/incremental
  generation promotion and completed-snapshot reads;
- `List<int>` / `List<string>` ↔ Postgres `integer[]` / `text[]` array round-trips.

It runs in CI via the solution-wide `dotnet test Transcendence.sln` step (GitHub `ubuntu-latest` has Docker
preinstalled, so Testcontainers works with no extra configuration).

**Python modeler tier (`analytics/modeler/tests`).** The offline Build Lab pipeline is not part of the
.NET solution, so it has its own `modeler` CI job in `.github/workflows/ci-web-backend.yml`: it creates
a venv on the runner's interpreter, installs `-e '.[test]'`, and runs `pytest`. Locally that is the
same two commands from `analytics/modeler` (see "Build Lab modeling and promotion").

Current `web:test` scope:
- Utility/unit tests in `apps/web/lib/*.test.ts`
- Component, route-handler, and telemetry tests in `apps/web/**/*.test.ts(x)`
- Runs in Vitest's `jsdom` environment

Note:
- `apps/web` package scripts `dev`, `build`, `lint`, and `test` prebuild `@transcendence/api-client` automatically, so direct commands such as `pnpm --filter web build` and `pnpm --filter web test` work without a separate manual client build step.

## Performance Gates

Performance is split into a **gate** and a **trend**, and the two answer different questions.
Do not collapse them.

| | Runs where | Measures | Purpose |
|---|---|---|---|
| **Gate** | `performance` CI job | seeded, hermetic stack | a change in the number means a change in the code, so it is attributable to a commit |
| **Trend** | `transcendence-web-perf.timer` on prod | the live deployment | what users actually get; moves with data growth as well as code |

### The gate

One CI job (`performance`) stands up Postgres, Redis, migrations, `seed-leaderboard.sql` and the
WebAPI, then runs both budgets against that one stack:

- **API** — `k6` with thresholds in `scripts/perf/api-load.js`.
- **Frontend** — `scripts/perf/web-lab.mjs`, driving Lighthouse's programmatic API.

```bash
pnpm perf:api          # k6, needs a WebAPI on :8080
pnpm perf:web          # build + lab budgets, needs the app on :3000
pnpm perf:web:ci       # lab budgets only, against an already-running app
```

**The frontend gate lives in the `performance` job and not in `web` for a structural reason.**
The `web` job has no backend, so the only routes it can honestly measure are the ones that render
without one. That is why coverage sat at three routes for so long. Pointing Lighthouse at a
data-dense route with no backend measures an empty state, renders fast, and *passes* — strictly
worse than not measuring. Any route added to `scripts/perf/routes.ci.json` must be one the CI seed
renders *representatively* — which is a stricter bar than "the seed has rows for it".

`/lol/leaderboards` is the worked example, and it is **deliberately excluded**. The seed does
populate it, but with so few rows that the page reflows as it settles: CI measured CLS 0.932 and a
0.65 performance score, while the nightly sweep against production measured CLS 0.016 and 0.95 for
the same route on the same commit. A 57x gap is the environment, not the code. Gating on it would
fail every PR for a defect that does not exist. Re-add it once the seed carries a realistic
leaderboard page, and confirm the CI number lands near the production one before trusting it.

Budgets are in `scripts/perf/web-budgets.json`. Keys are ceilings on the median sample, except
`category:*` keys which are floors on the Lighthouse category score. `routes` entries are keyed by
**route template** (from `@transcendence/web-routes`), not URL path, and override `default`.

### The trend

`transcendence-web-perf.timer` runs the same runner nightly on the prod box against
`https://transcend.kronic.one`, and writes Prometheus exposition to a textfile that node-exporter
serves. **CI never writes to Prometheus** — no receiver, no pushgateway, no exposed port, no
credentials in CI. See `scripts/ops/README.md` for the runbook.

Metrics, all gauges, labelled `route` and `form_factor`:

```
transcendence_web_lab_lcp_milliseconds          transcendence_web_lab_ttfb_milliseconds
transcendence_web_lab_cls                       transcendence_web_lab_speed_index_milliseconds
transcendence_web_lab_tbt_milliseconds          transcendence_web_lab_interactive_milliseconds
transcendence_web_lab_fcp_milliseconds          transcendence_web_lab_total_bytes
transcendence_web_lab_category_score{category}  transcendence_web_lab_last_success_unixtime_seconds
```

The `route` label comes from `webVitalsRouteTemplate()` in `@transcendence/web-routes` — the same
function the browser Web Vitals reporter uses. **That shared vocabulary is the point**: it is what
lets a lab series and a field series for the same route sit on one Grafana panel
(`Transcendence — Web Performance`). Never derive the label any other way.

`_last_success_unixtime_seconds` mirrors the worker's matchup convention and is load-bearing:
node-exporter serves a textfile indefinitely, so a dead sweep leaves every other panel looking
healthy. The `trn-web-lab-sweep-stale` alert is the only thing that catches it.

### Diagnosing a slow route

The gauges say *which* route is slow; only the full Lighthouse report says why. Pass
`--report-dir` to keep one report per route:

```bash
node scripts/perf/web-lab.mjs --base-url https://transcend.kronic.one \
  --routes scripts/perf/routes.prod.json --samples 1 --report-dir ./reports
```

Measure where the signal is. A developer laptop is fast enough to hide the problem — the same
route scored 0.98 locally and 0.61 on the prod box. To reproduce a production number, run the
sweep image on the box itself:

```bash
docker run --rm --shm-size=1g -v /var/lib/transcendence-perf/diag:/out \
  ghcr.io/luisgon-dev/transcendence-perf:main \
  --base-url https://transcend.kronic.one --routes scripts/perf/routes.prod.json \
  --samples 1 --report-dir /out
```

The audits worth reading first are `mainthread-work-breakdown` (where the time went),
`bootup-time` (which script), `long-tasks` (what blocked), and `unused-javascript`. A small
bundle with a large evaluation time means the cost is *work*, not download size, and no amount of
code splitting will help.

### Lab scores are not portable between machines

Lighthouse calibrates its simulated throttling against the CPU it runs on, so **absolute scores
only mean something relative to other runs on the same host**. Measured on one commit, same URL,
same image: `/lol/tierlist` scored **0.98 from a developer laptop and 0.61 from the prod box**, and
the box was idle at the time (load 7 on 46 cores, and removing the unit's `Nice`/`CPUWeight`
restrictions changed nothing). Server cores are simply much slower per-core than laptop cores.

Consequences, all of which the alerting and dashboards already assume:

- Never compare a CI gate number with a nightly sweep number. They run on different hardware.
- Budgets in `web-budgets.json` are calibrated for the CI runner and mean nothing on the box.
- The absolute alert (`trn-web-lab-performance-low`) is a deliberately low floor at 0.4 — it exists
  to catch a route falling off a cliff, not to express a quality bar.
- **Trend is the real instrument.** `trn-web-lab-lcp-regression` compares a route against its own
  7-day baseline on the same host, which is unaffected by the calibration offset.

### Adding routes to the nightly sweep

`scripts/perf/routes.prod.json` currently holds static routes only. Dynamic routes
(`/lol/champions/[championId]` and friends) are **deliberately excluded** until fixtures are chosen
with knowledge of the data. Under partial prerendering a missing entity still returns HTTP 200 with
a near-identical shell, and the identifier is echoed into the page regardless — so neither status
code nor page content reliably distinguishes a real record from a missing one from outside the app.
Pick IDs by querying the database for rows that actually have analytics, not by probing URLs.

## OpenAPI + TypeScript Client

Source of truth: `openapi/transcendence.v1.json` (committed). The generated client schema is rebuilt from that spec and committed with API changes.

```bash
pnpm api:gen
pnpm api:check
```

The exporter starts the WebAPI with the internal `OpenApi:ExportOnly=true` flag, which skips database-backed admin bootstrap work. It also clears the previous contract before downloading Swagger so a startup failure cannot pass by reusing stale output.

If hooks are installed (`pnpm hooks:install`), pre-commit runs path-aware checks automatically before each commit:
- `pnpm precommit:api-sync` runs only when staged files touch API-relevant paths (`Transcendence.WebAPI/`, `Transcendence.Service.Core/`, `Transcendence.Data/`, `scripts/openapi/export.sh`, committed OpenAPI spec), regenerates the client locally, and stages the refreshed spec.
- `pnpm precommit:check` runs `git diff --cached --check` to catch staged whitespace issues.

## Background Job Tuning

Key worker settings live under `Jobs:*` in `Transcendence.Service/appsettings.json`.
The public Web API also consumes `Jobs:MultiRegionIngestion` from `Transcendence.WebAPI/appsettings.json` so `/api/analytics/regions` and region-filter normalization stay aligned with the ingestion regions exposed to the frontend.

Host-level concurrency is configurable without rebuilding:

- `Worker:ThreadPoolMinThreads` (default `200`, never lower than the logical CPU count)
- `Jobs:Hangfire:Workers:Main` (default `24`)
- `Jobs:Hangfire:Workers:Analytics` (default `4`)
- `Jobs:Hangfire:Workers:Timeline` (default `8`)
- `Jobs:Hangfire:Workers:Discovery` (default `8`)
- `Jobs:Hangfire:Workers:History` (default `2`)

Tune worker pools against observed database/Riot capacity rather than host CPU alone. Production
Compose separately caps WebAPI connections at 20 and worker connections at 35, leaving 45 connections
of a 100-connection PostgreSQL server for migrations, exporters, and operations. The same Compose file
sets CPU/memory/PID guardrails for web (2 CPU/1 GiB), API (4 CPU/2 GiB), and worker (8 CPU/6 GiB).

Production defaults in `Transcendence.Service/appsettings.json` are coverage-first for LoL:
- `stable` keeps adaptive refresh (self-paced, ramp-aware), champion analytics ingestion, summoner maintenance, high-elo profile refresh, pro-roster discovery, and low-frequency timeline backfill enabled.
- `high-elo-profile-refresh` refreshes Master+ accounts and admits only evidence-backed one-tricks (30 or more games on one champion in the latest 50 stored Ranked Solo/Duo games).
- `pro-roster-discovery` stages Leaguepedia player-directory rows for admin review; source outages are non-fatal and retain the last good candidate set.
- `Jobs:Schedule:EnableProRosterDiscovery` defaults to `true`; `Jobs:Schedule:ProRosterDiscoveryCron` defaults to `15 3 * * *`.
- `Jobs:ProRosterDiscovery:Endpoint`, `PageSize`, `MaxPages`, and `PageDelaySeconds` control the external directory read. Pages are paced at 65 seconds by default to respect the source's anonymous Cargo rate limit, and successfully read pages are retained if a later page is throttled. Candidates must still be approved under `/admin/pro-summoners` before they enter public analytics.
- `match-timeline-backfill` is intentionally slower than ingestion because tier lists and core champion stats do not require timeline rows.
- `Jobs:Schedule:PurgeBacklogOnPatchRolloverOnStartup` is disabled and startup rollover logic preserves queued current-patch catch-up work.

### Recurring Job Scheduling (Development and Production)

`Transcendence.Service` hosts one of two background workers depending on environment (`Program.cs`): `DevelopmentWorker` when `ASPNETCORE_ENVIRONMENT=Development`, otherwise `ProductionWorker`. Both register the **same** recurring-job set through the shared `WorkerRecurringJobPolicy` — the two workers differ only in startup behavior, not in which recurring jobs they schedule.

Which recurring jobs are active is determined by:

- the per-job `Enable*` flags under `Jobs:Schedule` (for example `EnableChampionAnalyticsIngestion`, `EnableMatchTimelineBackfill`), and
- the resolved **scheduling profile** (`Jobs:Schedule:Profile`, falling back to `DefaultProfile`, default `stable`), whose `Jobs:SchedulingProfiles:Profiles:<name>:JobOverrides` can flip a job's `Enabled`/`Cron`/`MandatoryBaseline`. Profile overrides win over the descriptor defaults. `poll-live-games` is enabled in `stable`, bounded by `Jobs:LiveGamePolling`, and defaults to opted-in favorite summoners only.

The base `appsettings.json` ships `Jobs:Schedule:Profile = "stable"` (there is no `appsettings.Development.json`), so a local worker resolves the **same `stable` profile as production** unless you override `Jobs:Schedule:Profile` (or individual `Enable*` / `JobOverrides` values) via user-secrets or environment variables. Under `stable` the enabled jobs are the LoL analytics-coverage set (adaptive analytics refresh, champion-analytics ingestion, summoner maintenance, each a single self-pacing job that tightens cadence during the new-patch ramp window, plus match-timeline backfill, live-game polling for opted-in favorites, high-elo profile refresh, and pro-roster discovery), plus the baseline jobs (`detect-patch`, `retry-failed-matches`, `refresh-lock-lifecycle-cleanup`); `rune-selection-integrity-backfill` and the daily `refresh-champion-analytics` are disabled.

The worker watchdog requests graceful generic-host shutdown on a stale producer heartbeat, waiting
`Worker:Watchdog:GracefulShutdownTimeout` (default `00:00:15`) before its hard-exit/container-restart
fallback. Override it with `Worker__Watchdog__GracefulShutdownTimeout`; keep it long enough for an
ordinary Hangfire scope and transaction to observe cancellation and unwind.

`DevelopmentWorker`'s only environment-specific startup actions are: removing legacy/invalid recurring jobs (old `cache-warmup*` ids), an optional full Hangfire purge when `Jobs:Schedule:CleanupOnStartup=true` (default `false`), and a startup integrity check that fail-fasts on mandatory-baseline job failures. It does **not** run the production startup bootstrap described below.

### Production Startup Bootstrap

When `Transcendence.Service` runs in non-development environments, the `ProductionWorker` queues bounded startup bootstrap work:

- `Jobs:Schedule:RunPatchDetectionOnStartup=true` runs patch detection immediately on startup.
- `Jobs:Schedule:PurgeBacklogOnPatchRolloverOnStartup=false` keeps current-patch catch-up work intact across restarts.
- After startup patch detection confirms a rollover, the worker refreshes static data and queues a bounded analytics ingestion bootstrap without performing a blanket Hangfire purge.
- When `refresh-build-resource-analytics` is enabled, every production startup also enqueues an
  `onlyIfMissing` Build Atlas bootstrap. It exits immediately when the active patch already has a
  Ready generation and repairs a missing snapshot even when the deploy did not coincide with patch
  rollover.

### Build Atlas Refresh

Build Atlas (`/lol/items/*` and `/lol/runes/*`) is served only from completed snapshot generations;
the HTTP request path never falls back to scanning raw match resources. Its recurring job is
`refresh-build-resource-analytics`, controlled by:

- `Jobs:Schedule:RefreshBuildResourceAnalyticsCron` (default `40 * * * *`)
- `Jobs:Schedule:EnableRefreshBuildResourceAnalytics` (default `true`)
- `Analytics:BuildAtlas:MatchBatchSize` (default `500`)
- `Analytics:BuildAtlas:CommandTimeoutSeconds` (default `120`, clamped to 30–600)

The job runs independently on `analytics-warm`. A first/forced run rebuilds the retained active-patch
ranked-Solo/Duo corpus in bounded match batches. Incremental runs clone the active resource and exact
population atoms, add only matches not recorded by a completed generation, then atomically promote
the result. The active generation is unchanged when there are no new eligible matches. Static-data
detection also enqueues an `onlyIfMissing` bootstrap so new patches begin warming without waiting for
the hourly schedule. A PostgreSQL session advisory lock prevents startup, recurring, and manual
triggers from running generations concurrently; a losing trigger exits immediately, and PostgreSQL
releases the lock automatically if the owning worker connection dies. Hangfire PostgreSQL sliding
invisibility renewal is enabled globally so long-running jobs retain queue ownership past the
provider's default 30-minute invisibility window. Failed generations retain their manifest and
failure reason for diagnosis, but their resource/population payload and processed-match ledger are
deleted immediately (and swept again after each successful promotion). Cleanup is best-effort after
promotion so a storage-hygiene failure can never demote a successfully published generation.

### Build Lab modeling and promotion

Build Lab is shadow-only by default. `Analytics:BuildLab:Enabled`,
`Jobs:Schedule:EnableCreateBuildLabGeneration`, and
`Jobs:Schedule:EnablePromoteBuildLabGeneration` all default to `false`. Note that Emerald+ coverage at
timeline schema v2 cannot accrue *before* enablement — the flag is what makes ingestion capture and
stamp v2 at all — so the flip is the start of the backfill, not the reward for it. Enable it only with
object storage configured and disk headroom confirmed, and read "Enabling Build Lab on an existing
corpus" below first: the flip costs a multi-day, rate-gated re-ingestion of the retained corpus.

The offline modeler is built and published like every other app service
(`ghcr.io/luisgon-dev/transcendence-analytics-modeler`, path-filtered on `analytics/modeler/**`) and
runs as a **run-to-completion oneshot** behind an optional Compose profile, so it never starts with
the default stack and is never left running between generations:

```bash
# one generation, then exit (what the systemd timer invokes on prod)
docker compose --profile analytics-modeling run --rm analytics-modeler
docker compose --profile analytics-modeling run --rm --build analytics-modeler   # local iteration
```

On prod the schedule is `scripts/ops/transcendence-modeler.timer`, not the deploy poller: a run lasts
hours, and a poller that recreated the container mid-run destroyed the generation every time it
deployed. Exit code `0` means a generation completed or there was nothing pending; non-zero means the
generation failed.

Environment variables Compose actually supplies, and the code that reads each one:

| Variable | Consumed by |
| --- | --- |
| `BUILD_LAB_ENABLED` | `Analytics__BuildLab__Enabled` on the **worker** (generation/promotion, timeline extras + the effective timeline schema version) *and* the **WebAPI** (serving — without it the API answers "not enabled" even after a promotion), plus both `Jobs__Schedule__Enable{Create,Promote}BuildLabGeneration` keys — one switch, not four |
| `BUILD_LAB_CODE_REVISION` | worker: `Analytics__BuildLab__CodeRevision` (generation provenance) |
| `BUILD_LAB_DATABASE_URL` | modeler; Compose supplies the PostgreSQL service URL |
| `BUILD_LAB_DEIDENTIFICATION_SALT` | modeler — **secret**, no default, must be ≥ 32 chars or the container refuses to start |
| `BUILD_LAB_ARTIFACT_DIR` | modeler (default `/artifacts`) |
| `BUILD_LAB_POLL_SECONDS` | modeler (default `300`, floor `30`) |
| `BUILD_LAB_RUN_ONCE` | modeler; `true` processes one pending generation and exits |
| `BUILD_LAB_MAX_TRAINING_ROWS` | modeler; chronological sample ceiling for the design matrix (default `250000`, floor `20000`) |
| `BUILD_LAB_LOG_LEVEL` | modeler `LOG_LEVEL` (default `INFO`) |
| `BUILD_LAB_S3_*` (`ENDPOINT`/`BUCKET`/`ACCESS_KEY`/`SECRET_KEY`) | modeler artifact upload |
| `BUILD_LAB_MODELER_CPUS`, `BUILD_LAB_MODELER_MEMORY_LIMIT` | Compose container guardrails |
| `MODELER_IMAGE` | Compose image pin for rollback (`:sha-<short>`) |
| `TRN_FEATURE_BUILD_LAB`, `TRN_FEATURE_CHAMPION_RECOMMENDATIONS`, `TRN_FEATURE_BUILD_REFERENCE_LINKS` | web; independently expose each consumer surface after promotion |

`BUILD_LAB_LEASE_OWNER` is read by the modeler but deliberately **not** set in Compose: it defaults to
`hostname:pid`, so two modeler instances can never claim the same lease identity. Set it only for a
one-off manual run you want to recognise in the generation rows.

Everything else under `Analytics:BuildLab` (the publication thresholds below, `DatasetVersion`,
`PriorPatchesToBorrow`, `RetainedGenerations`, `RetiredGenerationGraceMinutes`) is config-file only —
set it in `config/backend.shared.json`, not through an env var.

#### Adaptive patch borrowing

`PriorPatchesToBorrow` is a **ceiling**, not a schedule. Patches ship fortnightly, so a fixed per-patch
weight is the wrong instrument: it discards good data from the champions a patch never touched and
keeps data from the ones it rebalanced. Recency sets the ceiling; each borrowed row then keeps only the
fraction of it that its cell still deserves:

- **Static change → 0.** A rebalance to the champion, the item, or a rune in the action hard-excludes
  the borrowed row. Detected by diffing per-patch static data: `ItemVersions` and `RuneVersions` from
  Community Dragon, and `ChampionVersions.BalanceHash` — a hash of a *numeric-only* Data Dragon
  projection (base stats plus each spell's cooldown/cost/range/effect). Measured against live data that
  projection flags 0 of 173 champions across a cosmetic-only patch, where a whole-record diff flags 10
  on `skins` alone, and 11 of 173 across a real balance patch.
- **Otherwise, a commensurability discount.** The current-vs-prior disagreement for that exact cell is
  scored as a z-score and decayed, so an agreeing cell borrows at nearly full strength, a thin cell is
  not thrown away for noise, and a cell that drifted for reasons static data cannot see (indirect
  interactions, system changes) decays to zero on its own.

Both halves matter because they are complementary *in time*: the drift test needs current-patch data to
have power and is weakest in the first days of a patch, which is exactly when static detection is
instant. Static detection in turn cannot see indirect or systemic changes, which the drift test can.

Known and accepted over-flag: Riot is migrating spells off `effect` onto dataValues, so an effect array
can drop to zeros with no balance change (Warwick did this in 16.15). That costs one champion's
borrowing for a patch. Excluding `effect` from the hash would instead miss four real changes in the same
patch and borrow across them, so the projection keeps it — a false positive costs coverage, a false
negative biases an estimate.

Champion `Roles` from the same table feed archetype pooling: an item's effect on a burst mage says more
about the same item on another burst mage than the role average does, so a sparse champion shrinks
toward champions that play like it. A champion with no published roles pools at the role level exactly
as before.

`BUILD_LAB_DEIDENTIFICATION_SALT` is a secret and ships as an **empty** placeholder in `.env.example`;
generate a real one (`openssl rand -hex 32`) into the deployed `.env` before enabling Build Lab and
never commit it. It keys the HMAC surrogate match/participant ids in the Parquet export, so a guessable
salt makes the export re-identifiable, and rotating it re-pseudonymizes every future export (old
exports keep their old surrogates and cannot be joined to new ones). Compose passes it with `:-` rather
than the required-variable `:?` form on purpose: Compose interpolates the whole file *before* it filters
by profile, so `:?` would break `docker compose up` for the default stack on any host without a salt.
The modeler enforces the requirement itself and exits with a clear message.

The S3 settings target any S3-compatible object store. If they are absent, artifacts stay in the
`build_lab_artifacts` volume and the manifest URI is `file://...`. Training exports contain
generation-scoped surrogate match/participant identifiers and never contain Riot IDs or PUUIDs.

The modeler holds a **PostgreSQL session advisory lock** (`build-lab-generation-modeling`) for the
whole run, and both Build Lab jobs run the reaper first. The reaper decides liveness by trying to take
that same lock: if it succeeds, no modeler session exists, so every `Modeling` row is failed with the
owner named in `FailureReason`. A dead modeler is therefore reclaimed on the next
`promote-build-lab-generation` tick — within ~10 minutes at the default cron — with nothing to
configure.

There is deliberately no heartbeat, expiry column, or timeout. The previous design renewed a deadline
from a background thread and reaped **six consecutive healthy generations**, because loading the frozen
dataset assembles millions of rows through a raw DBAPI cursor and holds the GIL for minutes, so the
renewal thread could not be scheduled. Liveness now belongs to the TCP session, which cannot be starved.
This is also the pattern `RefreshBuildResourceAnalyticsJob` and `MatchTimelineIngestionJob` already use.

Reaping matters because the coordinator refuses to create a second in-flight generation for a patch, so
a wedged row blocks the pipeline. `POST /api/admin/analytics/build-lab/generations/{id}/fail` is the
manual equivalent. A `PendingDataset` row is **not** reaped — nothing holds it — so a modeler that never
starts leaves it queued forever, silently: watch the `trn-buildlab-unclaimed-generation` alert
(`config/monitoring/`), not the wedge alert, for that failure.

Run the modeler tests from `analytics/modeler` after installing its test extra (CI runs the same two
commands in the `modeler` job):

```bash
python -m pip install -e '.[test]'
python -m pytest
```

Publication defaults under `Analytics:BuildLab` are 1,000 observed actions, effective sample size
500, confidence width at most 0.03, overlap at least 0.90, weighted balance at most 0.10, overall ECE
at most 0.015, and time-band ECE at most 0.025. These are configuration for stricter operation and
observability; `build-lab-v1` rejects any lowering. Introduce a new `DatasetVersion` and complete a
fresh shadow validation to change methodology.

Promotion is the gate that makes those numbers meaningful. `PromoteCandidateAsync` refuses a
candidate whose structural win model fails overall/time-band ECE, fails to beat the descriptive
baseline on Brier score and log loss, or fails the held-out-patch or leakage check; the generation is
marked `Failed` and can never become active. So no served Adjusted WPA figure originates from a
generation whose win model missed calibration.

`ArtifactSha256` is the SHA-256 of `ArtifactManifestJson`, so the promoter's check proves the stored
manifest is internally consistent with the checksum the modeler recorded in the same transaction. It
is **not** a content hash of the Parquet/joblib bundle at `ArtifactUri` and does not detect a
corrupted or swapped artifact in object storage — verify the bundle out-of-band before trusting a
re-hydrated artifact.

#### Deploying timeline schema v2

`MatchTimelineIngestionJob` carries two versions: `BaselineTimelineSchemaVersion = 1` (ordered item
purchases + skill orders, `FrameIntervalMinutes` frames — what every ingest captures) and
`CurrentTimelineSchemaVersion = 2` (the Build Lab payload). `TargetSchemaVersion(buildLabEnabled)`
picks between them, and the job only re-ingests a `Success` timeline whose `SchemaVersion` is **below
the target**. So **deploying this change with `BUILD_LAB_ENABLED=false` re-fetches nothing**: the
target stays v1, the corpus is already at v1, and no Riot budget is spent. The multi-day sweep is
bought by the flag flip, not by the deploy — see "Enabling Build Lab on an existing corpus".

- **Migration lock window.** `AddBuildLabAnalytics` adds three `integer NOT NULL DEFAULT 0` columns
  to `MatchParticipantTimelineSnapshots` (~22.5M rows) and creates the new Build Lab / event tables.
  A constant default is a metadata-only `ADD COLUMN` on PostgreSQL 11+, so there is no table
  rewrite; the hot-table lint passes it for exactly that reason. It still takes a brief
  `ACCESS EXCLUSIVE` lock, which queues behind any long-running reader — do not deploy during an
  analytics sweep or an `archive-old-patches.sh` run.
- **Storage sizing.** With `Analytics:BuildLab:Enabled=false` the frame cadence stays at
  `Jobs:TimelineIngestion:FrameIntervalMinutes` (default 2) and the three modeling-only tables stay
  empty. **Turning Build Lab on drops the cadence to one minute, which roughly doubles
  `MatchParticipantTimelineSnapshots`** (~22.5M → ~45M rows for the retained corpus) and starts writing
  `MatchTimelineEventPayloads` — one `jsonb` row per *persisted* event (the item lifecycle plus
  `CHAMPION_KILL`/`BUILDING_KILL`/`ELITE_MONSTER_KILL`, with null union members dropped; every other
  Match-V5 event type is discarded at ingestion) — plus `MatchParticipantItemEvents` and
  `MatchParticipantRankContexts`. Confirm free space against the retention policy (`KEEP_PATCHES`, see
  `scripts/ops/README.md`) *before* flipping the flag.
- **The flag gates the payload *and* the stamp, together.** The one-minute cadence, the item lifecycle
  events, the raw event payloads, the rank contexts, **and** the version the ingest stamps are all
  derived from `Analytics:BuildLab:Enabled` on the same run. So a v2 row is proof the extras are
  present, a v1 row is proof they are not, and the generation cohort filter (which requires
  `SchemaVersion >= 2` *and* an Emerald+ rank context per participant) can trust the stamp. A flag-off
  deployment can never manufacture a v2 row that silently lacks the payload.

#### Enabling Build Lab on an existing corpus

Enabling is a **config flip**, and the cost is a one-time re-ingestion of every retained timeline
against a low-rate Riot key. Do it in this order:

1. Confirm free disk against the doubled snapshot table plus the three modeling tables *before*
   flipping (see **Storage sizing** above), and confirm `KEEP_PATCHES` retention is actually running.
   Retention decides how many patches get re-fetched.
2. Set `BUILD_LAB_DEIDENTIFICATION_SALT` in the deployed `.env` (`openssl rand -hex 32`).
3. Flip `BUILD_LAB_ENABLED=true` and recreate the worker and WebAPI. The ingestion target rises to v2,
   so **every `Success` timeline in the retained corpus becomes stale and is re-fetched once**, at the
   job's normal rate-gated pace: a multi-day background sweep competing with new-match ingestion for the
   same Riot budget. Nothing else is required to start it — there is no `const` to bump.
4. Install and enable the modeler timer (`cp scripts/ops/transcendence-modeler.{service,timer}
   /etc/systemd/system/ && systemctl enable --now transcendence-modeler.timer`). Do not defer this: the
   same flag enables the create job, and once the first matches reach v2 a `PendingDataset` generation
   appears that only the modeler can claim — `trn-buildlab-unclaimed-generation` pages six hours later
   if nothing is running. Early generations will fail their evidence gates while coverage is thin,
   which is the intended behaviour.
5. Leave `TRN_FEATURE_BUILD_LAB` (and the two sibling web flags) `false` until a generation has actually
   promoted. Backend enablement and public exposure are separate switches on purpose.

**Turning it back off** stops the payload writes and drops the target to v1 immediately; existing v2
rows are left alone (they are `>= 1`, so never re-fetched) and stay usable if the flag is flipped on
again. Only a row that fails and retries while the flag is off is rewritten down to v1.

### Precomputed Champion Analytics

Champion analytics surfaces have independent recurring-job ownership on the four-worker
`analytics-warm` pool:

- `refresh-precomputed-analytics` publishes only the tabular core
  (`Jobs:Schedule:RefreshPrecomputedAnalyticsCron`, default `30 * * * *`).
- `refresh-champion-matchups` advances the incremental/resumable matchup generation
  (`Jobs:Schedule:RefreshChampionMatchupsCron`, default `35 * * * *`).
- `refresh-champion-build-snapshots` atomically replaces serialized champion build responses
  (`Jobs:Schedule:RefreshChampionBuildSnapshotsCron`, default `10 */6 * * *`).
- `refresh-pro-analytics` and `refresh-build-resource-analytics` retain their independent schedules.

Each job has its own concurrency boundary. Deterministic full-corpus jobs disable automatic retries,
so a slow build sweep cannot amplify database load or delay matchup ownership.

Matchups no longer rebuild through a corpus-wide participant/timeline self-join. New eligible matches
are materialized into narrow durable lane-pair facts, current ranks are frozen per immutable
generation, and champion batches commit independently. An interrupted generation resumes from its
persisted rank/champion progress; a timed-out multi-champion batch recursively splits. Only a complete
Ready generation is visible to reads.

- `Analytics:Precompute:MatchupSourceMatchBatchSize` (default `250`, clamped to 10–2,000)
- `Analytics:Precompute:MatchupChampionBatchSize` (default `8`, clamped to 1–100)
- `Analytics:Precompute:CommandTimeoutSeconds` (default `45`, clamped to 15–600)
- `Analytics:Precompute:MaxGenerationResumeAttempts` (default `3`, clamped to 1–20)
- `Analytics:Precompute:RetainedMatchupGenerations` (default `2`, clamped to 1–10)

Before the first production run, apply the online source-table preparation outside EF's migration
transaction:

```bash
psql "$DATABASE_URL" -f scripts/ops/install-matchup-performance-db.sql
```

The script creates the eligible-match and minute-15 covering indexes with
`CREATE INDEX CONCURRENTLY`, persists table-specific autovacuum/analyze thresholds for the
append-heavy source tables, and refreshes planner statistics. It is idempotent and intentionally
separate from the EF migration so ingestion writes are never blocked by a normal index build.

### Champion Analytics Ingestion

`Jobs:ChampionAnalyticsIngestion` now supports:

- `MinimumSuccessfulMatchesForCurrentPatch`
- `TargetSuccessfulMatchesForCurrentPatch`
- `DataStaleAfterMinutes`
- `MaxCandidateSummonersPerRun`
- `MinRefreshJobsToQueuePerRun`
- `MaxRefreshJobsToQueuePerRun`
- `RefreshLockMinutes`
- `PrioritizeFavoriteSummoners`
- `PrioritizeTrackedHighValueSummoners`
- `PrioritizeRankedHighEloSummoners`
- `FallbackToTrackedSummoners`
- `PauseWhenApiPriorityRefreshActive`
- `NewPatchRampHours`
- `RampDataStaleAfterMinutes`
- `RampMaxCandidateSummonersPerRun`
- `RampMinRefreshJobsToQueuePerRun`
- `RampMaxRefreshJobsToQueuePerRun`
- `HighEloTiers`

This job now prefers tracked high-value roster entries and Emerald+ ranked candidates before it falls back to the broad stale summoner pool. Region coverage targets scale with `Jobs:MultiRegionIngestion:Regions[*]:Weight`.

### Adaptive Throughput Budget Policy

`Jobs:AdaptiveThroughputBudget` supports:

- `VelocityLookbackMinutes` (default `30`)
- `HighPressureCooldownMinutes` (default `8`)
- `CatchUpHoldMinutes` (default `12`)
- `ModeSwitchCooldownMinutes` (default `4`)
- `CatchUpCoverageThreshold` (default `0.85`)
- `CatchUpBacklogAgeMinutes` (default `45`)
- `CatchUpCandidatePressureThreshold` (default `1.1`)
- `MinimumRecentVelocityPerHour` (default `12.0`)
- `CatchUpQueueBurstMultiplier` (default `1.6`)
- `CatchUpCandidateBurstMultiplier` (default `1.8`)
- `HighPressureCandidateMultiplier` (default `0.25`)
- `MaxQueueTargetHardCap` (default `40`)
- `MaxCandidateHardCap` (default `500`)

These settings determine per-run producer mode (`HighPressure`, `Balanced`, `CatchUp`), queue target, and candidate ceiling for low-priority ingestion producers.

### Starvation Guardrail Policy

`Jobs:StarvationGuardrail` supports:

- `Enabled` (default **`false`**)
- `MaxEligibleDeferAgeMinutes` (default `360`)
- `CatchUpWindowMinutes` (default `12`)
- `CatchUpCooldownMinutes` (default `20`)
- `ForcedCatchUpQueueTargetFloor` (default `1`)
- `ForcedCatchUpCandidateBurstMultiplier` (default `1.5`)
- `ForcedCatchUpMaxCandidateHardCap` (default `500`)

**Disabled by default.** The defer-age signal (oldest eligible summoner's `UpdatedAt` age ≥ `MaxEligibleDeferAgeMinutes`) is structurally unsatisfiable on the yield-limited personal key — ~4.1M summoners, only a few thousand <6h fresh — so it fired forced catch-up perpetually (bypassing the API-priority pause and driving the all-modes ancient-history grind). The adaptive throughput budget's coverage/velocity `CatchUp` mode and the cold-start override remain the legitimate bursting mechanisms. Re-enable only with a delta/growth-based defer signal, not absolute age. See `docs/ARCHITECTURE.md` → "Low-priority ingestion is ranked-head-only; defer-age guardrail retired".

### Match Ingestion Windows

`Jobs:MatchIngestion` supports:

- `MatchIdsPageSize`
- `HighPriorityRankedPages`
- `HighPriorityAllModesHeadPages`
- `HighPriorityNonRankedBackfillMaxPages`
- `LowPriorityRankedPages`
- `LowPriorityAllModesHeadPages`
- `LowPriorityNonRankedBackfillMaxPages`

High-priority user refresh always executes ranked head sync first, then all-mode sync/backfill. Low-priority ingestion preserves ranked-first behavior and can be preempted by active API-priority refresh demand.

Match-detail preparation is intentionally sequential inside each refresh job because the match service builds EF entity graphs using the job-scoped `DbContext`.

Non-ranked backfill ordering is tracked per summoner with `SummonerIngestionCursors` to ensure monotonic progress across repeated low-priority runs.

### Full-History Profile Backfill

`Jobs:FullHistoryBackfill` supports:

- `Enabled` (default `true`)
- `PageSize` (default `100`, clamped to Riot's Match-V5 page limit)
- `MaxPagesPerRun` (default `5`; each Hangfire execution processes a bounded chunk and re-enqueues itself if more history remains)
- `MaxFailureRetriesPerRun` (default `25`; retries unresolved match-detail fetch failures before scanning the next page)
- `MinimumMatchStartEpochSeconds` (default `1623801600`, June 16, 2021)

Signed-in manual profile refreshes enqueue `FullHistoryBackfillJob` after the normal quick refresh finishes. The job runs on the dedicated `history-backfill` Hangfire queue with its own small worker pool, so deep selected-player history scans do not block the high-priority refresh lane or the current-patch discovery lane. It persists compact `SummonerMatchFacts` and active-season ranked solo/duo aggregates instead of raw `Matches`, so the weekly old-patch archive/prune job does not remove the selected player's profile history.

### Summoner Maintenance

`Jobs:SummonerMaintenance` supports:

- `MaxCandidateSummonersPerRun`
- `MaxRefreshJobsToQueuePerRun`
- `DataStaleAfterMinutes`
- `RefreshLockMinutes`
- `PrioritizeFavoriteSummoners`
- `PrioritizeTrackedHighValueSummoners`
- `PrioritizeRankedHighEloSummoners`
- `EnableAllModesWidening` (default **`false`**) — when off, low-priority maintenance refreshes are current-patch ranked-head-only; when on, they may widen into all-modes-head + non-ranked backfill if the adaptive budget reports `IncludeAllModes`. Off by default because the widening grinds an uncovered summoner's ancient history through the rate gate and saturates the discovery lane. `ChampionAnalyticsIngestionJob` is always ranked-head-only.
- `HighEloTiers`
- `PauseWhenApiPriorityRefreshActive`
- `NewPatchRampHours`
- `RampMaxCandidateSummonersPerRun`
- `RampMaxRefreshJobsToQueuePerRun`
- `RampDataStaleAfterMinutes`

This recurring job refreshes stale summoners in low-priority mode when no active high-priority API refresh lock exists.

### Refresh Lock Lifecycle Cleanup and Telemetry

`Jobs:Schedule` refresh-lock lifecycle settings:

- `RefreshLockLifecycleCleanupCron` (default `*/5 * * * *`)
- `EnableRefreshLockLifecycleCleanup` (default `true`)
- `RefreshLockLifecycleForensicsWindowMinutes` (default `30`)
- `RefreshLockLifecycleCleanupBatchSize` (default `250`, development profile `100`)
- `RefreshLockLifecycleCleanupMaxBatchesPerRun` (default `8`, development profile `4`)

Telemetry emitted by refresh lock lifecycle instrumentation uses consistent tags:

- `lock_class`
- `platform_region`
- `outcome`
- `source`

Metric names:

- `transcendence.refresh_lock.lifecycle.events`
- `transcendence.refresh_lock.contention.wait_hint_seconds`
- `transcendence.refresh_lock.cleanup.deleted`
- `transcendence.refresh_lock.cleanup.duration_ms`
- `transcendence.refresh_lock.growth.active`
- `transcendence.refresh_lock.growth.expired`
- `transcendence.refresh_lock.growth.deleted_last_run`

Structured log event names:

- `refresh_lock.lifecycle`
- `refresh_lock.contention_wait_hint`
- `refresh_lock.cleanup`
- `refresh_lock.growth_snapshot`

Operational monitoring baseline:

- Track contention trend by lock class/region: compare `outcome=contention` against `outcome=acquired`.
- Track cleanup effectiveness: watch `growth.expired` and `growth.deleted_last_run` together; rising expired with flat deleted indicates retention pressure.
- Track cleanup health: watch `refresh_lock.cleanup` outcomes (`completed`, `canceled`, `failed`) and duration growth over time.

### Ingestion Throughput Telemetry

Throughput telemetry emitted by low-priority producers uses these tags:

- `producer`
- `queue_tier`
- `mode`
- `outcome`
- `source`

Metric names:

- `transcendence.ingestion_throughput.decisions`
- `transcendence.ingestion_throughput.defer_age_breaches`
- `transcendence.ingestion_throughput.catch_up.lifecycle`
- `transcendence.ingestion_throughput.queue_target`
- `transcendence.ingestion_throughput.queued_count`
- `transcendence.ingestion_throughput.catch_up.window_minutes`

Structured log event names:

- `ingestion_throughput.budget_decision`
- `ingestion_throughput.guardrail_decision`
- `ingestion_throughput.catch_up_window`
- `ingestion_throughput.queue_output`

Operational monitoring baseline:

- Rising `outcome=api_priority_active` with `mode=highpressure` confirms high-priority demand is preempting low-priority throughput.
- Track `defer_age_breach` and catch-up lifecycle starts to verify starvation guardrails are triggering when expected.
- Compare `queue_target` against `queued_count` and queue-output outcomes to identify throttling (`stopped_api_priority_preemption`) vs candidate scarcity (`skipped_no_candidates`).

### Timeline Ingestion

`Jobs:TimelineIngestion` supports:

- `Enabled`
- `MinuteMark`
- `MaxRetryAttempts`
- `BackfillBatchSize`
- `BackfillMaxEnqueuesPerRun`
- `BackfillCurrentPatchOnly`
- `PauseWhenApiPriorityRefreshActive`

Timeline ingestion persists ranked @15 snapshots and fetch status for matchup depth analytics.

### Rune Selection Integrity Backfill

`Jobs:Schedule` now supports:

- `RuneSelectionIntegrityBackfillCron`
- `EnableRuneSelectionIntegrityBackfill`
- `SummonerMaintenanceCron` (heartbeat cadence; the job self-paces — see below)
- `EnableSummonerMaintenance`
- `MatchTimelineBackfillCron`
- `EnableMatchTimelineBackfill`
- `ChampionAnalyticsIngestionCron` / `RefreshChampionAnalyticsAdaptiveCron` (heartbeat cadences)

> The separate `*-ramp` recurring jobs (`refresh-champion-analytics-ramp`, `champion-analytics-ingestion-ramp`, `summoner-maintenance-ramp`) and the `EnableNewPatchRamp` / `*RampCron` schedule keys were removed: new-patch ramp behavior is now folded into the base jobs, which self-pace from patch age (`SelfPaceRampIntervalMinutes` / `SelfPaceSteadyIntervalMinutes` on the producer options) and self-select ramp budget params from the existing `Ramp*` option values. See ARCHITECTURE.md → "Self-pacing (no separate ramp jobs)".

`Jobs:RuneSelectionIntegrityBackfill` supports:

- `BatchSize`
- `MaxBatchesPerRun`

### Analytics Compute Thresholds

Analytics sampling thresholds are configurable in both API and worker hosts:

- `Analytics:Compute:MinimumGamesRequired`
- `Analytics:Compute:MaturingPatchMinimumGamesRequired`
- `Analytics:Compute:EarlyPatchMinimumGamesRequired`
- `Analytics:Compute:BootstrapPatchMinimumGamesRequired`
- `Analytics:Compute:BootstrapWindowHours`
- `Analytics:Compute:ProvisionalWindowHours`
- `Analytics:Compute:MaturingWindowHours`

### Champion Tier Methodology (`Analytics:Tiering`)

Tuning knobs for the per-role-first, empirical-Bayes champion tier scorer (`ChampionTierScorer`), bound in both hosts. Defaults are baked in (no config required to run) and calibrated against live patch 16.14 scope volumes. All values are overridable without a logic redeploy:

- `Analytics:Tiering:Cutoffs:SMin` (default `0.03`) — strength-delta (win rate vs role baseline) floor for `S`
- `Analytics:Tiering:Cutoffs:AMin` (default `0.015`) — floor for `A`
- `Analytics:Tiering:Cutoffs:BMin` (default `-0.015`) — floor for `B`
- `Analytics:Tiering:Cutoffs:CMin` (default `-0.03`) — floor for `C` (below → `D`)
- `Analytics:Tiering:PriorStrengthMin` / `PriorStrengthMax` (default `50` / `2000`) — clamp on the empirical-Bayes prior strength `k`
- `Analytics:Tiering:PriorFitMinGamesFloor` / `PriorFitMinGamesCeiling` (default `20` / `200`) — bounds for the adaptive Beta-prior fit gate
- `Analytics:Tiering:PriorFitRoleVolumeShare` (default `0.0012`) — share of total role games used to scale that gate between its bounds
- `Analytics:Tiering:GradeMinGamesFloor` / `GradeMinGamesCeiling` (default `50` / `500`) — bounds for the adaptive tier eligibility gate; below the resolved gate a champion is flagged low-sample and clamped to `B`
- `Analytics:Tiering:GradeRoleVolumeShare` (default `0.003`) — share of total role games used to scale the grade gate between its bounds
- `Analytics:Tiering:ContestPickWeight` / `ContestBanWeight` (default `1` / `1`) — weights in the `contestedScore` popularity index

The computed grade is persisted in the `ChampionScopeGradeStats` table (added by the `AddChampionScopeGradeStat` migration). Because grades are recomputed on read (and re-persisted hourly), changing any of these knobs takes effect on the next refresh — no re-ingestion or backfill. Tier-list responses also expose `confidence` (`RESOLVED`, `FLAT`, or `INSUFFICIENT`) so a thin or uniform scope is not presented as a confidently balanced meta.

### Analytics Response Sampling

- Analytics APIs now expose sample metadata fields (`sampleStatus`, `sampleSize`, `minimumRecommendedSampleSize`, `patchAgeHours`, `isEarlyPatchWindow`, `patchPhase`, `isProvisional`).
- Current behavior is current-patch only (no previous-patch fallback responses).

## Documentation Policy (Contributor Requirement)

If a change affects any of the following, update docs in the same PR:

- Runtime behavior, user flows, or UI routes: update `README.md` and/or `docs/ARCHITECTURE.md`
- API endpoints, payloads, auth, or status codes: update `docs/API.md` and ensure OpenAPI is up to date
- Environment variables, secrets, compose, or run commands: update `docs/DEVELOPMENT.md`
