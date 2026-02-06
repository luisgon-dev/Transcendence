# Phase 4: Live Game & Authentication - Context

**Gathered:** 2026-02-05
**Status:** Ready for planning

<domain>
## Phase Boundary

Deliver live game lookup and secure API access for desktop/web clients. This phase adds Spectator-based live game data (with polling and analysis) plus authentication (API keys + JWT user accounts + favorites/preferences). It explicitly builds on Phase 2 profile data and Phase 3 analytics for participant/team enrichment.

</domain>

<decisions>
## Implementation Decisions

### Identity and Lookup
- PUUID remains the canonical user identifier in data storage.
- Public lookup accepts Riot ID (`gameName#tagLine`) + region and resolves to PUUID.
- Summoner-name-only patterns are avoided for new contracts.

### Live Game Architecture
- Use Spectator API wrapper service + short-lived cache (2-minute TTL) for endpoint reads.
- Add background polling job for tracked identities with adaptive intervals:
  - 5 minutes when offline/not in game
  - 30 seconds in lobby/champ select
  - 60 seconds in-game
- Handle Spectator 404 as expected "not in game", not an error.

### Enrichment and Analysis
- Enrich live participants with rank + recent performance from existing services.
- Reuse Phase 3 analytics (matchups/build/tier) for composition scoring.
- Win probability is a deterministic heuristic in v1 (non-ML), with clear factor breakdown.

### Authentication Strategy
- Implement API key auth first for desktop app unblock (AUTH-01).
- Add JWT-based user auth for account features (AUTH-02).
- Add favorites/preferences as JWT-protected user resources (AUTH-03).
- Use ASP.NET Core multiple schemes with explicit policies (`AppOnly`, `UserOnly`, `AppOrUser`).

### Security/Operations
- API keys stored hashed (never plaintext at rest).
- Existing unprotected admin cache endpoints become policy-protected in this phase.
- RSO account linking is deferred unless required for ownership-critical workflows.

</decisions>

<specifics>
## Specific Ideas

- Return live-game response state explicitly: `offline`, `lobby`, `in_game`, `ended`.
- Include a lightweight `lastUpdatedUtc` and `dataAgeSeconds` for live responses.
- Track polling state transitions for debugging and rate-limit tuning.

</specifics>

<deferred>
## Deferred Ideas

- Riot Sign-On (RSO) full linking flow (keep compatible design, defer heavy OAuth workflow).
- ML-based win probability model.
- Push/WebSocket architecture for live updates (polling remains baseline).

</deferred>

---

*Phase: 04-live-game-auth*
*Context gathered: 2026-02-05*
