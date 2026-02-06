---
phase: 04-live-game-auth
plan: 06
completed: 2026-02-05T21:27:00Z
status: complete
requires: ["04-04", "04-05"]
affects: [05-management]
---

# Phase 4 Plan 6: Favorites + Endpoint Hardening Summary

## One-Liner

Added user favorites/preferences APIs and secured previously-unprotected analytics cache invalidation endpoints.

## What Was Built

- Added personalization persistence:
  - `Transcendence.Data/Models/Auth/UserFavoriteSummoner.cs`
  - `Transcendence.Data/Models/Auth/UserPreferences.cs`
  - `Transcendence.Data/Repositories/Interfaces/IUserPreferencesRepository.cs`
  - `Transcendence.Data/Repositories/Implementations/UserPreferencesRepository.cs`
- Added personalization service layer:
  - `Transcendence.Service.Core/Services/Auth/Models/UserPreferenceDtos.cs`
  - `Transcendence.Service.Core/Services/Auth/Interfaces/IUserPreferencesService.cs`
  - `Transcendence.Service.Core/Services/Auth/Implementations/UserPreferencesService.cs`
- Added user endpoints (`UserOnly`):
  - `Transcendence.WebAPI/Controllers/UserPreferencesController.cs`
  - `GET /api/users/me/favorites`
  - `POST /api/users/me/favorites`
  - `DELETE /api/users/me/favorites/{favoriteId}`
  - `GET /api/users/me/preferences`
  - `PUT /api/users/me/preferences`
- Secured operational endpoints:
  - `POST /api/analytics/cache/invalidate` now `AppOnly`
  - `POST /api/analytics/champions/cache/invalidate` now `AppOnly`

## Decisions

- Favorites are stored using stable summoner PUUID + platform region.
- Preferences are one row per user (`UserAccountId` PK) with upsert behavior.
- Existing analytics read endpoints remain public; operational invalidation endpoints are protected.

## Verification

- EF migration created:
  - `Transcendence.Service/Migrations/20260205210835_AddUserPreferences.cs`
- `dotnet build Transcendence.sln` passes.

## Requirement Coverage

- AUTH-03 complete.

---

*Completed: 2026-02-05*
