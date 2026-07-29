# Monitoring stack (Prometheus + Grafana)

The **single source of truth** for the Transcendence observability stack. One deployable unit for both
local dev and prod. It is a separate compose from the app (repo-root `compose.yml`) but joins the app's
Docker network so Prometheus can scrape `web`/`webapi`/`service` by name.

```
config/monitoring/
  compose.yml            # base stack (local-friendly defaults, env-driven)
  compose.prod.yml       # prod overlay (admin-password file secret)
  prometheus.yml         # web, API, worker, PostgreSQL, Redis, host, and Prometheus targets
  grafana/
    provisioning/        # datasources, dashboards provider, alerting (rules + contact points)
    dashboards/          # dashboard JSON
  .env.example           # env vars (webhook, root URLs, ports…)
  secrets/               # git-ignored; holds grafana_admin_password in prod
```

## Local

Bring the app stack up first (it creates the `transcendence_transcendence-net` network), then:

```bash
cp config/monitoring/.env.example config/monitoring/.env   # optional; defaults work
cp config/monitoring/secrets/grafana_admin_password.example \
  config/monitoring/secrets/grafana_admin_password
# Replace the placeholder in secrets/grafana_admin_password before continuing.
docker compose -f config/monitoring/compose.yml up -d
# Grafana → http://localhost:3300  (admin + file-backed password)   Prometheus → http://localhost:9090
```

## Prod

Prod runs this stack at `/root/transcendence-monitoring/` (host `root@192.168.0.221`). It is **not**
part of the app's Portainer stack and is **not** touched by the `poll-deploy` pipeline — updating it is
a deliberate manual sync.

**Sync the repo → prod** (config + compose only; never overwrite `.env` or `secrets/`):

```bash
# from a checkout of this repo, on your workstation
rsync -av --delete \
  --exclude='.env' --exclude='secrets/grafana_admin_password' \
  config/monitoring/ root@192.168.0.221:/root/transcendence-monitoring/

# on prod (first time only): create the env + admin-password secret
#   /root/transcendence-monitoring/.env               -> DISCORD_ALERT_WEBHOOK_URL, GRAFANA_ROOT_URL,
#                                                         GRAFANA_COOKIE_SECURE, PROMETHEUS_EXTERNAL_URL,
#                                                         POSTGRES_EXPORTER_*
#   /root/transcendence-monitoring/secrets/grafana_admin_password  (chmod 600)

ssh root@192.168.0.221 'cd /root/transcendence-monitoring && \
  docker compose -f compose.yml -f compose.prod.yml up -d'
```

Because provisioning is bind-mounted, editing a dashboard or an alert rule in this repo and re-syncing
(then recreating Grafana) is the whole update loop — no more one-off `scp`.

Create a dedicated PostgreSQL exporter role rather than using the application owner:

```sql
CREATE ROLE transcendence_exporter LOGIN PASSWORD '<random>';
GRANT pg_monitor TO transcendence_exporter;
```

Set `POSTGRES_EXPORTER_DATABASE`, `POSTGRES_EXPORTER_USER`, and
`POSTGRES_EXPORTER_PASSWORD` in the production monitoring `.env`. Query-level metrics also require
PostgreSQL to start with `shared_preload_libraries=pg_stat_statements`, followed once by:

```sql
CREATE EXTENSION IF NOT EXISTS pg_stat_statements;
```

The app Compose enables `track_io_timing`, logs temp files of at least 64 MiB, and stages the preload
setting. Preloading requires a deliberate PostgreSQL restart; do not repeatedly restart a saturated
database just to activate metrics. The exporter continues to expose general database health without
the optional query-statements collector.

## Alerting

Grafana-provisioned alert rules live in `grafana/provisioning/alerting/`:

- `rules.yml` — web/WebAPI/worker/PostgreSQL-exporter/Redis-exporter down, PostgreSQL connection use
  above 80%, Redis rejected connections, API 5xx ratio, API p95 latency, sample-gated real-user p75
  LCP/INP/CLS degradation, matchup-generation failures/freshness, Build Lab generation health
  (unclaimed/wedged generations, lost training runs, staleness, dataset lag, rollback, empty or
  collapsed publication, and evidence/calibration gate breaches), and host disk capacity.
- `contactpoints.yml` — a `discord` receiver; URL from `DISCORD_ALERT_WEBHOOK_URL`.

`grafana/dashboards/web-vitals.json` shows route-filtered report volume, rating mix, p75 LCP/INP, and
p75 CLS from the Next.js Web Vitals endpoint. A new web process starts with empty in-memory histogram
state; Prometheus retains previously scraped samples according to its normal retention policy.

`grafana/dashboards/analytics-refresh.json` shows active matchup-generation age/size, resume attempt,
lifecycle failures/splits, and incremental source/fact throughput.

