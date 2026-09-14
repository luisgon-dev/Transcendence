FROM node:26-bookworm-slim@sha256:cd9f682fa2885cd1056e830424764158570061c59736a1da836bc3d73df095ae AS builder

WORKDIR /app

ENV NEXT_TELEMETRY_DISABLED=1

# Node 26 no longer bundles Corepack, so it is installed explicitly rather than pinning a pnpm
# version here — `packageManager` in the root package.json stays the single source of truth.
RUN npm install -g corepack@latest && corepack enable

# Copy only what we need for workspace install first (better layer caching).
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY .npmrc ./.npmrc
COPY openapi openapi
COPY packages/api-client/package.json packages/api-client/package.json
COPY packages/web-routes/package.json packages/web-routes/package.json
COPY apps/web/package.json apps/web/package.json

RUN pnpm install --frozen-lockfile

COPY packages/api-client packages/api-client
# Shared route-template vocabulary, imported by the Web Vitals reporter and the telemetry
# route. Plain JS + .d.ts with no build step, so it ships as source.
COPY packages/web-routes packages/web-routes
COPY apps/web apps/web

RUN mkdir -p apps/web/public

RUN pnpm --filter @transcendence/api-client build

RUN pnpm --filter web build

FROM node:26-bookworm-slim@sha256:cd9f682fa2885cd1056e830424764158570061c59736a1da836bc3d73df095ae AS runner

WORKDIR /app

ENV NODE_ENV=production
ENV PORT=3000
ENV HOSTNAME=0.0.0.0
ENV NEXT_TELEMETRY_DISABLED=1

COPY --from=builder --chown=node:node /app/apps/web/.next/standalone ./
COPY --from=builder --chown=node:node /app/apps/web/.next/static ./apps/web/.next/static
COPY --from=builder --chown=node:node /app/apps/web/public ./apps/web/public

USER node

EXPOSE 3000

# Detect a hung Next.js process (the other two services already have healthchecks). Node ships a
# global fetch, so no curl is needed in the slim runtime image.
HEALTHCHECK --interval=30s --timeout=5s --start-period=25s --retries=3 \
  CMD node -e "fetch('http://127.0.0.1:'+(process.env.PORT||3000)+'/api/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"

CMD ["node", "apps/web/server.js"]
