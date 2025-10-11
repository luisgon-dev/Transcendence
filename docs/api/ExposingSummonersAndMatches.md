# API Design: Summoners and Match History

This document outlines how to expose Summoners and Match History through the ASP.NET Core Web API with DTOs to produce a stable OpenAPI schema for client code generation (e.g., Rust for a Tauri app).

## Why use DTOs?

DTOs (Data Transfer Objects) are API-facing types that:
- Decouple your public API contract from internal domain models or third-party SDK types.
- Let you shape responses (rename, flatten, hide internal IDs), control serialization, and version safely.
- Produce stable OpenAPI schemas, which is crucial for client generation.

Use DTOs in controllers and map from your service/domain models to DTOs.

## Proposed Endpoints

- GET /api/summoners/{puuid}?platform=NA1  
  Returns a SummonerDto for the given PUUID and platform (platform is a platform route like NA1, EUW1, etc.).

- GET /api/summoners/by-riot-id/{gameName}/{tagLine}?platform=NA1  
  Resolve a summoner by Riot ID (gameName + tagLine) and platform.

- GET /api/summoners/{puuid}/matches?region=AMERICAS&start=0&count=20  
  Returns a paged list of MatchSummaryDto for the given PUUID and region (AMERICAS/EUROPE/ASIA/SEA). Supports pagination with start and count.

- GET /api/matches/{matchId}?region=AMERICAS  
  Returns MatchDetailsDto for a specific match.

Notes:
- Use platform (e.g., NA1) for Summoner endpoints and regional route (e.g., AMERICAS) for Match endpoints to align with Riot APIs.
- Validation: Ensure platform/region inputs are valid enums; return ProblemDetails for errors.

## DTOs (API Contracts)

These types are designed for the public API (OpenAPI generation). Keep them stable once published.
