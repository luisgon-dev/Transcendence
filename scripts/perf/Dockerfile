# Nightly frontend lab sweep.
#
# Runs the same runner the CI gate uses (scripts/perf/web-lab.mjs), but against the real
# deployment instead of a seeded stack, and emits Prometheus exposition instead of a pass/fail.
# Both halves are needed: CI measures fixed data so a change is attributable to a commit, this
# measures production so the numbers are true.
#
# Base is pinned to the same digest as apps/web/Dockerfile, the base the rest of the fleet uses.
FROM node:26-bookworm-slim@sha256:662933cf47f013bc8e4beb31a6116448427a82057ba7c42c97e4c5ba766504c2

# Chromium from the distro rather than Puppeteer's downloader: nothing here needs a browser
# download path, and avoiding it keeps the image off `extract-zip` and its unpatched advisories.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
         chromium \
         fonts-liberation \
         ca-certificates \
    && rm -rf /var/lib/apt/lists/*

ENV CHROME_PATH=/usr/bin/chromium \
    NODE_ENV=production \
    HOME=/home/sweep

# Chromium must not run as root, and Lighthouse wants a writable HOME for its user-data-dir.
# The user is created before anything is installed so npm and COPY can write as `sweep`
# directly — a `chown -R` afterwards would duplicate the whole node_modules tree into a second
# layer (174MB in the first version of this image).
RUN useradd --system --create-home --home-dir /home/sweep --shell /usr/sbin/nologin sweep \
    && mkdir -p /app \
    && chown sweep:sweep /app

WORKDIR /app
USER sweep

# Pinned to the versions the repo resolves, so the sweep and the gate measure identically.
RUN npm install --no-save --omit=dev lighthouse@13.5.0 chrome-launcher@1.2.1

# Only what the runner actually needs: the runner, the shared route vocabulary and the route
# list. No workspace install, no app source.
COPY --chown=sweep:sweep scripts/perf/web-lab.mjs ./scripts/perf/web-lab.mjs
COPY --chown=sweep:sweep scripts/perf/lighthouse-result.mjs ./scripts/perf/lighthouse-result.mjs
COPY --chown=sweep:sweep scripts/perf/routes.prod.json ./scripts/perf/routes.prod.json
COPY --chown=sweep:sweep packages/web-routes ./node_modules/@transcendence/web-routes

ENTRYPOINT ["node", "scripts/perf/web-lab.mjs"]
