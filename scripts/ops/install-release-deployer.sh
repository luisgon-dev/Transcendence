#!/usr/bin/env bash
# Explicit host migration; never invoked merely by merging this repository.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
command -v python3 >/dev/null
python3 -c 'import sys; assert sys.version_info >= (3, 12), "Python 3.12+ is required"'
[[ "$(id -u)" == 0 ]] || { printf '%s\n' 'Run the host installer as root.' >&2; exit 1; }
docker compose version >/dev/null
[[ -r /root/transcendence/.env && -r /root/transcendence/compose.yml ]]
# Make adoption's network intent explicit before disabling the old deployment controller.
python3 - <<'PY'
import re
from pathlib import Path
text = Path('/root/transcendence/.env').read_text()
for name in ('WEB_BIND_ADDRESS', 'API_BIND_ADDRESS'):
    if not re.search(r'(?m)^(?:export\s+)?' + name + r'\s*=\s*[^\s#]+', text):
        raise SystemExit('Set WEB_BIND_ADDRESS and API_BIND_ADDRESS explicitly in the host .env before adoption; see docs/DEPLOYMENT.md.')
PY
# Stop future legacy polls. An already-running deploy retains its lock and can finish safely.
systemctl disable --now transcendence-deploy.timer
install -D -m 0755 "$root/scripts/ops/release_bootstrap.py" /root/deploy/release_bootstrap.py
install -D -m 0644 "$root/scripts/ops/transcendence-release.service" /etc/systemd/system/transcendence-release.service
install -D -m 0644 "$root/scripts/ops/transcendence-release.timer" /etc/systemd/system/transcendence-release.timer
install -d -m 0700 /var/lib/transcendence-release
install -d -m 0755 /var/lib/transcendence-release/textfile
install -d -m 0755 /var/lib/transcendence-perf/textfile
systemctl daemon-reload
systemctl enable --now transcendence-release.timer
printf '%s\n' 'Release deployer installed. Inspect journalctl -u transcendence-release.service and /var/lib/transcendence-release/status.json.'
