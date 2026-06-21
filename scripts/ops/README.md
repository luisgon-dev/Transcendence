# Ops scripts

## `poll-deploy.sh` — pull-based prod deploy (replaces wud)

Prod is a single Docker host on a private LAN with no public SSH and no tunnel, so a
cloud GitHub runner cannot push a deploy to it. The intended auto-updater, **wud**
(what's-up-docker), silently fails to detect new `:main` digests for these ghcr
packages — wud 8.2.2 (the latest release) reports `updateAvailable=false` even when
the registry digest has changed, so pushes never auto-deployed and each release had to
be recreated by hand (see task **P1.3b**).

`poll-deploy.sh` replaces wud for the three app services (`web`, `webapi`, `service`)
with a deterministic, **outbound-only** digest poll:

1. Resolve the current `:main` manifest digest from ghcr with an anonymous pull token
   (the packages are public).
2. Compare it to the digest the running container was pulled at.
3. If they differ, `docker compose pull` + `up -d --no-deps <svc>` (postgres/redis are
   never touched), then post a Discord notification via `ALERTS_WEBHOOK_URL`.

No inbound exposure, no CI secret, no self-hosted runner. A `flock` guard prevents
overlapping runs. Runs every ~60s via the systemd timer (≈ wud's old cadence).

### Install (on prod, as root)

```bash
# from a checkout / scp of scripts/ops/
install -D -m 0755 poll-deploy.sh /root/deploy/poll-deploy.sh
install -D -m 0644 transcendence-deploy.service /etc/systemd/system/transcendence-deploy.service
install -D -m 0644 transcendence-deploy.timer   /etc/systemd/system/transcendence-deploy.timer
systemctl daemon-reload
systemctl enable --now transcendence-deploy.timer
```

### Operate

```bash
systemctl list-timers transcendence-deploy.timer   # next/last run
journalctl -u transcendence-deploy.service -n 50   # deploy history
/root/deploy/poll-deploy.sh                         # force a poll now
systemctl disable --now transcendence-deploy.timer  # pause auto-deploy (e.g. during an incident)
```

Rollback is unchanged: pin a service to an immutable `:sha-<short>` tag in the compose
file and `compose up -d` it (see `docs/ARCHITECTURE.md` "Deployment & rollback").

> If wud's app-container watching is ever wanted back, note it is unreliable here; this
> poller is the source of truth. wud still works fine for the public Docker Hub
> sidecars (portainer/dozzle/grafana/prometheus).
