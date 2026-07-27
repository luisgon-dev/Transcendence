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
  LCP/INP/CLS degradation, matchup-generation failures/freshness, and host disk capacity.
- `contactpoints.yml` — a `discord` receiver; URL from `DISCORD_ALERT_WEBHOOK_URL`.

`grafana/dashboards/web-vitals.json` shows route-filtered report volume, rating mix, p75 LCP/INP, and
p75 CLS from the Next.js Web Vitals endpoint. A new web process starts with empty in-memory histogram
state; Prometheus retains previously scraped samples according to its normal retention policy.

`grafana/dashboards/analytics-refresh.json` shows active matchup-generation age/size, resume attempt,
lifecycle failures/splits, and incremental source/fact throughput.

See `docs/ARCHITECTURE.md` → *Metrics-based alerting* for the rule semantics and DB/Redis coverage.
