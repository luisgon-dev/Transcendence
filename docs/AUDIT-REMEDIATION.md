# Audit remediation — November 2026

This is the current implementation record for the audit of `ab7b567`. The older
149-item roadmap is historical. Work is grouped into independently verifiable
changes; production rollout is separate from local verification.

## Acceptance checklist

- [x] A01: navigation auth forwards the same trusted client address as route handlers.
- [x] A02: PostgreSQL serializes refresh/reset claims; concurrent refreshes recover one
  encrypted successor for ten seconds, then replay revokes the affected family.
- [x] A03: public auth cannot grant admin. An explicit existing-account operator command
  writes an audit event. Reserved RSO identities cannot register with passwords.
- [x] A04: migration compatibility is three-state and unknown evidence holds rollout.
- [x] A05: persistent bounded image cache, asset readiness, rollout gate and alerts.
- [x] A06: shared build inputs trigger every consuming component image.
- [x] A07: clean npm and NuGet gates, compatible dependency updates.
- [x] A08: planner tests assert `n_distinct` independently of other PostgreSQL metadata.
- [x] A09: light/dark contrast, keyboard/mobile regression coverage.
- [x] A10: indexed outbox, bounded terminal/token retention, lag/freshness telemetry.
- [x] Seeded operation-completion browser tests run in CI without skips.
- [x] Production configuration and Docker secret-context validation.
- [x] Focused service/component extractions and preserved scouting/filter context.
- [x] Data provenance/SLO documentation and updated canonical developer instructions.

## Verification

Local validation: 362 web unit tests; 575 backend unit tests and 74 PostgreSQL
integration scenarios (full-suite runs plus the added release/retention checks).
Deployment and maintenance fault tests: 20 passed. Build-input/secret-context and
ESLint discovery checks: three passed. JavaScript and NuGet dependency audits are
clean. The OpenAPI/client were regenerated and EF reports no model drift.

All 49 production-browser checks passed in a clean run with no skips or retries,
against a disposable migrated/seeded API, including
profile operation completion without skips, light/dark axe, keyboard navigation
and match expansion, mobile overflow, and the dense Build Lab layout. CI now runs
the full browser suite using the same seed. CI execution on GitHub is separate from
these local checks; no production deployment has been performed.

A web runtime image was built with session-only proxy CA mounts (the cloud
Docker daemon uses VFS, so the temporary build combines layers to fit its disk).
The non-root runtime served all four optimized asset kinds after container
replacement, retained four cache entries, and returned an optimizer cache HIT.
The cache parent is writable without root. A real Docker context rejected copying
`.env.local`. Session proxy/CA adjustments are outside the repository.

The reported post-restart image incident has no confirmed root cause: production
restart logs were unavailable. Asset checks and cache persistence address the
observable readiness gap without treating the local cloud proxy as production
evidence. Keep process liveness shallow so an upstream CDN fault cannot restart-loop
the web service.

## Rollout

1. Configure the canonical origin, private edge/CIDRs and shared durable Data
   Protection keys before upgrading API/web. Existing admin roles survive; new
   grants require the explicit existing-account operator command.
2. Apply the generated migration through the worker's migration-only path, then
   roll worker/API/web in the existing poller's migration-safe order.
3. Confirm `/api/admin/release`, dependency health, image readiness and alert
   ingestion. Preserve the image cache volume during recreation.
4. Observe a real production restart and seven days of data/usage signals before
   changing the initial SLOs or prioritizing follow-up product work. This requires
   production host evidence and remains an operational follow-up, not an inferred
   explanation of the original incident.

The first CI run exposed a populated-home LCP regression (4.09 s against the
unchanged 4 s budget). The search dialog is now fetched on first intent and the
hero splash has explicit high fetch priority and a smaller payload. A local
three-sample mobile Lighthouse median improved to 3.54 s, with zero CLS; CI retains
full route reports for diagnosis. First click, first keyboard shortcut, Escape
cancellation and focus restoration are regression-tested.