`grafana/dashboards/build-lab.json` shows active-generation age, dataset lag, time-in-`Modeling` (the
wedge detector), published estimate counts, per-status age, lifecycle events, lost training runs,
published coverage, calibration error, effective sample size, the evidence-grade mix with its
global-fallback share, and promotion drift, from the `Transcendence.BuildLab` meter described below.

See `docs/ARCHITECTURE.md` → *Metrics-based alerting* for the rule semantics and DB/Redis coverage.

### Build Lab metrics contract

The emitter is `Transcendence.Service.Core/Services/Diagnostics/BuildLabTelemetry.cs`;
`Transcendence.Service/Program.cs` registers the meter with `AddMeter(BuildLabTelemetry.MeterName)`
and **resolves the singleton at startup**. That eager resolution is load-bearing: both Build Lab jobs
ship disabled, so nothing else would construct the meter and the series would be *absent* rather than
zero — and an absent series looks exactly like a dead worker on a dashboard. The constructor also
seeds every `(phase, result)` pair on the lifecycle counter with 0, because a counter series does not
exist until something increments it.

Build Lab ships **disabled** (`Analytics:BuildLab:Enabled=false`), and every gauge is published
regardless and reports **0** when the feature is off or when no generation occupies the state it
measures. That is what lets all `trn-buildlab-*` rules guard on `> 0` with `noDataState: OK`: a
disabled feature never pages.

| Metric | Type | Labels | Meaning |
| --- | --- | --- | --- |
| `transcendence_buildlab_generation_status_age_seconds` | gauge | `status` | Seconds since the oldest generation in that status last transitioned. `status="PendingDataset"` is the dead-modeler detector (nothing claimed the row); `status="Modeling"` is the heartbeat age, i.e. the wedge detector |
| `transcendence_buildlab_active_generation_age_seconds` | gauge | — | Seconds since the active generation was promoted |
| `transcendence_buildlab_dataset_lag_seconds` | gauge | — | `now - SourceCutoffUtc` of the active generation |
| `transcendence_buildlab_published_estimates` | gauge | `kind` (`action`/`path`) | Publishable rows in the active generation |
| `transcendence_buildlab_coverage_scopes` | gauge | `scope` (`champion_role`/`matchup`) | Distinct scopes the active generation publishes at least one estimate for |
| `transcendence_buildlab_calibration_error` | gauge | `metric` (`overall_ece`/`max_time_band_ece`) | ECE from the active generation's validation metrics; 0 means *not measured* |
| `transcendence_buildlab_effective_sample_size` | gauge | `stat` (`minimum`/`mean`) | Effective sample size behind the publishable action estimates; the minimum tracks the gate boundary |
| `transcendence_buildlab_estimate_grades` | gauge | `quality` (`PUBLISHABLE`/`INSUFFICIENT`/`GLOBAL_FALLBACK`) | Action estimates by evidence grade. `GLOBAL_FALLBACK` is the fallback-frequency signal; all three are always emitted so a share can be taken from their sum |
| `transcendence_buildlab_estimate_drift` | gauge | `stat` (`mean_abs`/`max_abs`) | Absolute Adjusted WPA movement the most recent promotion introduced over keys both generations published; holds until the next promotion |
| `transcendence_buildlab_generation_events_total` | counter | `phase` (`create`/`training`/`promote`/`rollback`), `result` (`success`/`error`/`rejected`/`skipped`/`abandoned`) | Lifecycle outcomes; `rejected` is a normal gate refusal, `error` is a fault, `abandoned` is an operator fail |

The OpenTelemetry instrument names are the dotted forms (`transcendence.buildlab.generation.status_age`
etc.); the Prometheus exporter produces the `_seconds` / `_total` names above (the `{…}` units are
annotations and add no suffix). `status` values are the `BuildLabGenerationStatus` enum names
(`PendingDataset`, `Modeling`, `Candidate`, `Ready`, `Failed`, `Retired`) so the wedge rule's
`status="Modeling"` selector matches.

**Thresholds are derived from the pipeline's cadences, not chosen.** `CreateBuildLabGenerationCron`
runs daily (`15 2 * * *`) and `PromoteBuildLabGenerationCron` every 10 minutes; the promote tick is
also what reaps expired modeling leases and refreshes the gauges. The modeler polls every
`BUILD_LAB_POLL_SECONDS` (300), leases for `BUILD_LAB_LEASE_SECONDS` (900) and heartbeats every
`BUILD_LAB_HEARTBEAT_SECONDS` (60). So: a claim is healthy within ~5 min (unclaimed rule fires at 6h);
a stopped modeler is reaped within 900 + 600 = 1500s, making the Modeling heartbeat age reachable only
when the reaper itself is not running (wedge rule fires at 2700s); and at most one training run exists
per day, so a single lost run over 24h is the whole signal (the previous "twice in six hours" could
never happen).
