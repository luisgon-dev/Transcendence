# Phase 4: Live Game & Authentication - Research

**Researched:** 2026-02-05
**Domain:** Spectator-based live game workflows and dual authentication (API key + JWT)
**Confidence:** HIGH for auth middleware patterns, MEDIUM for Spectator polling intervals/state detection

## Summary

Phase 4 is implementation-ready based on existing repository research:
- Live game should use background polling + short cache (not per-request Spectator calls).
- Polling should be adaptive by game state to protect rate limits.
- Auth should use ASP.NET Core multi-scheme middleware (`X-API-Key` + JWT), with explicit policies.

## Confirmed Stack

| Area | Stack/Pattern | Rationale |
|------|---------------|-----------|
| API key auth | Custom `AuthenticationHandler` + hashed keys | Fits desktop app requirement (`X-API-Key`), no external dependency required |
| User auth | ASP.NET Core JWT Bearer | Standard built-in middleware, easy policy composition |
| Live game polling | Hangfire recurring jobs | Already in use in project; consistent operational model |
| Live game cache | HybridCache (short TTL) | Reuses existing cache infrastructure to reduce Spectator load |

## Key Constraints and Pitfalls

- Spectator timestamps can be inconsistent; track transitions using server-side state.
- Treat Spectator 404 as expected "not in game".
- Enforce PUUID-first identity model; avoid mutable-name dependency.
- Avoid plaintext API key storage and endpoint-level manual auth parsing.

## References (already in repo)

- `.planning/research/ARCHITECTURE.md` (Phase 4 section + auth scheme patterns)
- `.planning/research/PITFALLS.md` (adaptive polling + auth security pitfalls)
- `.planning/research/SUMMARY.md` (phase rationale and success criteria)

---

*Phase: 04-live-game-auth*
*Research consolidated: 2026-02-05*
