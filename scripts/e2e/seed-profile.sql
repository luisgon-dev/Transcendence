\set ON_ERROR_STOP on
-- Disposable local/CI database only. No Riot calls or production credentials required.
INSERT INTO "Summoners" (
  "Id", "RiotSummonerId", "ProfileIconId", "SummonerLevel", "RevisionDate",
  "Puuid", "GameName", "TagLine", "GameNameNormalized", "TagLineNormalized",
  "PlatformRegion", "Region", "UpdatedAt", "LastActiveAtUtc"
) VALUES (
  'a1111111-1111-4111-8111-111111111111', 'ci-faker', 29, 100, 0,
  'ci-profile-puuid', 'faker', 'NA1', 'FAKER', 'NA1', 'NA1', 'americas', now(), now()
) ON CONFLICT ("Id") DO UPDATE SET "UpdatedAt" = now();

-- The performance seed owns representative match facts. Give its first player
-- the profile used by the keyboard/match-expansion browser regressions.
UPDATE "Summoners" SET "GameName" = 'Kronic', "TagLine" = 'NA1',
  "GameNameNormalized" = 'KRONIC', "TagLineNormalized" = 'NA1'
WHERE "RiotSummonerId" = 'perf-summoner-1';
INSERT INTO "Patches" ("Version", "ReleaseDate", "DetectedAt", "IsActive")
VALUES ('99.1', now() - interval '7 days', now(), true)
ON CONFLICT ("Version") DO UPDATE SET "IsActive" = true;

-- Publish the synthetic patch's tier-list data for the API-only browser stack.
CREATE TEMP TABLE e2e_champion_games AS
SELECT m."PlatformRegion" AS region, r."Tier" AS rank, mp."ChampionId" AS champion,
       mp."TeamPosition" AS role, count(*)::int AS games,
       count(*) FILTER (WHERE mp."Win")::int AS wins
FROM "MatchParticipants" mp
JOIN "Matches" m ON m."Id" = mp."MatchId"
JOIN "Ranks" r ON r."SummonerId" = mp."SummonerId" AND r."QueueType" = 'RANKED_SOLO_5x5'
WHERE m."Patch" = '99.1' AND m."QueueId" = 420 AND m."Status" = 1
GROUP BY m."PlatformRegion", r."Tier", mp."ChampionId", mp."TeamPosition";

INSERT INTO "ChampionRoleTierStats"
  ("Id", "Patch", "QueueFamily", "PlatformRegion", "RankTier", "ChampionId", "Role", "Games", "Wins", "ComputedAtUtc")
SELECT gen_random_uuid(), '99.1', 'RANKED_SOLO_DUO', region, rank, champion, role, games, wins, now()
FROM e2e_champion_games
ON CONFLICT DO NOTHING;

WITH grade_atoms AS (
  SELECT scopes.scope, champion, role, sum(games)::int AS games, sum(wins)::int AS wins
  FROM e2e_champion_games
  CROSS JOIN (VALUES ('ALL'), ('EMERALD_PLUS')) AS scopes(scope)
  WHERE scopes.scope = 'ALL' OR rank IN ('EMERALD', 'DIAMOND', 'MASTER', 'GRANDMASTER', 'CHALLENGER')
  GROUP BY scopes.scope, champion, role
), primary_roles AS (
  SELECT DISTINCT ON (scope, champion) scope, champion, role, games, wins
  FROM grade_atoms
  ORDER BY scope, champion, games DESC, role
), grade_rows AS (
  SELECT scope, champion, role, role AS primary_role, games, wins FROM grade_atoms
  UNION ALL
  SELECT scope, champion, 'ALL', role, games, wins FROM primary_roles
)
INSERT INTO "ChampionScopeGradeStats" (
  "Id", "Patch", "QueueFamily", "PlatformRegion", "RankScope", "Role", "ChampionId", "PrimaryRole",
  "Tier", "StrengthScore", "WinRate", "Games", "Wins", "PickRate", "BanRate", "ContestedScore",
  "RoleBaseline", "PriorStrength", "IsLowSample", "ComputedAtUtc"
)
SELECT gen_random_uuid(), '99.1', 'RANKED_SOLO_DUO', 'ALL', scope, role, champion, primary_role,
       1, wins::double precision / games - 0.5, wins::double precision / games, games, wins,
       1.0, 0.0, 1.0, 0.5, 20.0, false, now()
FROM grade_rows
ON CONFLICT DO NOTHING;
