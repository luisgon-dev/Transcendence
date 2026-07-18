# Monitoring stack (Prometheus + Grafana)

The **single source of truth** for the Transcendence observability stack. One deployable unit for both
local dev and prod. It is a separate compose from the app (repo-root `compose.yml`) but joins the app's
Docker network so Prometheus can scrape `webapi`/`service` by name.

```
config/monitoring/
  compose.yml            # base stack (local-friendly defaults, env-driven)
  compose.prod.yml       # prod overlay (admin-password file secret)
  prometheus.yml         # scrape config (webapi:8080, service:9464)
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
docker compose -f config/monitoring/compose.yml up -d
# Grafana → http://localhost:3300  (admin/admin)   Prometheus → http://localhost:9090
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
#                                                         GRAFANA_COOKIE_SECURE, PROMETHEUS_EXTERNAL_URL
#   /root/transcendence-monitoring/secrets/grafana_admin_password  (chmod 600)

ssh root@192.168.0.221 'cd /root/transcendence-monitoring && \
  docker compose -f compose.yml -f compose.prod.yml up -d'
```

Because provisioning is bind-mounted, editing a dashboard or an alert rule in this repo and re-syncing
(then recreating Grafana) is the whole update loop — no more one-off `scp`.

## Alerting

Grafana-provisioned alert rules live in `grafana/provisioning/alerting/`:

- `rules.yml` — WebAPI down, Worker down (`up == 0`), API 5xx error ratio, API p95 latency.
- `contactpoints.yml` — a `discord` receiver; URL from `DISCORD_ALERT_WEBHOOK_URL`.

See `docs/ARCHITECTURE.md` → *Metrics-based alerting* for the rule semantics and DB/Redis coverage.
