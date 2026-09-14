# Nightly frontend lab sweep.
#
# Runs the same runner the CI gate uses (scripts/perf/web-lab.mjs), but against the real
# deployment instead of a seeded stack, and emits Prometheus exposition instead of a pass/fail.
# Both halves are needed: CI measures fixed data so a change is attributable to a commit, this
# measures production so the numbers are true.
#
# Base is pinned to the same digest as apps/web/Dockerfile. An Alpine base was tried first and
# the prod host's containerd snapshotter could not extract its chromium layer ("mount callback
# failed ... mkdir /usr/share/pipewire"), while every Debian-based image in this repo pulls there
# without trouble. Staying on the base the rest of the fleet already uses is both the fix and the
# convention.
FROM node:22-bookworm-slim@sha256:6c74791e557ce11fc957704f6d4fe134a7bc8d6f5ca4403205b2966bd488f6b3

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
RUN npm install --no-save --omit=dev lighthouse@13.4.1 chrome-launcher@1.2.1

# Only what the runner actually needs: the runner, the shared route vocabulary and the route
# list. No workspace install, no app source.
COPY --chown=sweep:sweep scripts/perf/web-lab.mjs ./scripts/perf/web-lab.mjs
COPY --chown=sweep:sweep scripts/perf/routes.prod.json ./scripts/perf/routes.prod.json
COPY --chown=sweep:sweep packages/web-routes ./node_modules/@transcendence/web-routes

ENTRYPOINT ["node", "scripts/perf/web-lab.mjs"]
