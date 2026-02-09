FROM node:22-bookworm-slim AS builder

WORKDIR /app

ENV NEXT_TELEMETRY_DISABLED=1

RUN corepack enable

# Copy only what we need for workspace install first (better layer caching).
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY packages/api-client/package.json packages/api-client/package.json
COPY apps/web/package.json apps/web/package.json

RUN pnpm install --frozen-lockfile

COPY packages/api-client packages/api-client
COPY apps/web apps/web

RUN mkdir -p apps/web/public

RUN pnpm --filter @transcendence/api-client build

RUN pnpm --filter web build

FROM node:22-bookworm-slim AS runner

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

CMD ["node", "apps/web/server.js"]
